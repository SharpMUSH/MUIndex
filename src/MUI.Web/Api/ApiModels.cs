using MUI.Catalog;

namespace MUI.Web.Api;

/// <summary>
/// The wire shapes. Deliberately boring: flat records, spelled-out states, no polymorphism and no
/// cleverness a consumer would have to reverse-engineer.
/// </summary>
/// <remarks>
/// Separate types from <see cref="MUI.Catalog"/>'s view models on purpose: those may be reshaped
/// whenever a page is, these are a published contract a client has already written code against.
/// <see cref="ApiMapper"/> is the one file mapping between them.
/// </remarks>
public static class ApiVersion
{
    public const string Current = "1";
}

/// <summary>
/// A value, where it came from, and how old it is — the atom of the whole product (spec §5.1).
/// </summary>
/// <remarks>
/// <see cref="Stale"/> is the catalogue's answer, carried through untouched (spec §5.6) — recomputing
/// it here from <see cref="AgeSeconds"/> and a guess would let this disagree with the page.
/// </remarks>
public sealed record ProvenanceView(
    string Value,
    FieldSource Source,
    bool Measured,
    DateTimeOffset LastConfirmedAt,
    double AgeSeconds,
    string Age,
    bool Stale);

/// <summary>
/// One capability, measured beside declared, never merged (spec §3.1).
/// </summary>
/// <remarks>
/// Two fields, not one flag: "declared GMCP, never offered in any handshake" is the point of a
/// capability matrix and a boolean can't hold it. Each side is three states — <c>present</c>,
/// <c>absent</c>, <c>unknown</c> — because never having seen it said is not the same as absent.
/// </remarks>
public sealed record CapabilityView(
    string Protocol,
    CapabilityState Measured,
    CapabilityState Declared,
    bool Disagrees,
    DateTimeOffset? LastConfirmedAt,
    double? AgeSeconds,
    string? Age);

/// <summary>Which of the three states an hour of the presence grid is in (spec §5.4).</summary>
public enum PresenceState
{
    /// <summary>Probed and counted. Includes a measured zero, which is a count and not a gap.</summary>
    Counted,

    /// <summary>Probed, answered, and produced no number we could trust. Never a zero.</summary>
    Unmeasurable,

    /// <summary>Not reachable in that hour. Emphatically not a zero either.</summary>
    Gap,
}

/// <summary>
/// One hour of the presence grid. <see cref="Count"/> is non-null only when
/// <see cref="State"/> is <see cref="PresenceState.Counted"/>.
/// </summary>
public sealed record PresenceCellView(int DayOfWeek, int Hour, PresenceState State, int? Count);

/// <summary>
/// The presence series. <see cref="Kind"/> and <see cref="Timezone"/> ship so a consumer never has
/// to infer what the axes mean.
/// </summary>
public sealed record PresenceView(
    string Kind,
    string Timezone,
    IReadOnlyList<PresenceCellView> Cells);

/// <summary>
/// How a player count was obtained, or that it was not. Never inferred from 0.
/// </summary>
/// <remarks>
/// A game publishing <c>PLAYERS</c> in MSSP has reported a number about itself; calling that
/// <see cref="Measured"/> would violate rule 5 (measured vs. declared). A count read off a connect
/// screen counts as <see cref="Measured"/> — we open a socket and parse it ourselves on every probe.
/// <see cref="Unknown"/> is "we cannot say it was measured", covering the ordinary case of no count
/// at all (a null <c>playersNow</c> beside it).
/// </remarks>
public enum PlayerCountState
{
    Measured,
    Declared,
    Unknown,
}

/// <summary>
/// An address, and how long it has answered there (spec §9).
/// </summary>
/// <remarks><c>state</c> is the catalogue's own word (<c>active</c>/<c>stale</c>/<c>gone</c>), not a threshold applied here, so this can't disagree with the page.</remarks>
public sealed record EndpointView(
    string Host,
    int Port,
    string Kind,
    bool TlsMeasured,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    string State);

