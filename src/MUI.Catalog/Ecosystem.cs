namespace MUI.Catalog;

/// <summary>
/// A count over the set it was counted in. There is no way to hold a proportion here without the
/// denominator it is a proportion of.
/// </summary>
/// <remarks>
/// <para>
/// The structural half of spec §15.7. "62% of games offer UTF-8" is not a fact — it is a fact only
/// once "of the 431 games whose handshake we have completed" is attached to it, and a reader who
/// cannot see the denominator cannot tell a ratio over four hundred measurements from a ratio over
/// four. So the denominator is a field rather than a caption: a share cannot travel to a renderer
/// without it, and <see cref="Fraction"/> cannot be computed from anything else.
/// </para>
/// <para>
/// <see cref="Fraction"/> is null on an empty denominator rather than zero, for the same reason
/// <see cref="CapabilityState.Unknown"/> is not <see cref="CapabilityState.Absent"/>: nothing
/// measured is not nought per cent.
/// </para>
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
/// <para>
/// <see cref="NotIdentified"/> is carried beside the shares rather than folded into them. A game
/// whose codebase we could not read is not a game running "Other" — it is a game we have no answer
/// for, and rolling those into a residual bar would publish our own gap as somebody's market share.
/// </para>
/// <para>
/// <b><see cref="Lineages"/> is measured against the same denominator as <see cref="Families"/>,
/// and <see cref="NotClassified"/> is the price of that.</b> The tempting denominator is the games
/// we <em>did</em> place, which would make the bars add to 100% and every one of them wrong in the
/// same direction: a codebase we decline to classify would silently inflate the share of every
/// codebase we do. Our own abstention is not evidence about anybody's lineage, so it stays visible
/// as its own number and out of everyone else's — the same rule as <see cref="NotIdentified"/>,
/// one step further in.
/// </para>
/// <para>
/// Both are built by <see cref="Of"/> rather than by each <c>IGameQueries</c>, because the fixture
/// and the database had already drifted: one labelled a family with the commonest spelling and the
/// other with whichever row it read first, so the demo could name a codebase something the real
/// dashboard never would.
/// </para>
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
    /// <para>
    /// <b>A population of one cannot answer the question this panel asks.</b> Live, 49 of the 72
    /// families here were a single game at 0.5%: the tail was two-thirds of the list, and among it
    /// <c>Alter Aeon</c>, <c>Materia Magica</c>, <c>LuminariMUD</c> and <c>EternityMUD</c> — a game's
    /// own name, restated as its codebase, in a chart about what the hobby shares. So the split is
    /// here rather than in the surfaces, which would otherwise each decide where the tail begins.
    /// </para>
    /// <para>
    /// <b>The cut is the count, and deliberately not the name.</b> Refusing a <c>CODEBASE</c> that
    /// matches the game's own name was the obvious rule and it is wrong in both directions.
    /// <c>LambdaMOO</c>, <c>EmpireMUD</c>, <c>CircleMUD</c>, <c>Evennia</c> and <c>CoffeeMUD</c> are
    /// all codebases several games run whose flagship game carries the name, so it would blank a
    /// true field for exactly one member of a real family and leave the family's count wrong. And it
    /// would not reach <c>Rapture</c>, <c>Anatolia</c> or <c>OSB</c>, which are one-game rows saying
    /// just as little. Refusing the value outright would be worse than either: a game that told us
    /// what it runs would land in <see cref="NotIdentified"/>, which is our editorial decision
    /// recorded as their silence — rule 5, on the page that argues for it.
    /// </para>
    /// <para>
    /// Both halves are projections of <see cref="Families"/> rather than lists of their own. Two
    /// stored lists that must partition one set is an invitation for a surface to render a game
    /// twice or not at all, and there is nothing to check them against once they disagree.
    /// </para>
    /// </remarks>
    public IReadOnlyList<MeasuredShare> Shared => [.. Families.Where(share => share.Count > 1)];

    /// <summary>The codebases exactly one listed game runs, in the order the panel folds them.</summary>
    public IReadOnlyList<MeasuredShare> SoleUse => [.. Families.Where(share => share.Count == 1)];

    /// <summary>
    /// <see cref="SoleUse"/> as one share of the games that told us — one game each, so the count of
    /// codebases and the count of games are the same number.
    /// </summary>
    /// <remarks>
    /// Over <see cref="Identified"/>, the same denominator as every bar above it, so that
    /// <see cref="Shared"/> plus this is exactly the games that answered and a reader can add the
    /// page up. A fold that quietly narrowed the denominator would inflate every share left in it.
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
/// <para>
/// Two denominators because they are two different sets, and this is exactly the place where one
/// denominator for both would be a lie: <see cref="Handshakes"/> is the games whose session our
/// crawler completed, and <see cref="MsspReports"/> is the games whose MSSP report we hold. A game
/// can be in either, both or neither.
/// </para>
/// <para>
/// <b><see cref="Offered"/> is nullable, and the null is load-bearing.</b> It is null when the store
/// holds no measured observation of this protocol from any listed game — which is the state TLS is
/// in today, because the probe dials plain telnet and TLS is not a telnet option. Rendering that as
/// "0% of games offer TLS" would state a limit of our crawler as a fact about the hobby, which is
/// rule 5. Deriving it from the data rather than from a list compiled here means the column starts
/// reporting a share on its own the day the first measurement lands, and it fails in the safe
/// direction: a protocol genuinely nobody offers reads as unmeasured, which understates a claim
/// rather than manufacturing one.
/// </para>
/// <para>
/// <see cref="Declined"/> counts only games with an explicit measured <c>false</c>, and the crawler
/// writes one for exactly one protocol: MSSP, which every probe asks for by name, so silence there
/// is an answer. For every other protocol the remainder is <see cref="Unobserved"/> — we did not see
/// it, which is not the same fact as the game not having it. Every surface rendering this has to say
/// so; <see cref="Offered"/> is a floor and not a measurement of absence.
/// </para>
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
    /// Floored at nought rather than left to go negative. The two sides come from different tables —
    /// the denominator from availability and the numerator from stored fields — so a capability row
    /// on a game with no reachable interval, which a staff correction can write, would otherwise put
    /// a negative count on a public page. The residual is the wrong place to surface that: it is a
    /// number about the games, and an inconsistency of ours does not belong in it.
    /// </remarks>
    public int Unobserved => Math.Max(0, Handshakes - (Offered ?? 0) - Declined);

    /// <summary>The measured side, or null where nothing has ever been measured.</summary>
    public MeasuredShare? Measured =>
        Offered is { } offered ? new MeasuredShare(Protocol, offered, Handshakes) : null;

    /// <summary>
    /// The declared side. A share for every protocol but the one the column is made of.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Absence of a claim is not a claim, so this is a share and not a null for every ordinary
    /// protocol: a game that reported MSSP and did not mention GMCP has answered the question.
    /// </para>
    /// <para>
    /// <b>MSSP is the exception, and its denominator is what breaks.</b> Every one of the
    /// <see cref="MsspReports"/> games demonstrably supports MSSP — we are holding the report — so
    /// the true figure is all of them, and the one that rendered was <c>1 of 131</c>: the count of
    /// games that listed MSSP inside their own MSSP report. A share whose numerator counts a habit
    /// and whose denominator counts the fact is not a weak measurement, it is not a measurement, and
    /// the honest cell is empty. The measured side stays exactly as it is — see
    /// <see cref="EcosystemProtocols.Instrument"/> for why it is the strongest figure on the table
    /// rather than the weakest.
    /// </para>
    /// </remarks>
    public MeasuredShare? DeclaredShare =>
        EcosystemProtocols.IsInstrument(Protocol) ? null : new(Protocol, Declared, MsspReports);
}

