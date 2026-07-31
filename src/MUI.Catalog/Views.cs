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
/// <remarks>
/// <see cref="LastReachableAt"/> is carried because the last-seen facet (spec §9) filters on it, and
/// a facet whose value cannot be read off the rows it returned is one a reader has to take on trust.
/// Null means we have never once reached the game, which is a different fact from "reachable a long
/// time ago" and is never rendered as the older of the two.
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
    DateTimeOffset? LastReachableAt = null);

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
/// <remarks>
/// <para>
/// Every member here is one control on the panel and one querystring parameter, named in
/// <see cref="FacetKeys"/>. That correspondence is what makes a filtered listing linkable, the back
/// button work and the read API answer the same question the page does — there is no filter state
/// anywhere else, and nothing here needs a session to mean something.
/// </para>
/// <para>
/// <see cref="MeasuredProtocols"/> and <see cref="Tls"/> read observations; <see cref="Codebase"/>,
/// <see cref="Family"/>, <see cref="Genre"/> and <see cref="Language"/> read what a game says about
/// itself. <see cref="Charset"/> is the odd one and is deliberately on the measured side: it is what
/// CHARSET settled on in the handshake, never the game's MSSP claim about an encoding.
/// </para>
/// </remarks>
public sealed record GameFilter
{
    public string? Text { get; init; }

    public bool IncludeArchived { get; init; }

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

    public FacetChoice? Charset { get; init; }

    /// <summary>
    /// The codebase exactly as a game reports it, <c>PennMUSH 1.8.8p0</c> and all — a counted facet
    /// over the values actually present in the catalogue.
    /// </summary>
    public FacetChoice? Codebase { get; init; }

    public FacetChoice? Family { get; init; }

    public FacetChoice? Genre { get; init; }

    public FacetChoice? Language { get; init; }

    /// <summary>
    /// A codebase <em>family</em> — <c>PennMUSH</c>, not <c>PennMUSH 1.8.8p0</c>. Matched by
    /// <see cref="CodebaseFamily"/>, so every patchlevel of one codebase is one facet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists because a reference page for a codebase has to link to the games running it, and
    /// a link is only honest if the page's own count and the listing it lands on are one query. Free
    /// text will not do the job: <c>?q=PennMUSH</c> searches names against the database and would
    /// find the games <em>called</em> PennMUSH rather than the games running it.
    /// </para>
    /// <para>
    /// <b>Distinct from <see cref="Codebase"/> and from <see cref="Family"/>, and all three are real.</b>
    /// <c>Codebase</c> is the raw string a game published; <c>Family</c> is MSSP's own <c>FAMILY</c>
    /// variable, which answers <c>TinyMUD</c> or <c>DikuMUD</c>; this is the codebase with its
    /// version taken off. A reference page for PennMUSH wants the third and neither of the others.
    /// </para>
    /// <para>
    /// A <see cref="FacetChoice"/> for its polarity rather than its matching: the choice carries the
    /// value and whether it is being filtered in or out, and the <em>test</em> is supplied by the
    /// caller — <see cref="CodebaseFamily.Matches"/>, a bounded prefix, so <c>ROM</c> does not gather
    /// <c>ROMulus</c>. It is not offered as a counted facet in the panel, so it never appears in the
    /// vocabulary the choice facets are drawn from.
    /// </para>
    /// </remarks>
    public FacetChoice? CodebaseFamily { get; init; }
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
    /// <summary>
    /// The listing and the facet counts that describe it, from one pass (spec §9).
    /// </summary>
    /// <remarks>
    /// One method rather than a listing call and a counts call, because a facet must not be able to
    /// lie about what a click will produce. Two calls are two answers to two slightly different
    /// questions, and the first time they disagreed the panel would be advertising a count the
    /// listing could not deliver.
    /// </remarks>
    Task<GameListing> SearchAsync(GameFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Just the games — for the callers that want a listing and no panel.
    /// </summary>
    /// <remarks>
    /// Every implementation answers it by projecting <see cref="SearchAsync"/>, so there is no route
    /// by which a caller that does not want facets gets a different listing from one that does.
    /// </remarks>
    Task<IReadOnlyList<GameSummary>> ListAsync(
        GameFilter filter,
        CancellationToken cancellationToken = default);

    Task<GamePage?> FindAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// The listing entry for a game known by id, or null.
    /// </summary>
    /// <remarks>
    /// The public surfaces address a game by slug, because that is what a URL carries. The owner
    /// surfaces address it by id, because a claim is bound to the game and not to a name a rename can
    /// move — so this exists rather than having those pages reach past the interface to a store, or
    /// resolve a slug they were never given.
    /// </remarks>
    Task<GameSummary?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

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
