using MUI.Crawl;
using MUI.Discovery;

namespace MUI.Crawler;

/// <summary>
/// Everything the hosted crawler is allowed to do, and where it starts from on day one.
/// </summary>
/// <remarks>
/// The per-cycle bounds are <see cref="DiscoveryOptions"/>' and the per-probe bounds are
/// <see cref="ProbeOptions"/>'; this adds only what a deployment owns — whether the loop runs at all,
/// which advisory lock it competes for, and the addresses it knows before any referral has been
/// followed.
/// </remarks>
public sealed record CrawlerOptions
{
    /// <summary>
    /// Off makes the deployable a pure web tier — the hosted service still starts, says so once and
    /// stands down, because a replica that was meant to crawl and is not looks exactly like one that
    /// was told not to. The advisory lock already guarantees one crawler, so this is a deliberate
    /// choice about where the crawl runs rather than a safety net.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Which advisory lock this deployment's crawler competes for (spec §12).</summary>
    public long AdvisoryLockKey { get; init; } = AdvisoryLease.CrawlKey;

    /// <summary>Per-cycle bounds: concurrency, batch size, rate floors, the probe timeout.</summary>
    public DiscoveryOptions Discovery { get; init; } = new();

    /// <summary>Per-probe bounds: session timeout, settle periods, subnegotiation ceiling.</summary>
    public ProbeOptions Probe { get; init; } = new();

    /// <summary>What one source may put through the public submission form (spec §9).</summary>
    public SubmissionOptions Submissions { get; init; } = new();

    /// <summary>
    /// When presence is rolled up, how far ahead its partitions are made, and how long each grain is
    /// kept (spec §5.2, §15.4). Runs on its own advisory lock and its own schedule.
    /// </summary>
    public PresenceMaintenanceOptions Maintenance { get; init; } = new();

    /// <summary>
    /// How often the hashing salt behind §11's aggregates rotates. Never persisted, never logged, and
    /// the reason a unique-player estimate is possible inside an epoch and not across two.
    /// </summary>
    public SaltRotationOptions Salt { get; init; } = new();

    /// <summary>
    /// Addresses the crawler knows before it has followed anything.
    /// </summary>
    /// <remarks>
    /// <b>They matter only on day one.</b> §7.1's effective seed set is every game ever found, growing
    /// monotonically, so adding a seed here later is a convenience rather than a mechanism. Seeding is
    /// idempotent: a seed already in the registry keeps its own schedule and is not dragged forward.
    /// </remarks>
    public IReadOnlyList<CrawlSeed> Seeds { get; init; } = [];

    /// <summary>
    /// Whether the crawler applies pending migrations before its first cycle.
    /// </summary>
    /// <remarks>
    /// True by default and it runs under the advisory lock, so exactly one replica migrates and the
    /// rest wait behind the lock they could not take. <see cref="MUI.Catalog.Persistence.MigrationRunner"/>
    /// is idempotent regardless.
    /// </remarks>
    public bool ApplyMigrations { get; init; } = true;

    /// <summary>Throws on a setting that could only have come from a typo or a hand-edited file.</summary>
    public void Validate()
    {
        Discovery.Validate();
        Probe.Validate();
        Maintenance.Validate();
        Salt.Validate();
        Submissions.Validate();

        foreach (var seed in Seeds)
        {
            seed.Validate();
        }
    }
}

/// <summary>One address a deployment configured by hand.</summary>
/// <param name="Host">The host name or literal address.</param>
/// <param name="Port">The port.</param>
/// <param name="IsOperatorSeed">
/// Whether this address is exempt from <see cref="HostScopeGuard"/>'s resolved-address gate.
/// <b>False by default, and that is the security-relevant half</b> (§7.2): the exemption exists so
/// somebody can point the crawler at their own <c>127.0.0.1</c> and mean it, and "operator-supplied
/// seeds <em>may</em> be exempted" is not "configured therefore exempt". A seed pointing somewhere it
/// should not go is refused like any other unless a human has said, per address, that they meant it.
/// </param>
public sealed record CrawlSeed(string Host, int Port, bool IsOperatorSeed = false)
{
    /// <summary>
    /// Reads the <c>host:port</c> a person writes — and the bracketed IPv6 form, because a seed list
    /// is exactly where somebody writes one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lives here rather than in whichever surface reads a seed list, because <c>mui-crawl</c>'s
    /// <c>--seed</c> and a deployment's seed environment variable have to agree about what an address
    /// is; two parsers would eventually disagree about a bracketed address and only one of them would
    /// be tested. Anything that is not host:port throws rather than being skipped: a seed silently
    /// dropped is a crawl that quietly never dialled what it was pointed at.
    /// </para>
    /// <para>
    /// <b>An ambiguous address is refused rather than guessed at.</b> The first version fell back to
    /// the last colon whenever the brackets did not match, so <c>[2001:db8::1:4201</c> was accepted
    /// as <c>2001:db8::1</c> port 4201 and a bare <c>2001:db8::1:4201</c> was split the same way —
    /// a parser inventing the writer's intent from a typo. It could not reach a private address that
    /// way (every target is resolved and ruled on by <see cref="HostScopeGuard"/> before it is
    /// dialled, and the string the parser produced is the string that gets gated), but it could dial
    /// a host nobody wrote down, which rule 4 says a parser does not get to do.
    /// </para>
    /// </remarks>
    public static CrawlSeed Parse(string value, bool isOperatorSeed = false)
    {
        ArgumentNullException.ThrowIfNull(value);

        var text = value.Trim();
        string host;
        int colon;

        if (text.StartsWith('['))
        {
            // Brackets are the only unambiguous way to write an IPv6 literal with a port, so they
            // have to close, close once, and be followed immediately by the colon.
            var close = text.IndexOf(']', StringComparison.Ordinal);

            if (close < 0 || close + 1 >= text.Length || text[close + 1] != ':')
            {
                throw new ArgumentException(
                    $"'{value}' opens a bracket it does not close with ']:'; a bracketed address is "
                    + "written [host]:port.");
            }

            host = text[1..close];
            colon = close + 1;

            if (host.AsSpan().ContainsAny('[', ']'))
            {
                throw new ArgumentException($"'{value}' is not [host]:port.");
            }
        }
        else
        {
            colon = text.LastIndexOf(':');
            host = colon > 0 ? text[..colon] : string.Empty;

            if (host.Contains(':', StringComparison.Ordinal))
            {
                // The last colon of a bare IPv6 literal is part of the address as often as it is a
                // port separator, and nothing in the string says which. Refusing beats choosing.
                throw new ArgumentException(
                    $"'{value}' has more than one colon and no brackets, so there is no telling the "
                    + "port from the address; write it as [host]:port.");
            }
        }

        if (colon <= 0 || !int.TryParse(text[(colon + 1)..], out var port))
        {
            throw new ArgumentException($"'{value}' is not host:port.");
        }

        var seed = new CrawlSeed(host, port, isOperatorSeed);
        seed.Validate();

        return seed;
    }

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Host);

        if (Port is < 1 or > 65535)
        {
            throw new ArgumentException($"Seed {Host}:{Port} does not name a port.");
        }
    }

    public override string ToString() => Host.Contains(':') ? $"[{Host}]:{Port}" : $"{Host}:{Port}";
}
