namespace MUI.Discovery;

/// <summary>
/// One address the crawler probes, on its own schedule, for ever.
/// </summary>
/// <remarks>
/// Spec §7.1: the moment a host answers it is promoted to a target with its own
/// <c>next_probe_at</c> and is probed forever after on its own account. <see cref="DiscoveredFromGameId"/>
/// is provenance, not a dependency — it lets a hostile or careless <c>REFERRAL</c> list be traced and
/// its subtree pruned (§7.2), and a target whose referring game disappears stays due on schedule.
/// <para>
/// <b>There is no retirement flag. Do not add one</b> — not <c>Retired</c>, not <c>RetiredAt</c>,
/// not <c>Enabled</c>, and not a <c>NextProbeAt</c> of <see cref="DateTimeOffset.MaxValue"/> meaning
/// "never". §7.4 requires a game dark for years to still be probed weekly, forever, including after
/// archiving, because a returning game re-listing itself with no human involved is the point.
/// <c>CrawlTargetTests.NothingInTheRegistryCanRetireATarget</c> enforces this by reflection, so a
/// well-meant addition fails a test rather than quietly ending a game's listing.
/// </para>
/// </remarks>
public sealed record CrawlTarget
{
    public required Guid Id { get; init; }

    /// <summary>Null until the host answered for itself (spec §7.2).</summary>
    public Guid? GameId { get; init; }

    public required string Host { get; init; }

    public required int Port { get; init; }

    public bool UseTls { get; init; }

    public required DateTimeOffset NextProbeAt { get; init; }

    public int ConsecutiveFailures { get; init; }

    /// <summary>The server's own request, honoured as a floor under the interval (spec §7.7, §11).</summary>
    public TimeSpan? CrawlDelay { get; init; }

    public required DateTimeOffset FirstSeenAt { get; init; }

    /// <summary>
    /// The encoding an operator has said this game's bytes are in — its <c>CHARSET</c> field at
    /// source <c>staff</c> — or null, which is nearly every target.
    /// </summary>
    /// <remarks>
    /// Travels with the target because the probe has no database and must know the encoding before
    /// decoding a byte. Read-only: the one property here that belongs to the game rather than the
    /// address, so it is loaded with the row and never written back through it.
    /// </remarks>
    public string? Charset { get; init; }

    public DateTimeOffset? LastProbedAt { get; init; }

    public Guid? DiscoveredFromGameId { get; init; }

    public int Depth { get; init; }

    /// <summary>
    /// Exempts this target from <see cref="HostScopeGuard"/>'s resolved-address check (spec §7.2).
    /// </summary>
    /// <remarks>
    /// True only for an address a human operator configured directly (e.g. <c>127.0.0.1</c> for local
    /// testing) — never for a stranger's <c>REFERRAL</c> list. <b>Defaults to false</b>, which is the
    /// security-relevant half: every referral and import is constructed without setting it, so the
    /// un-configured case is always the guarded behaviour.
    /// </remarks>
    public bool IsOperatorSeed { get; init; }

    /// <summary>
    /// When somebody handed us this address through the public form, or null when we found it
    /// ourselves (spec §7.6, migration 0010).
    /// </summary>
    /// <remarks>
    /// A submission creates no game — a game exists only once a host answers for itself (§7.1) — so
    /// this holds the fact until <c>CatalogueBinder</c> mints one and copies it across. It keeps a
    /// submitted game off every public surface until somebody claims it (§8).
    /// <para>
    /// <b>Nothing sets this on a target that already exists</b> — <see cref="ICrawlTargetRepository.AddAsync"/>
    /// collapses onto the existing row and changes nothing but depth, so submitting an address we
    /// already crawl is a no-op. Otherwise the form would be a way to hide any listed game by naming it.
    /// </para>
    /// </remarks>
    public DateTimeOffset? SubmittedAt { get; init; }
}

/// <summary>
/// The registry. Monotonic by construction: there is no delete, no retire, and no method that can
/// stop a target being probed (spec §7.1, §7.4).
/// </summary>
/// <remarks>
/// The absence is the contract: <c>CrawlTargetTests.NothingInTheRegistryCanRetireATarget</c> reads
/// this interface's members by reflection and fails on any name that sounds like removal.
/// </remarks>
public interface ICrawlTargetRepository
{
    Task<CrawlTarget?> ByAddressAsync(string host, int port, CancellationToken ct);

    /// <summary>
    /// Adds a target, or returns the existing one's id. Never resets or resurfaces a known address's
    /// schedule — a repeat sighting may only lower the recorded depth.
    /// </summary>
    Task<Guid> AddAsync(CrawlTarget target, CancellationToken ct);

    Task<IReadOnlyList<CrawlTarget>> DueAsync(DateTimeOffset now, int limit, CancellationToken ct);

    Task RecordAttemptAsync(
        Guid id,
        DateTimeOffset at,
        bool succeeded,
        TimeSpan? crawlDelay,
        DateTimeOffset nextProbeAt,
        CancellationToken ct);

    Task AttachGameAsync(Guid id, Guid gameId, CancellationToken ct);
}
