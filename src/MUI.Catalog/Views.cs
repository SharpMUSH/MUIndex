namespace MUI.Catalog;

/// <summary>
/// A value as a page shows it: what it says, where it came from, and how old it is.
/// </summary>
/// <remarks>
/// <see cref="IsStale"/> is resolved here, against the field's own expected-refresh window (spec
/// §5.6), so the page, the plain-text surface and the API cannot disagree about when a value has
/// aged out.
/// </remarks>
public sealed record ProvenanceChip(
    string Value,
    FieldSource Source,
    DateTimeOffset LastConfirmedAt,
    bool IsStale)
{
    /// <summary>
    /// Whether somebody observed this, as opposed to a game reporting it.
    /// </summary>
    /// <remarks>
    /// Resolved through <see cref="FieldSources"/> so every surface — this chip, the API's
    /// <c>playersNowState</c>, a game's own embedded badge — agrees on one predicate.
    /// </remarks>
    public bool IsMeasured => FieldSources.IsMeasured(Source);
}

/// <summary>
/// One capability, measured beside declared. Two columns, never merged into one badge.
/// </summary>
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
    /// Not measured, not "not reachable" — a failed probe writes no presence row at all (see
    /// <c>PresenceWriter</c>), so this covers both an unreachable hour and an unprobed one. Never
    /// render it as downtime (rule 2); reachability has its own strip, measured from intervals.
    /// </remarks>
    public bool IsGap => !Probed;
}

/// <summary>A game as the listing shows it.</summary>
/// <remarks>
/// <para>
/// <see cref="LastReachableAt"/> null means never reached, distinct from "reachable a long time
/// ago" — never rendered as the older of the two.
/// </para>
/// <para>
/// <see cref="PlayersNowProvenance"/> and <see cref="CodebaseProvenance"/> label the two bare values
/// above them, on the summary because the listing needs to distinguish a `WHO` read minutes ago from
/// a game's own six-year-old claim. Null only where the value beside it is null.
/// </para>
/// <para>
/// <see cref="HasIcon"/> is a flag, not the image — no age, no provenance. It covers "no `ICON`
/// field", "fetch failed" and "no database" alike, all rendering the monogram: a failed fetch of
/// ours is not a fact about the game (rule 5).
/// </para>
/// <para>
/// <see cref="PlayersOverWindow"/> is filled only when the listing sorts on it. Null means either
/// "not asked" or "asked, nothing countable" — <c>GameSorting.IsUnranked</c> resolves which.
/// </para>
/// </remarks>
public sealed record GameSummary(
    Guid Id,
    string Slug,
    string Name,
    string? Tagline,
    LifecycleState State,
    bool IsClaimed,
    int? PlayersNow,
    string? Codebase,
    IReadOnlyList<string> MeasuredProtocols,
    DateTimeOffset? LastReachableAt = null,
    ProvenanceChip? PlayersNowProvenance = null,
    ProvenanceChip? CodebaseProvenance = null,
    bool HasIcon = false,
    PresenceWindow? PlayersOverWindow = null);

/// <summary>
/// What a game's counts added up to over one window — the basis a window sort ranks on (spec §9).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Median"/>, not a mean: a mean is pulled around by the one evening a game got linked
/// from somewhere. It is an observed count — the walk takes the first value whose running frequency
/// reaches the half-way position, never an average of two.
/// </para>
/// <para>
/// Both figures are over counted samples alone; an uncountable or unprobed hour writes no count and
/// may not be ranked as zero. <see cref="Samples"/> is the tally the median was taken over, not the
/// number of hours in the window, and is carried alongside it — a median needs its sample size
/// published beside it, or a game probed four times looks the same as one probed three hundred.
/// </para>
/// <para>
/// <see cref="Peak"/> is the largest single reading, not a simultaneous total.
/// </para>
/// <para>
/// <see cref="Window"/> is the span asked for; the far end snaps back to a whole UTC day because the
/// surviving rollup is bucketed by day. See <c>NpgsqlGameQueries.PlayersOverWindowAsync</c>.
/// </para>
/// </remarks>
public sealed record PresenceWindow(TimeSpan Window, int Median, int Peak, int Samples);

