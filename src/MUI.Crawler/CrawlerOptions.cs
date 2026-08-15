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
    public long AdvisoryLockKey { get; init; } = CrawlLease.DefaultKey;

    /// <summary>Per-cycle bounds: concurrency, batch size, rate floors, the probe timeout.</summary>
    public DiscoveryOptions Discovery { get; init; } = new();

    /// <summary>Per-probe bounds: session timeout, settle periods, subnegotiation ceiling.</summary>
    public ProbeOptions Probe { get; init; } = new();

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
    /// Lives here rather than in whichever surface reads a seed list, because <c>mui-crawl</c>'s
    /// <c>--seed</c> and a deployment's seed environment variable have to agree about what an address
    /// is; two parsers would eventually disagree about a bracketed address and only one of them would
    /// be tested. Anything that is not host:port throws rather than being skipped: a seed silently
    /// dropped is a crawl that quietly never dialled what it was pointed at.
    /// </remarks>
    public static CrawlSeed Parse(string value, bool isOperatorSeed = false)
    {
        ArgumentNullException.ThrowIfNull(value);

        var text = value.Trim();
        var colon = text.StartsWith('[') && text.Contains("]:", StringComparison.Ordinal)
            ? text.IndexOf("]:", StringComparison.Ordinal) + 1
            : text.LastIndexOf(':');

        if (colon <= 0 || !int.TryParse(text[(colon + 1)..], out var port))
        {
            throw new ArgumentException($"'{value}' is not host:port.");
        }

        var seed = new CrawlSeed(text[..colon].Trim('[', ']'), port, isOperatorSeed);
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