/// <summary>
/// One protocol's adoption on one day (spec §9).
/// </summary>
/// <remarks>
/// The share is not stored, it is divided out of the same <see cref="ProtocolAdoption"/> the live
/// dashboard divides. A snapshot holding a percentage would be a number whose denominator had been
/// thrown away — uncheckable afterwards, and impossible to recompute if a share is ever defined
/// differently.
/// </remarks>
public sealed record AdoptionPoint(DateTimeOffset At, ProtocolAdoption Adoption);

/// <summary>
/// One protocol's history, and whether there is enough of it to call a curve.
/// </summary>
/// <remarks>
/// <see cref="IsCurve"/> exists so a surface can refuse to draw a line through one point. A single
/// snapshot rendered as a trend would be this site inventing a direction it has not measured, which
/// is the same fault as an invented zero wearing a different hat.
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
/// <para>
/// <b>Shares, not totals.</b> There is no player figure on this record and there is deliberately
/// nowhere to put one. §15.7 withholds the absolute "how many people play MU*" number because a
/// ratio over the measured set survives the unclaimed and unreachable biases and a count does not,
/// and a total that exists on the view model is a total somebody will render.
/// </para>
/// <para>
/// <b>A snapshot, and it says so.</b> The spec asks for adoption <em>curves</em>, and the store
/// cannot yet honestly draw one: <c>game_field</c> holds a current value with transitions beside it
/// in <c>field_change</c>, so a curve has to be reconstructed from transitions —
/// <see cref="CapabilityTransitions"/> is how many exist so far, and it is the count that says when
/// the curve becomes worth drawing. The tempting alternative is to plot each observation's
/// <c>first_seen_at</c>, which would draw a beautiful rising line that is a picture of our crawl
/// reaching more games and not of anybody adopting anything.
/// </para>
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
    /// Read beside <see cref="MsspReports"/> by the surfaces, which have two counts of one protocol
    /// to reconcile: we hold more reports than there are games offering MSSP today, because a report
    /// is not discarded when a game stops reissuing it.
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
/// Listed even when nothing is known about them, because "we have not measured TLS yet" is one of
/// the more useful things this page can say and it is only visible if the row exists. Every other
/// capability appears when there is something to report and is left out when there is not.
/// </remarks>
public static class EcosystemProtocols
{
    public static IReadOnlyList<string> Headline { get; } = ["TLS", "UTF-8", "GMCP", "MXP"];

