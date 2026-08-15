using MUI.Catalog;

namespace MUI.Web.Api;

/// <summary>
/// The wire shapes. Deliberately boring: flat records, spelled-out states, no polymorphism and no
/// cleverness a consumer would have to reverse-engineer.
/// </summary>
/// <remarks>
/// These are separate types from <see cref="MUI.Catalog"/>'s view models on purpose. The view models
/// are what a page renders and may be reshaped whenever a page is; these are a published contract
/// that a MUD client or a rival directory has already written code against. The mapping between them
/// is one file (<see cref="ApiMapper"/>), which is where a change to one becomes a decision about
/// the other rather than an accident.
/// </remarks>
public static class ApiVersion
{
    public const string Current = "1";
}

/// <summary>
/// A value, where it came from, and how old it is — the atom of the whole product (spec §5.1).
/// </summary>
/// <remarks>
/// <para>
/// An API that flattened this to <c>"codebase": "PennMUSH 1.8.8p0"</c> would be a different product
/// from the site: the site's whole claim is that it tells you <em>how it knows</em>, and a consumer
/// republishing a bare string cannot pass that on.
/// </para>
/// <para>
/// <see cref="Stale"/> is the catalogue's answer, carried through untouched. Staleness is a property
/// of the field's own expected-refresh window (spec §5.6) and nothing downstream re-derives it — an
/// API that recomputed it from <see cref="AgeSeconds"/> and a guess would disagree with the page.
/// </para>
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
/// Two fields and not one flag, because "declared GMCP, never offered in any handshake" is the most
/// useful thing a capability matrix can say and a single boolean cannot hold it. Each side is three
/// states — <c>present</c>, <c>absent</c>, <c>unknown</c> — because "we never saw it said either
/// way" is not "it is not there".
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
/// <para>
/// Three members, because there were two and the missing one was being answered with
/// <see cref="Measured"/>. A game that publishes <c>PLAYERS</c> in MSSP has reported a number about
/// itself; calling that a measurement of ours is rule 5, and it shipped in the same object as a
/// <c>playersNowProvenance.measured</c> of <c>false</c> saying the opposite.
/// </para>
/// <para>
/// A count read off a connect screen is <see cref="Measured"/>, not <see cref="Declared"/> — we open
/// a socket and parse that text ourselves on every probe. <c>playersNowProvenance.source</c> still
/// tells the three apart (<c>who</c>, <c>banner</c>, <c>mssp</c>) for a consumer who cares which,
/// and <c>MUI.Catalog.FieldSources</c> is the one place the line is drawn.
/// </para>
/// <para>
/// <see cref="Unknown"/> is "we cannot say it was measured", which covers the ordinary case of no
/// count at all — a null <c>playersNow</c> beside it, which is how absence is detected — and would
/// also cover a count that arrived without a label. Neither implementation can produce the second,
/// and guessing at one would be the fabrication this member exists to avoid.
/// </para>
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
/// <remarks>
/// A consumer holding an address that stopped working is the one this is for. <c>state</c> is the
/// catalogue's own word — <c>active</c>, <c>stale</c>, <c>gone</c> — rather than a threshold applied
/// here, so this cannot disagree with the page about when an address has been left.
/// </remarks>
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
/// <c>direction</c> is <c>lists</c> or <c>listed-by</c> and is not decoration: the two are different
/// people's claims, and a consumer merging them would attribute each game's referral list to the
/// other. <c>present</c> false means the list stopped naming it, never that the edge was deleted.
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
/// <remarks>
/// Every member is nullable because a game nothing overlaps the window for is unmeasured, and
/// unmeasured is not zero per cent.
/// </remarks>
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
/// <para>
/// <see cref="PlayersNowProvenance"/> and <see cref="CodebaseProvenance"/> label the bare values
/// they sit beside. Spec §10.1 recorded their absence as <em>the one place the API contradicts the
/// rule the whole project exists to serve</em>: the game route labelled every field with a source,
/// an age and a staleness while <c>/api/games</c> shipped a count and a codebase as
/// though a number were a number. A consumer republishing this listing could not tell a count read
/// out of a <c>WHO</c> four minutes ago from one a game asserted about itself six years ago, which is
/// precisely the confusion the incumbents' directories thrive on.
/// </para>
/// <para>
/// Null exactly where the value beside it is null. A chip over an absent count would attest to a
/// measurement nobody took, and "we did not measure this" is the label in that case.
/// </para>
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

    // Null means never once reached, which is not "reached a long time ago" — the last-seen facet
    // (spec §9) keeps them apart and a consumer reading only the listing has to be able to as well.
    DateTimeOffset? LastReachableAt,
    string Url,
    string ApiUrl);

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

    // The same two labels the listing carries, so a consumer that moved from one route to the other
    // does not have to learn a second way of asking how a fact was obtained. The codebase's label is
    // also in Fields, where every registry field's is; these two are lifted out because they are the
    // two values this API publishes bare.
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
/// One property per facet, named for the querystring parameter that sets it, built by
/// <see cref="Of"/> from the filter itself rather than from the raw query — so an echo cannot claim
/// a filter the query did not apply, and a facet added to <see cref="GameFilter"/> and forgotten
/// here fails to compile rather than silently vanishing from the answer.
/// </remarks>
public sealed record FilterView(
    string? Q,
    bool IncludeArchived,
    IReadOnlyList<string> Protocol,
    bool Tls,
    ActivityBand? Band,
    LastSeenBand? Seen,
    string? Charset,
    string? Codebase,
    string? Family,
    string? Genre,
    string? Language,
    string? CodebaseFamily,
    GameSort Sort)
{
    public static FilterView Of(GameFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return new FilterView(
            filter.Text,
            filter.IncludeArchived,
            filter.MeasuredProtocols,
            filter.Tls,
            filter.Band,
            filter.LastSeen,
            filter.Charset?.Token,
            filter.Codebase?.Token,
            filter.Family?.Token,
            filter.Genre?.Token,
            filter.Language?.Token,
            filter.CodebaseFamily?.Token,
            filter.Sort);
    }
}

