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
    /// <summary>
    /// Whether somebody observed this, as opposed to a game reporting it.
    /// </summary>
    /// <remarks>
    /// Answered by <see cref="FieldSources"/> and not spelled out here. Every surface that says
    /// "measured" or "declared" — this chip, the API's <c>playersNowState</c>, the badge a game
    /// embeds on its own site — resolves through that one predicate, because a line drawn twice is
    /// a line that moves once.
    /// </remarks>
    public bool IsMeasured => FieldSources.IsMeasured(Source);
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
/// <para>
/// <see cref="LastReachableAt"/> is carried because the last-seen facet (spec §9) filters on it, and
/// a facet whose value cannot be read off the rows it returned is one a reader has to take on trust.
/// Null means we have never once reached the game, which is a different fact from "reachable a long
/// time ago" and is never rendered as the older of the two.
/// </para>
/// <para>
/// <b><see cref="PlayersNowProvenance"/> and <see cref="CodebaseProvenance"/> are the labels for the
/// two bare values above them</b>, and they are on the summary rather than only on the page because
/// the listing is a surface in its own right — spec §10.1 called an unlabelled listing "the one
/// place the API contradicts the rule the whole project exists to serve". A count somebody read out
/// of a <c>WHO</c> four minutes ago and one a game asserted about itself six years ago are different
/// facts, and a reader of the listing alone could not tell them apart while this type had nowhere to
/// put the difference. Null where — and only where — the value beside it is null: a chip over
/// nothing would be a label attesting to a measurement nobody took.
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
    ProvenanceChip? CodebaseProvenance = null);

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
    IReadOnlyList<ChangeEntry> Changes,

    // §9's referral neighbours. Defaulted because the fixture and the tests build a page without
    // them, and a game with no neighbours is the ordinary case rather than a missing read.
    IReadOnlyList<ReferralNeighbour>? Neighbours = null)
{
    public int DisagreementCount => Capabilities.Count(c => c.Disagrees);

    /// <summary>Games this one points at, and games that point at it — measured, not curated.</summary>
    public IReadOnlyList<ReferralNeighbour> Referrals => Neighbours ?? [];
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
/// <para>
/// <b>This is a measurement, and of the referrer rather than of the referred.</b> A game publishing
/// a <c>REFERRAL</c> list is asserting something, and what we measured is that they published it —
/// so an edge says "this list named that address on this date" and never "these games are related".
/// The direction is kept because the two are different claims by different people, and merging them
/// into one "neighbours" bag would attribute each game's list to the other.
/// </para>
/// <para>
/// Only edges whose address we have identified as a game appear. A referral to a host we have never
/// listed is a real edge and an unnameable neighbour, and inventing a name for it would be worse
/// than the omission.
/// </para>
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
/// <para>
/// <see cref="FirstSeenAt"/>, <see cref="LastSeenAt"/> and <see cref="State"/> are stored per
/// endpoint (migration 0005) and were dropped in this projection, so a game that had moved could
/// never render the move. That matters more here than the missing dates suggest: an address a game
/// has left is the single most useful thing to show somebody holding an old one, and §7.5's promise
/// that nothing is deleted is only visible if the departed row is rendered as departed rather than
/// omitted.
/// </para>
/// <para>
/// <see cref="State"/> is the table's own vocabulary — <c>active</c>, <c>stale</c>, <c>gone</c> —
/// carried through rather than re-derived from the dates, so a surface cannot invent a threshold the
/// writer does not share.
/// </para>
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
/// Every member here is one control on the panel and one querystring parameter, named in
/// <see cref="FacetKeys"/>. That correspondence is what makes a filtered listing linkable, the back
/// button work and the read API answer the same question the page does — there is no filter state
/// anywhere else, and nothing here needs a session to mean something.
/// </para>
/// <para>
/// <see cref="MeasuredProtocols"/> and <see cref="Tls"/> read observations; <see cref="Codebase"/>,
/// <see cref="CodebaseVersion"/>, <see cref="Family"/>, <see cref="Genre"/> and
/// <see cref="Language"/> read what a game says about itself. <see cref="Charset"/> is the odd one
/// and is deliberately on the measured side: it is what CHARSET settled on in the handshake, never
/// the game's MSSP claim about an encoding.
/// </para>
/// <para>
/// <see cref="Lineage"/> is on neither side, and is the only member here that is not. It reads a
/// classification of ours, which is why it is labelled <see cref="FacetEvidence.Derived"/> rather
/// than being quietly filed with the things games told us.
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
    /// A codebase <em>family</em> — <c>PennMUSH</c>, never <c>PennMUSH 1.8.8p0</c>. Every patchlevel
    /// of one codebase is one facet value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Matched by folding a game's codebase with <see cref="CodebaseFamily.Of"/> and comparing for
    /// equality — the same fold the panel counts, which is what makes a count a promise about what
    /// clicking it returns. There was briefly a second, looser rule for this (a bounded prefix) used
    /// by a filter that ran beside the facet; two definitions of one word is how a facet ends up
    /// returning a game no count included.
    /// </para>
    /// <para>
    /// A codebase reference page links here to say "the games running PennMUSH", so this and the
    /// page's own headline count are one query — see <c>CodebaseFigures</c>. The old
    /// <c>?codebase-family=</c> spelling those links carry is still accepted and lands here.
    /// </para>
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
    /// <b>Not <see cref="Family"/>, and it exists because <see cref="Family"/> cannot answer this.</b>
    /// MSSP's <c>FAMILY</c> vocabulary has no <c>MUSH</c> in it — PennMUSH answers <c>TinyMUD</c> —
    /// and the rest of the MUSH world publishes no MSSP at all. See <see cref="CodebaseLineage"/>.
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
    /// A question about presentation, and still part of the filter, because the URL is the whole of
    /// this page's state: a sorted listing has to be linkable exactly as a filtered one is, and the
    /// read API has to answer the same question the page asked. <see cref="GameSort.Name"/> is the
    /// default and is the one order that ranks nobody.
    /// </remarks>
    public GameSort Sort { get; init; } = GameSort.Name;
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
    /// The same page, addressed by the identifier that does not move (spec §5.7).
    /// </summary>
    /// <remarks>
    /// Both keys reach one page in one read. Without this the API's own advice — store the id, the
    /// slug is mutable — cost a caller the whole catalogue: the id route listed every game to find a
    /// slug, and then read the page anyway. Answering it by way of <see cref="FindByIdAsync"/>
    /// instead is cheaper and still wrong in the same direction, because that assembles a summary,
    /// its fields and its presence digest to hand back one string.
    /// </remarks>
    Task<GamePage?> FindAsync(Guid id, CancellationToken cancellationToken = default);

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
    /// <remarks>
    /// The span is defaulted rather than required so that a caller who does not care gets the week —
    /// which is what this table has always meant, and what changing the default silently would
    /// misrepresent on every surface that has ever printed it.
    /// </remarks>
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
/// <see cref="Id"/> leads because it is the identifier that survives (spec §5.7) — the slug moves
/// when a game renames itself. It is here rather than joined on afterwards because the query that
/// built this entry already had the game in hand: without it, every request for ten feed rows read
/// the entire catalogue to put an id beside each slug, and the API published the durable key as
/// though it were optional.
/// </remarks>
public sealed record FeedEntry(Guid Id, string Slug, string Name, DateTimeOffset At, string Detail);