    /// <summary>
    /// The protocol this page measures <em>with</em>, and the one row on it that is not a floor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Being the instrument makes its measured column stronger, not weaker.</b> The tempting
    /// move is to hold MSSP out of the adoption table on the grounds that its measured side is how
    /// the declared column exists at all — and that gets the argument exactly backwards. We do not
    /// request any other option, so every other measured figure is a floor and an unknown number of
    /// servers support things they never offered us. MSSP is the one we ask for by name, which is
    /// why it is the only row with a real negative beside it: 295 games were asked and declined.
    /// How much of this hobby publishes MSSP at all is also the fact that bounds what anybody can
    /// know about the rest of it, which makes it close to the most useful line on the page.
    /// </para>
    /// <para>
    /// What being the instrument does break is the declared cell, and only that —
    /// <see cref="ProtocolAdoption.DeclaredShare"/> is null here for a denominator that cannot be
    /// fixed rather than a number that is merely small.
    /// </para>
    /// </remarks>
    public const string Instrument = "MSSP";

    public static bool IsInstrument(string protocol) =>
        string.Equals(protocol, Instrument, StringComparison.Ordinal);
}


/// <summary>
/// How far back the busiest ranking looks (spec §9).
/// </summary>
/// <remarks>
/// <para>
/// Three named spans rather than an arbitrary range, because a ranking is a claim and each of these
/// is a different one: a week says who is busy now, a quarter says who has been busy. An open range
/// would let a reader pick a fortnight in which one game happened to be linked from somewhere and
/// call the result a ranking.
/// </para>
/// <para>
/// The windows are <b>day-aligned in UTC</b> and not rolling from the current instant: the median
/// behind them is summed out of <c>presence_rollup_day</c>, whose buckets are whole UTC days
/// (migration 0019). A rolling window would have to cut the oldest bucket in half and could not,
/// so the honest description — and the one the page prints — is "the last N days" meaning N whole
/// day buckets, the current, partial one included.
/// </para>
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
    /// Unknown input falls back to the default rather than erroring: a ranking is a browsable
    /// surface, and a mistyped window is not worth a 400 in front of a page that has a right answer
    /// to give.
    /// </remarks>
    public static RankingSpan Parse(string? slug) =>
        All.FirstOrDefault(s => string.Equals(s.Slug(), slug, StringComparison.OrdinalIgnoreCase),
            RankingSpan.Week);

    /// <summary>
    /// How many of the window's days a game must have been measured on to be ranked in it.
    /// </summary>
    /// <remarks>
    /// Half, rounded up. The sample floor alone does not scale: a game probed hard for two days
    /// clears twenty-four samples and would then rank in a ninety-day table off two days of data,
    /// which is a claim about a quarter made from a weekend. Coverage is what makes the longer
    /// windows mean what they say, and it is why the floor itself stays flat.
    /// </remarks>
    public static int MinimumDays(this RankingSpan span) => (span.Days() + 1) / 2;
}