public sealed record ChangeView(DateTimeOffset At, string Summary);

/// <summary>
/// One referral edge, resolved to a game (spec §9).
/// </summary>
/// <remarks>
/// <c>direction</c> (<c>lists</c>/<c>listed-by</c>) matters: the two are different people's claims,
/// and merging them would attribute each game's referral list to the other. <c>present</c> false
/// means the list stopped naming it, never that the edge was deleted.
/// </remarks>
public sealed record NeighbourView(
    string Slug,
    string Name,
    string Host,
    int Port,
    string Direction,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    bool Present);

/// <summary>
/// An availability span with its cause (spec §5.3). Intervals rather than samples: a hundred
/// consecutive timeouts are one of these.
/// </summary>
public sealed record AvailabilitySpanView(
    AvailabilityState State,
    FailureCause Cause,
    DateTimeOffset FromAt,
    DateTimeOffset? ToAt,
    bool Open,
    double DurationSeconds);

/// <summary>
/// Reachability over a window. <b>Reachable, never uptime</b> (spec §5.8) — we measured a socket
/// from one vantage point and did not measure whether the game was up.
/// </summary>
/// <remarks>Every member is nullable: a game nothing overlaps the window for is unmeasured, not zero per cent.</remarks>
public sealed record ReachabilityView(
    int WindowDays,
    double? Fraction,
    double? Percent,
    double? LongestOutageSeconds);

/// <summary>
/// The connect screen, or the fact that its owner asked us to stop republishing it (spec §11).
/// </summary>
public sealed record ConnectScreenView(bool Suppressed, string? Text);

/// <summary>
/// A game as the listing publishes it — and, for the two facts a row leads with, how we know them.
/// </summary>
/// <remarks>
/// <see cref="PlayersNowProvenance"/> and <see cref="CodebaseProvenance"/> label the bare values they
/// sit beside (spec §10.1) — without them a consumer can't tell a count read off a live <c>WHO</c>
/// from one a game merely asserted. Null exactly where the value beside it is null.
/// </remarks>
public sealed record GameSummaryView(
    Guid Id,
    string Slug,
    string Name,
    string? Tagline,
    LifecycleState State,
    bool Archived,
    bool Claimed,
    int? PlayersNow,
    PlayerCountState PlayersNowState,
    ProvenanceView? PlayersNowProvenance,
    string? Codebase,
    ProvenanceView? CodebaseProvenance,
    IReadOnlyList<string> MeasuredProtocols,

    // Null means never once reached — distinct from "reached a long time ago" (spec §9's last-seen facet).
    DateTimeOffset? LastReachableAt,
    string Url,
    string ApiUrl,

    // What a window sort ranked this row on; present only when one was asked for, not on every response.
    PresenceWindowView? PlayersOverWindow = null);

/// <summary>
/// One game's counts over one window, as <c>?sort=medianWeek</c> and its neighbours rank on them.
/// </summary>
/// <remarks>
/// <see cref="Median"/>, not a mean — same choice <c>/rankings</c> makes, since a mean is pulled
/// around by one busy evening. It is an <b>observed</b> count, the first value whose running
/// frequency reaches the half-way mark. <see cref="Samples"/> ships beside it so a figure's basis
/// isn't hidden (§15.7): ranking on <see cref="Median"/> without it ranks our crawl schedule. Both
/// figures are over <b>counted</b> samples alone; a game with no countable probe in the window is
/// absent from this field, never present with zeroes.
/// </remarks>
public sealed record PresenceWindowView(int WindowDays, int Median, int Peak, int Samples);

