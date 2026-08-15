namespace MUI.Discovery;

/// <summary>
/// One address the crawler probes, on its own schedule, for ever.
/// </summary>
/// <remarks>
/// <para>
/// Spec §7.1: "the moment a host answers, it is promoted to a <c>CrawlTarget</c> with its own
/// independent <c>next_probe_at</c> and is probed forever after on its own account". The referral that
/// found it is provenance, not a dependency — <see cref="DiscoveredFromGameId"/> exists so a hostile
/// or careless <c>REFERRAL</c> list can be traced and its whole subtree pruned (§7.2), and a target
/// whose referring game disappears is still due on schedule.
/// </para>
/// <para>
/// <b>There is no retirement flag, and adding one would be the single worst regression this
/// repository could ship.</b> Not a <c>Retired</c> boolean, not a <c>RetiredAt</c>, not an
/// <c>Enabled</c>, and not a <c>NextProbeAt</c> of <see cref="DateTimeOffset.MaxValue"/> meaning
/// "never" — the last is the folklore version of the same bug and is just as final. §7.4 says a game
/// dark for two years is still probed weekly, forever, including after archiving, because a returning
/// game re-listing itself with no human involved is the thing every incumbent failed at.
/// <c>CrawlTargetTests.NothingInTheRegistryCanRetireATarget</c> holds the absence in place by
/// reflection, so a well-meant addition fails a test rather than quietly ending a game's listing.
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

    public DateTimeOffset? LastProbedAt { get; init; }

    public Guid? DiscoveredFromGameId { get; init; }

    public int Depth { get; init; }

    /// <summary>
    /// Exempts this target from <see cref="HostScopeGuard"/>'s resolved-address check (spec §7.2).
    /// </summary>
    /// <remarks>
    /// True only for an address a human operator configured. Someone pointing the crawler at
    /// <c>127.0.0.1</c> to test against their own server has said what they mean; a stranger's
    /// <c>REFERRAL</c> list has not. <b>The default is false, and that is the security-relevant half of
    /// this property</b> — every referral and every import is constructed by code that never mentions
    /// it, and what they get by not thinking about it must be the guarded behaviour.
    /// </remarks>
    public bool IsOperatorSeed { get; init; }

    /// <summary>
    /// When somebody handed us this address through the public form, or null when we found it
    /// ourselves (spec §7.6, migration 0010).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A submission creates no game — §7.1's promotion is unchanged, and a game exists when a host
    /// answers for itself — so this is where the fact waits until <c>CatalogueBinder</c> mints one and
    /// copies it across. It is what keeps a submitted game off every public surface until somebody
    /// claims it (§8).
    /// </para>
    /// <para>
    /// <b>Nothing sets this on a target that already exists</b>, and that is a security property
    /// rather than an optimisation: <see cref="ICrawlTargetRepository.AddAsync"/> collapses onto the
    /// existing row and changes nothing but depth, so submitting an address we already crawl is a
    /// no-op. Otherwise the form would be a way to hide any listed game on the site by naming it.
    /// </para>
    /// </remarks>
    public DateTimeOffset? SubmittedAt { get; init; }
}

/// <summary>
/// The registry. Monotonic by construction: there is no delete, no retire, and no method that can
/// stop a target being probed (spec §7.1, §7.4).
/// </summary>
/// <remarks>
/// The absence is the contract. <c>CrawlTargetTests.NothingInTheRegistryCanRetireATarget</c> reads
/// this interface's members by reflection and fails on any name that sounds like removal, so
/// "nothing is ever retired" is a tested state rather than a convention somebody has to remember.
/// </remarks>
public interface ICrawlTargetRepository
{
    Task<CrawlTarget?> ByAddressAsync(string host, int port, CancellationToken ct);

    /// <summary>
    /// Adds a target, or returns the existing one's id. Adding a known address never resets its
    /// schedule and never resurfaces it — the only thing a repeat sighting may change is the recorded
    /// depth, and only downward.
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
