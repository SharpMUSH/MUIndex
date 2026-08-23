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
    /// stands down, rather than looking identical to a replica that lost the advisory lock.
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
    /// The Intermud-3 pass: whether it runs, where the sidecar is, and how hard to lean on the
    /// network. Runs on its own advisory lock and its own schedule, and is off by default.
    /// </summary>
    public I3ServiceOptions I3 { get; init; } = new();

    /// <summary>
    /// The AresCentral pass: whether it runs, and the credentials the hub issued. Its own advisory
    /// lock and its own schedule. On by default — a GET against a documented API registers nothing —
    /// but a host that finds no credentials turns it off, so a deployment without them is unchanged.
    /// </summary>
    public AresServiceOptions Ares { get; init; } = new();

    /// <summary>
    /// The §8.3 sweep that reads TXT records for the games somebody is mid-claim on. Its own advisory
    /// lock and its own schedule; on by default, since it dials nothing and its cost is set by how
    /// many people are claiming rather than by the size of the catalogue.
    /// </summary>
    public DnsClaimSweepOptions DnsClaims { get; init; } = new();

    /// <summary>
    /// Addresses the crawler knows before it has followed anything.
    /// </summary>
    /// <remarks>
    /// Matter only on day one: §7.1's effective seed set grows monotonically from every game ever
    /// found, so adding a seed later is a convenience, not a mechanism. Idempotent — a seed already in
    /// the registry keeps its own schedule.
    /// </remarks>
    public IReadOnlyList<CrawlSeed> Seeds { get; init; } = [];

    /// <summary>
    /// Whether the crawler applies pending migrations before its first cycle.
    /// </summary>
    /// <remarks>
    /// Runs under the advisory lock, so exactly one replica migrates.
    /// <see cref="MUI.Catalog.Persistence.MigrationRunner"/> is idempotent regardless.
    /// </remarks>
    public bool ApplyMigrations { get; init; } = true;

    /// <summary>Throws on a setting that could only have come from a typo or a hand-edited file.</summary>
    public void Validate()
    {
        Discovery.Validate();
        Probe.Validate();
        Maintenance.Validate();
        Submissions.Validate();

        // Validated here, not only by the pass itself, because AddMuiCrawler calls this at
        // registration: an enabled pass with no credentials otherwise registers cleanly and then
        // dies when the hosted service starts, and an exception out of a BackgroundService takes
        // the site with it. A startup error naming the setting beats that.
        Ares.Validate();

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
/// <b>False by default — security-relevant</b> (§7.2): the exemption exists so somebody can point the
/// crawler at their own <c>127.0.0.1</c> and mean it, never as "configured therefore exempt".
/// </param>
public sealed record CrawlSeed(string Host, int Port, bool IsOperatorSeed = false)
{
    /// <summary>
    /// Reads the <c>host:port</c> a person writes — and the bracketed IPv6 form, because a seed list
    /// is exactly where somebody writes one.
    /// </summary>
    /// <remarks>
    /// Lives here rather than in whichever surface reads a seed list, so <c>mui-crawl</c>'s
    /// <c>--seed</c> and a deployment's seed environment variable agree about what an address is.
    /// Anything that isn't host:port throws rather than being skipped — a seed silently dropped is a
    /// crawl that quietly never dialled what it was pointed at. An ambiguous address is refused rather
    /// than guessed at: every target is still resolved and ruled on by
    /// <see cref="HostScopeGuard"/> before dialling, but a parser guessing at intent could dial a host
    /// nobody actually wrote down.
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
