namespace MUI.Catalog;

/// <summary>
/// A value as a page shows it: what it says, where it came from, and how old it is.
/// </summary>
/// <remarks>
/// The atom of the whole interface. It appears hundreds of times per page in aggregate, and it is
/// why there is no unlabelled data anywhere on this site. <see cref="IsStale"/> is resolved here,
/// against the field's own expected-refresh window (spec §5.6), so the page, the plain-text surface
/// and the API cannot disagree about when a value has aged out.
/// </remarks>
public sealed record ProvenanceChip(
    string Value,
    FieldSource Source,
    DateTimeOffset LastConfirmedAt,
    bool IsStale)
{
    /// <summary>Whether somebody observed this, as opposed to a game asserting it.</summary>
    public bool IsMeasured => Source is FieldSource.Handshake or FieldSource.Who;
}

/// <summary>
/// One capability, measured beside declared. Two columns, never merged into one badge.
/// </summary>
/// <remarks>
/// The disagreement is the interesting case, so it is a first-class state rather than something a
/// reader has to notice. "Declared GMCP, never offered in 214 handshakes" is the single most useful
/// thing a capability matrix can say.
/// </remarks>
public sealed record CapabilityRow(
    string Protocol,
    CapabilityState Measured,
    CapabilityState Declared,
    DateTimeOffset? LastConfirmedAt)
{
    public bool Disagrees =>
        (Measured, Declared) is (CapabilityState.Absent, CapabilityState.Present)
            or (CapabilityState.Present, CapabilityState.Absent);
}

/// <summary>
/// Four states, because "we did not observe it" and "it is not there" are different facts and only
/// one of them is a measurement.
/// </summary>
public enum CapabilityState
{
    /// <summary>Nothing said either way. Renders as <c>–</c>, never as absent.</summary>
    Unknown,

    Present,

    Absent,
}

/// <summary>One hour of the activity grid. The three states of spec §5.4, ready to render.</summary>
public sealed record ActivityCell(int DayOfWeek, int Hour, int? Count, bool Probed)
{
    /// <summary>Probed and counted — including a measured zero, which is a filled cell.</summary>
    public bool IsCounted => Probed && Count is not null;

    /// <summary>Probed and could not be counted. Hatched, not empty.</summary>
    public bool IsUnmeasurable => Probed && Count is null;

    /// <summary>
    /// No measurement for that hour. Empty — and emphatically not a zero.
    /// </summary>
    /// <remarks>
    /// <b>Not measured, not "not reachable".</b> A presence row exists only when a probe got far
    /// enough to try counting, and a probe that failed writes no presence row at all — it goes to
    /// the availability writer instead (see <c>PresenceWriter</c>'s own remarks). So silence here
    /// covers both an hour we could not reach and an hour we never probed, and those are different
    /// facts about a game. Rendering silence as unreachability states our own gap as their outage,
    /// which is the one thing this site may never do: a game found an hour ago would have 167 hours
    /// of a perfect week's uptime described as downtime. Reachability has its own strip, measured
    /// from intervals that can tell the difference.
    /// </remarks>
    public bool IsGap => !Probed;
}

/// <summary>A game as the listing shows it.</summary>
public sealed record GameSummary(
    Guid Id,
    string Slug,
    string Name,
    string? Tagline,
    LifecycleState State,
    bool IsClaimed,
    int? PlayersNow,
    string? Codebase,
    IReadOnlyList<string> MeasuredProtocols);

/// <summary>A game as its own page shows it.</summary>
/// <remarks>
/// The game is the hero and the connect screen follows it as evidence (spec §9) — so the order of
/// the fields here is the order of the page, deliberately. A reader arriving from a search engine
/// needs to know what the game is in one glance, and forty lines of box-drawing does not answer that.
/// </remarks>
public sealed record GamePage(
    GameSummary Summary,
    string? Description,
    IReadOnlyList<GameEndpointView> Endpoints,
    string? ConnectScreen,
    bool ConnectScreenSuppressed,
    double? ReachableFraction,
    TimeSpan? LongestOutage,
    IReadOnlyList<CapabilityRow> Capabilities,
    IReadOnlyList<ActivityCell> Activity,
    IReadOnlyDictionary<string, ProvenanceChip> Declared,
    IReadOnlyList<ChangeEntry> Changes)
{
    public int DisagreementCount => Capabilities.Count(c => c.Disagrees);
}

public sealed record GameEndpointView(string Host, int Port, string Kind, bool TlsMeasured);

public sealed record ChangeEntry(DateTimeOffset At, string Summary);

/// <summary>What the listing was asked for. A plain GET form's worth of state and nothing more.</summary>
public sealed record GameFilter
{
    public string? Text { get; init; }

    public bool IncludeArchived { get; init; }

    public IReadOnlyList<string> MeasuredProtocols { get; init; } = [];

    public ActivityBand? Band { get; init; }
}

/// <summary>
/// The activity facet (spec §5.2). A game whose counts are all unmeasurable is
/// <see cref="Quiet"/>, never <see cref="Dark"/> — being uncountable is not being absent.
/// </summary>
public enum ActivityBand
{
    PlayersNow,
    ActiveThisWeek,
    Quiet,
    Dark,
    Archived,
}

/// <summary>
/// Everything the site reads. Deliberately separate from the write-side stores.
/// </summary>
/// <remarks>
/// The seam that lets the web tier be built before the database exists: pages depend on this, a
/// fixture implements it now, and Postgres implements it later without a page changing. It returns
/// view models rather than rows for the same reason the plain surface exists — if a page had to
/// assemble a fact from three tables, the plain renderer would have to repeat that assembly and the
/// two would drift.
/// </remarks>
public interface IGameQueries
{
    Task<IReadOnlyList<GameSummary>> ListAsync(GameFilter filter, CancellationToken cancellationToken = default);

    Task<GamePage?> FindAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>The three liveness feeds (spec §9) — the differentiator no incumbent can publish.</summary>
    Task<LivenessFeeds> FeedsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Codebase share and protocol adoption over the measured set (spec §9). Shares, never totals.
    /// </summary>
    Task<EcosystemDashboard> EcosystemAsync(CancellationToken cancellationToken = default);

    /// <summary>Rankings computed from measured data only (spec §9). There is no vote anywhere.</summary>
    Task<Rankings> RankingsAsync(CancellationToken cancellationToken = default);
}

public sealed record LivenessFeeds(
    IReadOnlyList<FeedEntry> NewlyDiscovered,
    IReadOnlyList<FeedEntry> WentDark,
    IReadOnlyList<FeedEntry> CameBack);

public sealed record FeedEntry(string Slug, string Name, DateTimeOffset At, string Detail);