/// <summary>A game as its own page shows it.</summary>
/// <remarks>
/// Field order here is deliberately the order of the page (spec §9): the game first, the connect
/// screen as evidence after it.
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
    IReadOnlyList<ChangeEntry> Changes,

    // §9's referral neighbours. Defaulted: no neighbours is the ordinary case, not a missing read.
    IReadOnlyList<ReferralNeighbour>? Neighbours = null,

    // Encoding ConnectScreen was read with (operator's CHARSET override, or the crawler's strict
    // decoder). Ours, not the game's, so it rides here rather than in Declared. Null for UTF-8/ASCII.
    string? ConnectScreenCharset = null,

    // Why an editor ruled this address out (migration 0024). Null for every other state, including
    // `unlisted`, which is not our argument to make.
    string? ExcludedReason = null,

    // Ways to reach this game's people, in render order (see QuickLinks). Defaulted for the same
    // reason Neighbours is: no address at all is the ordinary case, not a missed read.
    IReadOnlyList<QuickLink>? Reachable = null)
{
    public int DisagreementCount => Capabilities.Count(c => c.Disagrees);

    /// <summary>Games this one points at, and games that point at it — measured, not curated.</summary>
    public IReadOnlyList<ReferralNeighbour> Referrals => Neighbours ?? [];

    /// <summary>
    /// Where this game's people are — its site, its rooms, its inbox.
    /// </summary>
    /// <remarks>
    /// Every entry is also in <see cref="Declared"/> with its full provenance chip — not a
    /// duplication, these are the same facts rendered as navigation.
    /// </remarks>
    public IReadOnlyList<QuickLink> Links => Reachable ?? [];
}

/// <summary>Which way a referral runs.</summary>
public enum ReferralDirection
{
    /// <summary>This game's own list names them. A fact about what this game published.</summary>
    Lists,

    /// <summary>Their list names this game. A fact about what they published.</summary>
    ListedBy,
}

/// <summary>
/// One end of a referral edge, resolved to a game we know (spec §9).
/// </summary>
/// <remarks>
/// A measurement of the referrer, not the referred: an edge says "this list named that address on
/// this date", never "these games are related". Direction is kept because they're different claims
/// by different people. Only edges resolved to a known game appear — an unnameable neighbour is
/// omitted rather than invented.
/// </remarks>
public sealed record ReferralNeighbour(
    string Slug,
    string Name,
    string Host,
    int Port,
    ReferralDirection Direction,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,

    // False means the list stopped naming it, never that the edge was deleted.
    bool Present);

/// <summary>
/// One address a game answers on, with the history the table has always kept (spec §9).
/// </summary>
/// <remarks>
/// <see cref="State"/> is the table's own vocabulary (<c>active</c>, <c>stale</c>, <c>gone</c>),
/// carried through rather than re-derived from the dates, so a surface cannot invent a threshold the
/// writer does not share.
/// </remarks>
public sealed record GameEndpointView(
    string Host,
    int Port,
    string Kind,
    bool TlsMeasured,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    string State)
{
    /// <summary>Whether we still reach the game here.</summary>
    public bool IsCurrent => string.Equals(State, "active", StringComparison.Ordinal);
}

public sealed record ChangeEntry(DateTimeOffset At, string Summary);

/// <summary>What the listing was asked for. A plain GET form's worth of state and nothing more.</summary>
/// <remarks>
/// <para>
/// Every member is one control on the panel and one querystring parameter, named in
/// <see cref="FacetKeys"/> — no filter state lives anywhere else.
/// </para>
/// <para>
/// <see cref="MeasuredProtocols"/> and <see cref="Tls"/> read observations; <see cref="Codebase"/>,
/// <see cref="CodebaseVersion"/>, <see cref="Family"/>, <see cref="Genre"/> and
/// <see cref="Language"/> read what a game says about itself. <see cref="Charset"/> is on the
/// measured side deliberately: what CHARSET settled on, never MSSP's claimed encoding.
/// <see cref="Lineage"/> is on neither side — a classification of ours, labelled
/// <see cref="FacetEvidence.Derived"/>.
/// </para>
/// <para>
/// <see cref="Uncounted"/> and <see cref="Unreachable"/> read the absence of a measurement, and are
/// separate switches rather than values of <see cref="Band"/> because they're independent of it and
/// of each other — neither is <see cref="ActivityBand.Quiet"/>.
/// </para>
/// </remarks>
public sealed record GameFilter
{
    public string? Text { get; init; }