/// <summary>One facet value as the API publishes it, with what choosing it returns.</summary>
public sealed record FacetValueView(string Value, int Count, bool Selected, bool Unknown);

/// <summary>
/// One facet: its querystring name, whether it reads a measurement or a claim, and its values.
/// </summary>
/// <remarks>
/// <c>evidence</c> is published rather than left for a consumer to infer, for the same reason the
/// page prints it: a facet reading <c>capability.gmcp.measured</c> and one reading MSSP's
/// <c>GENRE</c> are not the same kind of statement, and a client that presented them identically
/// would be making the claim this whole site exists to stop making.
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
/// <remarks>
/// It was nullable while <c>FeedEntry</c> carried only a slug and the endpoint joined ids on out of
/// the listing — an identifier that went missing whenever the join did. The query supplies it now,
/// so a reader may store it (spec §5.7) without a fallback path for the times we could not say.
/// </remarks>
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
/// <para>
/// <see cref="CountedSamples"/> and <see cref="UncountableSamples"/> are counts of <em>probes</em>
/// and never of players. They are separate because a bucket probed three times and uncountable every
/// time is a different fact from one in which nobody was logged in, and a consumer that added them
/// together would erase §5.4's middle state.
/// </para>
/// <para>
/// <see cref="Min"/>, <see cref="Max"/> and <see cref="Mean"/> are over the counted probes alone and
/// are null when there were none. <b>Null is not zero</b>: this series never answers "how many
/// players" with a number it inferred.
/// </para>
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
/// The gaps are the third state of §5.4 and they mean "not measured" — they do not mean the game was
/// empty and they do not mean it was unreachable, which is what <c>/availability</c> answers. Filling
/// them in would publish our crawl schedule as though it were somebody's quiet hours.
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
/// <remarks>
/// The spans are <see cref="AvailabilitySpanView"/> — the same shape the game route already
/// publishes, not a second one meaning the same thing, so a consumer that can read one can read both.
/// </remarks>
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
