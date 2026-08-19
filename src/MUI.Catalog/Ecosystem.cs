namespace MUI.Catalog;

/// <summary>
/// A count over the set it was counted in. There is no way to hold a proportion here without the
/// denominator it is a proportion of.
/// </summary>
/// <remarks>
/// The denominator travels with the count so a share can't be rendered without it — a ratio over
/// four hundred measurements looks identical to a ratio over four otherwise. <see cref="Fraction"/>
/// is null on an empty denominator rather than zero: nothing measured is not nought per cent.
/// </remarks>
public sealed record MeasuredShare(string Label, int Count, int Denominator)
{
    public double? Fraction => Denominator == 0 ? null : (double)Count / Denominator;
}

/// <summary>
/// Which codebases the listed games run, as shares of the games that told us — by family, and by
/// the lineage those families descend from (spec §9).
/// </summary>
/// <remarks>
/// <see cref="NotIdentified"/> is a game whose codebase we couldn't read, not a game running "Other" —
/// folding it into the shares would publish our own gap as market share. <see cref="Lineages"/> shares
/// the same denominator as <see cref="Families"/>; <see cref="NotClassified"/> is the games we
/// declined to classify, kept visible on its own rather than silently inflating everyone else's share.
/// </remarks>
public sealed record CodebaseUsage(
    IReadOnlyList<MeasuredShare> Families,
    IReadOnlyList<MeasuredShare> Lineages,
    int Identified,
    int NotIdentified,
    int NotClassified)
{
    public static readonly CodebaseUsage None = new([], [], 0, 0, 0);

    /// <summary>The codebases more than one listed game runs — the shares that are shares.</summary>
    /// <remarks>
    /// The cut is on count, not name: refusing a <c>CODEBASE</c> that matches the game's own name
    /// seems like the obvious filter, but it blanks real families like <c>LambdaMOO</c> or
    /// <c>CircleMUD</c> whose flagship carries the name, while one-game rows like <c>Rapture</c> still
    /// get through. Refusing the value outright would record our own editorial decision as the game's
    /// silence (rule 5). <see cref="Shared"/> and <see cref="SoleUse"/> are both projections of
    /// <see cref="Families"/> rather than separately stored lists, so they can't drift out of partition.
    /// </remarks>
    public IReadOnlyList<MeasuredShare> Shared => [.. Families.Where(share => share.Count > 1)];

    /// <summary>The codebases exactly one listed game runs, in the order the panel folds them.</summary>
    public IReadOnlyList<MeasuredShare> SoleUse => [.. Families.Where(share => share.Count == 1)];

    /// <summary>
    /// <see cref="SoleUse"/> as one share of the games that told us — one game each, so the count of
    /// codebases and the count of games are the same number.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="Identified"/> as denominator, same as every bar above it, so
    /// <see cref="Shared"/> plus this equals exactly the games that answered.
    /// </remarks>
    public MeasuredShare SoleUseTotal => new("sole use", SoleUse.Count, Identified);

    /// <summary>
    /// The dashboard's codebase panel, from the <c>CODEBASE</c> value of every game that gave us one.
    /// </summary>
    /// <param name="values">One entry per game that told us its codebase, as it spelled it.</param>
    /// <param name="listed">Every listed game, so the games that told us nothing can be counted.</param>
    public static CodebaseUsage Of(IReadOnlyList<string> values, int listed)
    {
        ArgumentNullException.ThrowIfNull(values);

        var families = Shares(values.Select(CodebaseFamily.For), values.Count);
        var lineages = Shares(values.Select(CodebaseLineage.Of), values.Count);

        return new CodebaseUsage(
            families,
            lineages,
            values.Count,
            listed - values.Count,
            values.Count - lineages.Sum(share => share.Count));
    }

    private static List<MeasuredShare> Shares(IEnumerable<string?> labels, int denominator) =>
    [
        .. labels
            .OfType<string>()
            .Where(label => label.Length > 0)
            .GroupBy(label => label, StringComparer.OrdinalIgnoreCase)

            // The same rule the facet panel labels its values with, so the dashboard and the listing
            // cannot end up calling one family two things.
            .Select(group => new MeasuredShare(Spellings.Commonest(group), group.Count(), denominator))
            .OrderByDescending(share => share.Count)
            .ThenBy(share => share.Label, StringComparer.Ordinal),
    ];
}