public sealed record GameView(
    Guid Id,
    string Slug,
    string Name,
    string? Tagline,
    string? Description,
    LifecycleState State,
    bool Archived,
    bool Claimed,
    int? PlayersNow,
    PlayerCountState PlayersNowState,

    // Same two labels the listing carries; lifted out because they're the two values this API
    // publishes bare (the codebase's label also lives in Fields, with every other registry field's).
    ProvenanceView? PlayersNowProvenance,
    string? Codebase,
    ProvenanceView? CodebaseProvenance,
    IReadOnlyList<string> MeasuredProtocols,
    IReadOnlyList<EndpointView> Endpoints,
    ConnectScreenView ConnectScreen,
    ReachabilityView Reachable,
    IReadOnlyList<CapabilityView> Capabilities,
    int DisagreementCount,
    IReadOnlyDictionary<string, ProvenanceView> Fields,
    PresenceView Presence,
    IReadOnlyList<AvailabilitySpanView> Availability,
    IReadOnlyList<ChangeView> Changes,

    /// <summary>§9's referral neighbours, both arrows, each labelled with which way it runs.</summary>
    IReadOnlyList<NeighbourView> Referrals,
    string Url,
    string ApiUrl);

/// <summary>
/// What the listing was asked for, echoed so a cached response says what it answers.
/// </summary>
/// <remarks>
/// Built by <see cref="Of"/> from the filter itself, not the raw query, so a facet added to
/// <see cref="GameFilter"/> and forgotten here fails to compile rather than silently vanishing.
/// <see cref="CodebaseFamily"/> is the old name for <see cref="Codebase"/>, carried at the same
/// value: this is a published API-v1 contract, so a field disappearing from an unchanged version
/// would read as "filter not applied" rather than "filter renamed".
/// </remarks>
public sealed record FilterView(
    string? Q,
    bool IncludeArchived,

    /// <summary>
    /// Whether games declaring adult content were in this answer. <c>false</c> unless
    /// <c>?adult=1</c> asked for them, so a cached response says which listing it is.
    /// </summary>
    bool IncludeAdult,
    IReadOnlyList<string> Protocol,
    bool Tls,
    ActivityBand? Band,
    LastSeenBand? Seen,

    /// <summary>
    /// Whether the games we hold no readable count for were asked for, or dropped.
    /// </summary>
    /// <remarks>
    /// <b>Not a claim about any game either way</b> — <c>!yes</c> echoes a decision this request made
    /// about its own answer, not that "these games are empty".
    /// </remarks>
    string? Uncounted,

    /// <summary>The same, for the games we could not reach recently. Never the archive.</summary>
    string? Unreachable,
    string? Charset,
    string? Codebase,
    string? Version,
    string? Lineage,
    string? Family,
    string? Genre,
    string? Language,
    GameSort Sort,

    /// <summary>The old name for <see cref="Codebase"/>, at the same value. Prefer the new one.</summary>
    string? CodebaseFamily = null)
{
    public static FilterView Of(GameFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return new FilterView(
            filter.Text,
            filter.IncludeArchived,
            filter.IncludeAdult,
            filter.MeasuredProtocols,
            filter.Tls,
            filter.Band,
            filter.LastSeen,
            filter.Uncounted?.Token,
            filter.Unreachable?.Token,
            filter.Charset?.Token,
            filter.Codebase?.Token,
            filter.CodebaseVersion?.Token,
            filter.Lineage?.Token,
            filter.Family?.Token,
            filter.Genre?.Token,
            filter.Language?.Token,
            filter.Sort,
            filter.Codebase?.Token);
    }
}

/// <summary>One facet value as the API publishes it, with what choosing it returns.</summary>
public sealed record FacetValueView(string Value, int Count, bool Selected, bool Unknown);

/// <summary>
/// One facet: its querystring name, whether it reads a measurement or a claim, and its values.
/// </summary>
/// <remarks>
/// <c>evidence</c> is published rather than left to inference: a facet reading measured GMCP and one
/// reading MSSP's declared <c>GENRE</c> are not the same kind of statement.
/// </remarks>
public sealed record FacetGroupView(
    string Key,
    FacetEvidence Evidence,
    FacetKind Kind,
    int Total,
    IReadOnlyList<FacetValueView> Values);