/// <summary>
/// One game in the busiest ranking, with the measurements the rank is computed from beside it.
/// </summary>
/// <remarks>
/// The median rather than the peak, because a peak is one sample and a game that had forty players
/// for a minute is not busier than one that has thirty all day. Both are carried anyway, with the
/// number of samples they were taken over, so the basis is on the page rather than in a footnote —
/// a ranking whose arithmetic a reader cannot check is the kind of ranking §2 refuses.
/// <see cref="Median"/> is an observed value and never an average of two, so it is a number some
/// probe actually read.
///
/// <see cref="Days"/> is the second half of the basis and matters more the wider the window gets:
/// twelve hundred samples over a quarter is a different fact depending on whether they came from
/// ninety days or from three, and the eligibility rule the table applies (<see
/// cref="RankingSpans.MinimumDays"/>) is stated in the same units.
/// </remarks>
public sealed record BusiestGame(string Slug, string Name, int Median, int Peak, int Samples, int Days = 0);

/// <summary>
/// A game's current unbroken run of measured reachability.
/// </summary>
/// <remarks>
/// <see cref="Since"/> is carried rather than a duration alone because the duration means nothing
/// without it: a spell cannot be longer than we have been watching, so the honest sentence is
/// "reachable on every probe since 12 March" and never "reachable for four years". Reachable, not
/// up (spec §5.8) — we measured a socket from one vantage point.
/// </remarks>
public sealed record ReachableSpell(string Slug, string Name, DateTimeOffset Since)
{
    public TimeSpan LengthAt(DateTimeOffset now) => now - Since;
}

/// <summary>
/// The rankings (spec §9), computed from measured data only.
/// </summary>
/// <remarks>
/// <para>
/// There is no voting affordance on this site and there never will be — that is what reduced Top Mud
/// Sites to a link graveyard, and it is the first thing on §2's permanent non-goals. So every
/// ranking here has to be a measurement with a stated basis, which rules out "best" and rules in
/// "busiest by measured concurrent players over a named window, among games that produced enough
/// samples to have a median".
/// </para>
/// <para>
/// <see cref="Eligible"/> beside <see cref="ListedGames"/> is the denominator rule applied to a
/// league table: a top ten drawn from twelve eligible games and a top ten drawn from four hundred
/// are different claims, and the difference is invisible unless the page says which it is. Archived
/// games are excluded here and from nothing else (spec §7.5).
/// </para>
/// </remarks>
public sealed record Rankings(
    DateTimeOffset AsOf,
    TimeSpan Window,
    int MinimumSamples,
    int ListedGames,
    int Eligible,
    IReadOnlyList<BusiestGame> Busiest,
    IReadOnlyList<ReachableSpell> LongestUnbroken)
{
    /// <summary>Which of the three windows this table was computed over.</summary>
    /// <remarks>
    /// Carried beside <see cref="Window"/> rather than derived from it, so the selector highlights
    /// the span that was actually asked for instead of one recovered by matching a duration.
    /// </remarks>
    public RankingSpan Span { get; init; } = RankingSpan.Week;

    /// <summary>Days of the window a game must have been measured on to appear.</summary>
    public int MinimumDays => Span.MinimumDays();

    public static Rankings Empty(DateTimeOffset asOf, TimeSpan window, int minimumSamples) =>
        new(asOf, window, minimumSamples, 0, 0, [], []);
}