/// <summary>
/// One protocol, measured beside declared, each with its own denominator (spec §9).
/// </summary>
/// <remarks>
/// Two denominators: <see cref="Handshakes"/> (session completed) and <see cref="MsspReports"/> (MSSP
/// report held) are different sets — a game can be in either, both, or neither.
/// <see cref="Offered"/> is null, not zero, when nothing has been measured — the state TLS is in
/// today, since the probe only dials plain telnet. Rendering that as "0% offer TLS" would state our
/// crawler's limit as a fact about the hobby (rule 5).
/// <see cref="Declined"/> only counts an explicit measured <c>false</c>; only MSSP gets one, since
/// every probe asks for it by name. Everywhere else the remainder is <see cref="Unobserved"/> — not
/// seen is not the same fact as absent, and surfaces rendering this must say so.
/// </remarks>
public sealed record ProtocolAdoption(
    string Protocol,
    int? Offered,
    int Declined,
    int Handshakes,
    int Declared,
    int MsspReports)
{
    /// <summary>
    /// Games whose handshake completed and in which we saw neither a yes nor a no.
    /// </summary>
    /// <remarks>
    /// Floored at zero: the numerator and denominator come from different tables (stored fields vs.
    /// availability), so a capability row on a game with no reachable interval could otherwise put a
    /// negative count on a public page.
    /// </remarks>
    public int Unobserved => Math.Max(0, Handshakes - (Offered ?? 0) - Declined);

    /// <summary>The measured side, or null where nothing has ever been measured.</summary>
    public MeasuredShare? Measured =>
        Offered is { } offered ? new MeasuredShare(Protocol, offered, Handshakes) : null;

    /// <summary>
    /// The declared side. A share for every protocol but the one the column is made of.
    /// </summary>
    /// <remarks>
    /// A share, not a null, for every ordinary protocol: absence of a claim is not a claim. MSSP is
    /// the exception — every <see cref="MsspReports"/> game demonstrably supports MSSP (we hold the
    /// report), so scoring it against games that mentioned MSSP inside their own report would give a
    /// share whose numerator counts a habit and whose denominator counts a fact, not a measurement.
    /// </remarks>
    public MeasuredShare? DeclaredShare =>
        EcosystemProtocols.IsInstrument(Protocol) ? null : new(Protocol, Declared, MsspReports);
}

/// <summary>
/// One protocol's adoption on one day (spec §9).
/// </summary>
/// <remarks>
/// The share isn't stored, only divided out of the same <see cref="ProtocolAdoption"/> the live
/// dashboard divides — a stored percentage would discard its denominator and become uncheckable.
/// </remarks>
public sealed record AdoptionPoint(DateTimeOffset At, ProtocolAdoption Adoption);

/// <summary>
/// One protocol's history, and whether there is enough of it to call a curve.
/// </summary>
/// <remarks>
/// <see cref="IsCurve"/> lets a surface refuse to draw a line through one point — a single snapshot
/// rendered as a trend would be an invented direction, the same fault as an invented zero.
/// </remarks>
public sealed record AdoptionCurve(string Protocol, IReadOnlyList<AdoptionPoint> Points)
{
    public bool IsCurve => Points.Count > 1;

    /// <summary>The measured share at each end, or null where either end never measured it.</summary>
    public (MeasuredShare? First, MeasuredShare? Last) Ends =>
        Points.Count == 0
            ? (null, null)
            : (Points[0].Adoption.Measured, Points[^1].Adoption.Measured);
}