public sealed record GameListView(
    string ApiVersion,
    DateTimeOffset GeneratedAt,
    FilterView Filter,
    IReadOnlyList<FacetGroupView> Facets,
    int Total,
    int Limit,
    int Offset,
    int Count,
    IReadOnlyList<GameSummaryView> Games);

/// <summary>
/// One event in a liveness register. <see cref="Id"/> is the durable key and is never absent.
/// </summary>
/// <remarks>The query supplies it directly now (previously joined in and occasionally missing), so a reader may store it (spec §5.7) without a fallback path.</remarks>
public sealed record FeedEntryView(
    Guid Id,
    string Slug,
    string Name,
    DateTimeOffset At,
    string Detail,
    string Url,
    string ApiUrl);

/// <summary>The three liveness registers, and where each is also published as RSS (spec §10).</summary>
public sealed record FeedsView(
    string ApiVersion,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<FeedEntryView> NewlyDiscovered,
    IReadOnlyList<FeedEntryView> WentDark,
    IReadOnlyList<FeedEntryView> CameBack,
    IReadOnlyDictionary<string, string> Rss);

public sealed record LicenceView(string Id, string Name, string? Url, string Attribution);

public sealed record AttributionView(string Name, string? Url, string Role);

/// <summary>The first object of the bulk dump: what this is, who to credit, and on what terms.</summary>
public sealed record DumpHeaderView(
    string ApiVersion,
    DateTimeOffset GeneratedAt,
    LicenceView Licence,
    IReadOnlyList<AttributionView> Attribution,
    string Notice);

/// <summary>The route table, so a consumer can start from <c>/api</c> and find everything.</summary>
public sealed record ApiIndexView(
    string ApiVersion,
    string Name,
    string Description,
    DateTimeOffset GeneratedAt,
    LicenceView Licence,
    IReadOnlyList<RouteView> Routes,
    IReadOnlyList<string> Notes);

public sealed record RouteView(string Method, string Path, string Returns);

/// <summary>
/// One bucket of measured presence (spec §10, §5.2).
/// </summary>
/// <remarks>
/// <see cref="CountedSamples"/> and <see cref="UncountableSamples"/> are counts of <em>probes</em>,
/// never of players — kept separate so summing them can't erase §5.4's middle state.
/// <see cref="Min"/>/<see cref="Max"/>/<see cref="Mean"/> are over counted probes alone and null when
/// there were none. <b>Null is not zero.</b>
/// </remarks>
public sealed record PresenceBucketView(
    DateTimeOffset At,
    int CountedSamples,
    int UncountableSamples,
    int? Min,
    int? Max,
    decimal? Mean);

/// <summary>
/// A game's presence over time (spec §10).
/// </summary>
/// <remarks>
/// <b>A bucket nobody measured is absent from <see cref="Buckets"/>, never present as a zero.</b>
/// A gap is §5.4's third state ("not measured"), not "empty" and not "unreachable" — that's what
/// <c>/availability</c> answers.
/// </remarks>
public sealed record PresenceSeriesView(
    string ApiVersion,
    DateTimeOffset GeneratedAt,
    Guid GameId,
    string Slug,
    string Grain,
    DateTimeOffset From,
    DateTimeOffset To,
    int Count,
    IReadOnlyList<PresenceBucketView> Buckets,
    string Notice);

/// <summary>
/// A game's reachability over time (spec §10), as spans rather than samples.
/// </summary>
/// <remarks>Reuses <see cref="AvailabilitySpanView"/>, the same shape the game route publishes, so a consumer that can read one can read both.</remarks>
public sealed record AvailabilitySeriesView(
    string ApiVersion,
    DateTimeOffset GeneratedAt,
    Guid GameId,
    string Slug,
    DateTimeOffset From,
    DateTimeOffset To,
    int Count,
    IReadOnlyList<AvailabilitySpanView> Spans,
    string Notice);