    public bool IncludeArchived { get; init; }

    /// <summary>
    /// Whether games declaring adult content are in the answer (<see cref="AdultContent"/>).
    /// </summary>
    /// <remarks>
    /// Defaults to <c>true</c> — the opposite of <see cref="IncludeArchived"/> — because archiving is
    /// a catalogue-wide rule (spec §7.5) that belongs in the filter's default, while hiding adult
    /// games is a default of the listing surface alone and lives in <c>GameFilterBinding</c>. A
    /// filter built in code (data dump, home page counts, per-codebase figures) is unaffected.
    /// </remarks>
    public bool IncludeAdult { get; init; } = true;

    /// <summary>
    /// Protocols the handshake was observed offering, intersected. Never what MSSP declared —
    /// <c>capability.*.measured</c> and <c>capability.*.declared</c> are two fields for exactly this
    /// reason, and a facet reading the second would be the central lie of the project.
    /// </summary>
    public IReadOnlyList<string> MeasuredProtocols { get; init; } = [];

    /// <summary>An endpoint we completed a TLS connection to — not an <c>SSL</c> line in MSSP.</summary>
    public bool Tls { get; init; }

    public ActivityBand? Band { get; init; }

    public LastSeenBand? LastSeen { get; init; }

    /// <summary>
    /// The games we reached and hold no readable count for (<see cref="FacetKeys.Uncounted"/>).
    /// </summary>
    /// <remarks>
    /// A <see cref="FacetChoice"/>, not a <see cref="Band"/> value: it's orthogonal to every band and
    /// can be combined with any of them. It is not <see cref="ActivityBand.Quiet"/> — that band also
    /// holds a game measured at zero every hour, and treating it as "uncountable" would state our
    /// parser's failure as a fact about somebody's empty game (rules 2, 4, 5). Excluding is a
    /// decision about this listing, never a claim about a game.
    /// </remarks>
    public FacetChoice? Uncounted { get; init; }

    /// <summary>
    /// The games we could not reach recently (<see cref="FacetKeys.Unreachable"/>).
    /// </summary>
    /// <remarks>
    /// The other half of "this row has no number": could not get in at all, where
    /// <see cref="Uncounted"/> got in and could not count. Read from the availability series, never
    /// from a presence gap — see <see cref="FacetedSearch.NotReachedRecently"/>. Not the archive.
    /// </remarks>
    public FacetChoice? Unreachable { get; init; }

    public FacetChoice? Charset { get; init; }

    /// <summary>
    /// A codebase <em>family</em> — <c>PennMUSH</c>, never <c>PennMUSH 1.8.8p0</c>. Every patchlevel
    /// of one codebase is one facet value.
    /// </summary>
    /// <remarks>
    /// Matched by folding a game's codebase with <see cref="CodebaseFamily.Of"/> and comparing for
    /// equality — the same fold the panel counts, so a count stays a promise about what clicking it
    /// returns. The old <c>?codebase-family=</c> querystring spelling is still accepted and lands
    /// here.
    /// </remarks>
    public FacetChoice? Codebase { get; init; }

    /// <summary>
    /// The codebase exactly as a game reports it, <c>PennMUSH 1.8.8p0</c> and all — a counted facet
    /// over the values actually present in the catalogue.
    /// </summary>
    public FacetChoice? CodebaseVersion { get; init; }