/// <summary>Where §9's adoption curves are kept.</summary>
public interface IEcosystemSnapshots
{
    /// <summary>Records today's figures, replacing today's if a pass has already run.</summary>
    Task<int> RecordAsync(
        DateTimeOffset asOf,
        IReadOnlyList<ProtocolAdoption> protocols,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdoptionPoint>> CurveAsync(
        string protocol,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The ecosystem dashboard: codebase share and protocol adoption over the measured set (spec §9).
/// </summary>
/// <remarks>
/// No player total on this record, deliberately: §15.7 withholds it because a ratio over the measured
/// set survives biases a raw count doesn't, and a total on the view model is a total someone renders.
/// This is a snapshot, not yet a curve — <c>game_field</c> holds only a current value plus transitions
/// in <c>field_change</c>, so <see cref="CapabilityTransitions"/> tracks how many exist, which is when
/// a curve becomes honest to draw. Plotting each observation's <c>first_seen_at</c> instead would
/// chart our crawl reaching more games, not adoption.
/// </remarks>
public sealed record EcosystemDashboard(
    DateTimeOffset AsOf,
    int ListedGames,
    int Handshakes,
    int MsspReports,
    DateTimeOffset? OldestHandshake,
    int CapabilityTransitions,
    CodebaseUsage Codebases,
    IReadOnlyList<ProtocolAdoption> Protocols)
{
    /// <summary>What asking every server for MSSP produced, or null before anything was measured.</summary>
    /// <remarks>
    /// Read beside <see cref="MsspReports"/>: we hold more reports than there are games offering MSSP
    /// today, since a report isn't discarded when a game stops reissuing it.
    /// </remarks>
    public ProtocolAdoption? Mssp =>
        Protocols.FirstOrDefault(protocol => EcosystemProtocols.IsInstrument(protocol.Protocol));

    public static EcosystemDashboard Empty(DateTimeOffset asOf) => new(
        asOf, 0, 0, 0, null, 0, CodebaseUsage.None, []);
}

/// <summary>
/// The protocols §9 names as the dashboard's headline four.
/// </summary>
/// <remarks>
/// Listed even when unmeasured, because "we haven't measured TLS yet" is worth saying and only
/// visible if the row exists. Every other capability appears only when there's something to report.
/// </remarks>
public static class EcosystemProtocols
{
    public static IReadOnlyList<string> Headline { get; } = ["TLS", "UTF-8", "GMCP", "MXP"];

    /// <summary>
    /// The protocol this page measures <em>with</em>, and the one row on it that is not a floor.
    /// </summary>
    /// <remarks>
    /// Being the instrument makes the measured column stronger, not weaker: we don't request any
    /// other protocol, so every other measured figure is a floor, while MSSP is the one we ask for by
    /// name — the only row with a real "declined" count. It's also what breaks the declared cell:
    /// <see cref="ProtocolAdoption.DeclaredShare"/> is null here for a denominator that can't be
    /// fixed, not because the number is merely small.
    /// </remarks>
    public const string Instrument = "MSSP";

    public static bool IsInstrument(string protocol) =>
        string.Equals(protocol, Instrument, StringComparison.Ordinal);
}


/// <summary>
/// How far back the busiest ranking looks (spec §9).
/// </summary>
/// <remarks>
/// Three named spans, not an arbitrary range — a ranking is a claim, and each span makes a different
/// one. Windows are day-aligned in UTC, not rolling: <c>presence_rollup_day</c> buckets are whole UTC
/// days (migration 0019), so "the last N days" means N whole day buckets, current partial one included.
/// </remarks>
public enum RankingSpan
{
    /// <summary>Seven days. The default, and what the ranking has always meant.</summary>
    Week,

    /// <summary>Thirty days.</summary>
    Month,

    /// <summary>Ninety days.</summary>
    Quarter,
}

/// <summary>Words and windows for <see cref="RankingSpan"/>, in one place.</summary>
public static class RankingSpans
{
    /// <summary>Every span, in the order the selector offers them.</summary>
    public static readonly IReadOnlyList<RankingSpan> All =
        [RankingSpan.Week, RankingSpan.Month, RankingSpan.Quarter];

    public static int Days(this RankingSpan span) => span switch
    {
        RankingSpan.Week => 7,
        RankingSpan.Month => 30,
        RankingSpan.Quarter => 90,
        _ => throw new ArgumentOutOfRangeException(nameof(span)),
    };

    public static TimeSpan Window(this RankingSpan span) => TimeSpan.FromDays(span.Days());

    /// <summary>What the span is called in a query string: <c>7d</c>, <c>30d</c>, <c>90d</c>.</summary>
    public static string Slug(this RankingSpan span) => $"{span.Days()}d";

    /// <summary>
    /// The span a query string names, or <see cref="RankingSpan.Week"/>.
    /// </summary>
    /// <remarks>
    /// Unknown input falls back to the default instead of erroring — a mistyped window isn't worth a
    /// 400 in front of a page that has a right answer to give.
    /// </remarks>
    public static RankingSpan Parse(string? slug) =>
        All.FirstOrDefault(s => string.Equals(s.Slug(), slug, StringComparison.OrdinalIgnoreCase),
            RankingSpan.Week);

    /// <summary>
    /// How many of the window's days a game must have been measured on to be ranked in it.
    /// </summary>
    /// <remarks>
    /// Half the window, rounded up. A flat sample floor alone doesn't scale: two days of hard probing
    /// could otherwise qualify a game for a ninety-day ranking.
    /// </remarks>
    public static int MinimumDays(this RankingSpan span) => (span.Days() + 1) / 2;
}

/// <summary>
/// One game in the busiest ranking, with the measurements the rank is computed from beside it.
/// </summary>
/// <remarks>
/// Median, not peak — a peak is one sample, and forty players for a minute isn't "busier" than thirty
/// all day. <see cref="Median"/> is an observed value, never an average of two. <see cref="Days"/>
/// matters more as the window widens: twelve hundred samples over a quarter is a different fact
/// depending on whether they came from ninety days or three, matching the units
/// <see cref="RankingSpans.MinimumDays"/> is stated in.
/// </remarks>
public sealed record BusiestGame(string Slug, string Name, int Median, int Peak, int Samples, int Days = 0);

/// <summary>
/// One game in the "trending this week" board — always the current week against the one before it,
/// unlike <see cref="BusiestGame"/> which reads whichever span the selector asks for.
/// </summary>
/// <remarks>
/// Both medians are carried, not only the direction <see cref="GrowthDirection"/> already gives the
/// listing row: a board that only said "up" would be repeating the arrow, and the point of a board is
/// the two numbers a reader would otherwise have to visit two game pages to compare.
/// </remarks>
public sealed record TrendingGame(string Slug, string Name, int Median, int PriorMedian, int Samples, int PriorSamples)
{
    /// <summary>
    /// How much higher this week's median is than last week's, as a fraction of the larger — the
    /// same basis <see cref="GrowthTrend.Of"/> classifies up/steady/down from, so the board's order
    /// agrees with the arrow's own reading of the same two numbers.
    /// </summary>
    public double Change
    {
        get
        {
            var basis = Math.Max(Median, PriorMedian);
            return basis == 0 ? 0 : (Median - PriorMedian) / (double)basis;
        }
    }
}

/// <summary>
/// A game's current unbroken run of measured reachability.
/// </summary>
/// <remarks>
/// <see cref="Since"/> is carried because a duration alone means nothing without it — "reachable
/// since 12 March", never "reachable for four years". Reachable, not up (spec §5.8): we measured a
/// socket, not the game.
/// </remarks>
public sealed record ReachableSpell(string Slug, string Name, DateTimeOffset Since)
{
    public TimeSpan LengthAt(DateTimeOffset now) => now - Since;
}

/// <summary>
/// The rankings (spec §9), computed from measured data only.
/// </summary>
/// <remarks>
/// No voting affordance, ever — that's what reduced Top Mud Sites to a link graveyard (§2's permanent
/// non-goal), so every ranking here is a measurement with a stated basis: busiest by measured
/// concurrent players over a named window, among games with enough samples for a median.
/// <see cref="Eligible"/> beside <see cref="ListedGames"/> states which denominator a top ten was
/// drawn from. Archived games are excluded here and nowhere else (spec §7.5).
/// </remarks>
public sealed record Rankings(
    DateTimeOffset AsOf,
    TimeSpan Window,
    int MinimumSamples,
    int ListedGames,
    int Eligible,
    IReadOnlyList<BusiestGame> Busiest,
    IReadOnlyList<ReachableSpell> LongestUnbroken,

    /// <summary>
    /// The top gainers this week against last week — always the week span, unlike
    /// <see cref="Busiest"/>, since "trending" is inherently a fresh-vs-recent comparison and a
    /// 90-day version of it would be answering a different question under the same word.
    /// </summary>
    IReadOnlyList<TrendingGame> TrendingThisWeek)
{
    /// <summary>Which of the three windows this table was computed over.</summary>
    /// <remarks>
    /// Carried beside <see cref="Window"/>, not derived from it, so the selector highlights the span
    /// actually requested.
    /// </remarks>
    public RankingSpan Span { get; init; } = RankingSpan.Week;

    /// <summary>Days of the window a game must have been measured on to appear.</summary>
    public int MinimumDays => Span.MinimumDays();

    public static Rankings Empty(DateTimeOffset asOf, TimeSpan window, int minimumSamples) =>
        new(asOf, window, minimumSamples, 0, 0, [], [], []);
}
