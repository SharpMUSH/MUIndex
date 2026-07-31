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

/// <summary>Whether a player count is a measurement or an absence of one. Never inferred from 0.</summary>
public enum PlayerCountState
{
    Measured,
    Unknown,
}

public sealed record EndpointView(string Host, int Port, string Kind, bool TlsMeasured);

public sealed record ChangeView(DateTimeOffset At, string Summary);

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
    string? Codebase,
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
    string? Codebase,
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
    string? Language)
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
            filter.Language?.Token);
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

public sealed record FeedEntryView(
    Guid? Id,
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