    /// <summary>
    /// The lineage we place a codebase in: <c>MUSH</c>, <c>DikuMUD</c>. Ours rather than anybody's
    /// claim, and carried as <see cref="FacetEvidence.Derived"/> everywhere it is shown.
    /// </summary>
    /// <remarks>
    /// Not <see cref="Family"/>: MSSP's <c>FAMILY</c> vocabulary has no <c>MUSH</c> in it (PennMUSH
    /// answers <c>TinyMUD</c>), and the rest of the MUSH world publishes no MSSP at all. See
    /// <see cref="CodebaseLineage"/>.
    /// </remarks>
    public FacetChoice? Lineage { get; init; }

    /// <summary>MSSP's own <c>FAMILY</c> variable, as the game published it. See <see cref="Lineage"/>.</summary>
    public FacetChoice? Family { get; init; }

    public FacetChoice? Genre { get; init; }

    public FacetChoice? Language { get; init; }

    /// <summary>
    /// What order the answer comes back in.
    /// </summary>
    /// <remarks>
    /// Part of the filter, not separate state, so a sorted listing stays linkable. Default is
    /// <see cref="GameSort.Players"/>; must match the default in <c>GameFilterBinding</c>.
    /// </remarks>
    public GameSort Sort { get; init; } = GameSort.Players;
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
/// Lets the web tier be built before the database exists: pages depend on this, a fixture implements
/// it now, Postgres later, without a page changing. Returns view models rather than rows so the plain
/// surface doesn't have to repeat the same assembly and risk drifting from it.
/// </remarks>
public interface IGameQueries
{
    /// <summary>
    /// The listing and the facet counts that describe it, from one pass (spec §9).
    /// </summary>
    /// <remarks>
    /// One method, not a listing call plus a counts call — a facet must not advertise a count the
    /// listing can't deliver.
    /// </remarks>
    Task<GameListing> SearchAsync(GameFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Just the games — for the callers that want a listing and no panel.
    /// </summary>
    /// <remarks>
    /// Every implementation projects <see cref="SearchAsync"/>, so no caller gets a listing that
    /// disagrees with the faceted one.
    /// </remarks>
    Task<IReadOnlyList<GameSummary>> ListAsync(
        GameFilter filter,
        CancellationToken cancellationToken = default);

    Task<GamePage?> FindAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// The same page, addressed by the identifier that does not move (spec §5.7).
    /// </summary>
    /// <remarks>
    /// Both keys reach one page in one read, so a caller storing the id (the slug is mutable) never
    /// has to list the whole catalogue to resolve it back to a slug first.
    /// </remarks>
    Task<GamePage?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The listing entry for a game known by id, or null.
    /// </summary>
    /// <remarks>
    /// Public surfaces address a game by slug (what a URL carries); owner surfaces by id (a claim is
    /// bound to the game, not to a name a rename can move).
    /// </remarks>
    Task<GameSummary?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The three liveness feeds (spec §9) — the differentiator no incumbent can publish.</summary>
    Task<LivenessFeeds> FeedsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Codebase share and protocol adoption over the measured set (spec §9). Shares, never totals.
    /// </summary>
    Task<EcosystemDashboard> EcosystemAsync(CancellationToken cancellationToken = default);

    /// <summary>Rankings computed from measured data only (spec §9). There is no vote anywhere.</summary>
    /// <remarks>Defaulted to the week rather than required, matching what this table has always meant.</remarks>
    Task<Rankings> RankingsAsync(
        RankingSpan span = RankingSpan.Week,
        CancellationToken cancellationToken = default);
}

public sealed record LivenessFeeds(
    IReadOnlyList<FeedEntry> NewlyDiscovered,
    IReadOnlyList<FeedEntry> WentDark,
    IReadOnlyList<FeedEntry> CameBack);

/// <summary>
/// One event in a liveness register: which game, when, and what happened.
/// </summary>
/// <remarks>
/// <see cref="Id"/> leads because it's the identifier that survives a rename (spec §5.7); carried
/// from the query that built the entry rather than joined on afterwards.
/// </remarks>
public sealed record FeedEntry(Guid Id, string Slug, string Name, DateTimeOffset At, string Detail);
