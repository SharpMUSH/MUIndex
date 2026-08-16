using MUI.Catalog;

namespace MUI.Web.Components;

/// <summary>
/// Every sentence the ecosystem dashboard and the rankings say, in one place.
/// </summary>
/// <remarks>
/// <para>
/// The graphical page and the plain surface render the same numbers, and on these two pages the
/// numbers are almost entirely made of their qualifications: a share is meaningless without its
/// denominator, and a protocol column is misleading without the sentence saying that a protocol we
/// did not see is not a protocol a game lacks. Sentences that load-bearing cannot be written twice —
/// the plain copy would drift into being the honest one and the graphic into being the quotable one,
/// which is precisely the failure mode §9 says plain mode exists to catch.
/// </para>
/// <para>
/// So the wording lives here and both surfaces read it. What differs between them is layout: one has
/// a bar beside the number and the other does not, and the bar is an illustration of a sentence that
/// is already complete without it.
/// </para>
/// </remarks>
public static class EcosystemCopy
{
    /// <summary>
    /// A share as a number over the set it was counted in, always in that order.
    /// </summary>
    /// <remarks>
    /// The count and the denominator come first and the percentage second, because the percentage is
    /// the derived figure and the one that travels when somebody quotes the page. An empty
    /// denominator reads as nothing measured rather than as nought per cent — 0 of 0 is not 0%.
    /// </remarks>
    public static string Share(MeasuredShare share)
    {
        ArgumentNullException.ThrowIfNull(share);

        return share.Fraction is { } fraction
            ? $"{share.Count} of {share.Denominator} ({Wording.Percent(fraction)})"
            : $"{share.Count} of {share.Denominator} — nothing measured yet";
    }

    /// <summary>
    /// What the codebase shares are a fraction of, with the listing beside it.
    /// </summary>
    /// <remarks>
    /// <b>Both numbers, because one of them read as the other.</b> This said "Share of the 144
    /// listed games that told us what they run", which puts the identified count exactly where the
    /// size of the catalogue belongs — and a reader with no reason to doubt it came away believing
    /// the site lists 144 games rather than 418. That is the denominator rule failing in the one
    /// direction it exists to catch, on the page that argues for it, so the denominator and the set
    /// it was drawn from are now in the same sentence and neither can be mistaken for the other.
    /// </remarks>
    public static string CodebaseBasis(CodebaseUsage codebases)
    {
        ArgumentNullException.ThrowIfNull(codebases);

        var listed = codebases.Identified + codebases.NotIdentified;

        return $"Of the {Games(listed)} listed, {codebases.Identified} told us what they run, and "
            + $"every share below is over those {codebases.Identified}. A codebase we could not "
            + "read is left out of the denominator, never counted as something else.";
    }

    /// <summary>
    /// How to read the MSSP row, whose two counts differ and whose declared cell is empty.
    /// </summary>
    /// <remarks>
    /// The gap is the honest half of "nothing is ever deleted" showing through: a report we read
    /// once is kept when the game stops publishing one, so the set we hold reports from is larger
    /// than the set offering MSSP today. Two numbers a reader can subtract have to be reconciled on
    /// the page — left alone they read as an arithmetic error, which is how this one was found.
    /// </remarks>
    public static string MsspBasis(ProtocolAdoption mssp, int reports)
    {
        ArgumentNullException.ThrowIfNull(mssp);

        var opening = $"{EcosystemProtocols.Instrument} is the one row below that is not a floor: "
            + "we ask every server for it by name, so the games that did not offer it were asked "
            + "and declined. It is also the only one with no declared figure, because every game "
            + "whose report we hold supports it by demonstration and a count of the ones that also "
            + "listed it would measure a habit against that.";

        if (mssp.Offered is not { } offered)
        {
            return opening;
        }

        var gap = reports - offered;

        return gap <= 0
            ? opening
            : $"{opening} We hold {reports} reports and {Games(offered)} offer MSSP today: the "
                + $"other {gap} stopped publishing one after we read it, and a report is not "
                + "thrown away because it stopped being reissued.";
    }

    /// <summary>What the measured column is a fraction of, spelled out wherever it is used.</summary>
    public static string Handshakes(int games) =>
        $"{Games(games)} whose handshake we completed";

    /// <summary>What the declared column is a fraction of. A different set, deliberately named apart.</summary>
    public static string MsspReports(int games) =>
        $"{Games(games)} whose MSSP report we hold";

    /// <summary>The measured side of one protocol, including the case where there is no measurement.</summary>
    public static string Measured(ProtocolAdoption protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);

        if (protocol.Measured is not { } share)
        {
            // Never "0%". Nothing has ever been observed to offer this, which is a statement about
            // our reach and not about the hobby — TLS is the standing case, because the crawler dials
            // plain telnet and TLS is not a telnet option.
            return "not measured — never observed";
        }

        var rest = protocol.Declined > 0
            ? $" · {Games(protocol.Declined)} declined when asked"
            : string.Empty;

        return protocol.Unobserved > 0
            ? $"{Share(share)}{rest} · {Games(protocol.Unobserved)} neither offered nor asked"
            : $"{Share(share)}{rest}";
    }

    /// <summary>
    /// The declared side of one protocol. A share where there is one to state.
    /// </summary>
    /// <remarks>
    /// A missing claim is not a claim, so a protocol nobody declared is 0% and not a blank. The
    /// blank is reserved for the case where the denominator itself cannot carry the question, which
    /// is MSSP and only MSSP: every game whose report we hold has proved it supports MSSP by
    /// sending one, so there is no population left over to be a share of.
    /// </remarks>
    public static string Declared(ProtocolAdoption protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);

        return protocol.DeclaredShare is { } share
            ? Share(share)
            : "not asked — every report here is the answer";
    }

    /// <summary>
    /// The sentence without which the measured column is a lie by omission.
    /// </summary>
    /// <remarks>
    /// The crawler writes a capability down when it observes one and otherwise writes nothing, and it
    /// is right to: it requests MSSP alone, declines MCCP outright, and has been measured against
    /// live servers that plainly implement protocols they never offered our handshake. So the only
    /// honest reading of the measured column is a floor, and a page that renders it without saying so
    /// publishes our own instrumentation as a fact about somebody's game.
    /// </remarks>
    public const string Floor =
        "Read every measured figure below as a floor. We ask for MSSP by name, so silence there is "
        + "an answer. Nothing else here is requested, and a server may support a protocol without "
        + "ever offering it.";

    /// <summary>Why there is a snapshot here and not the curve §9 asks for.</summary>
    /// <remarks>
    /// The alternative was available and is the reason this paragraph exists: every observation
    /// carries a <c>first_seen_at</c>, and plotting those would draw a confident rising line that
    /// measures our crawler reaching more games and nothing whatever about adoption.
    /// </remarks>
    public const string NoCurve =
        "A snapshot of what we can measure now. An adoption curve plots games changing their minds, "
        + "and we record a change when it happens, so the curve becomes drawable once enough have "
        + "been recorded. Plotting when we first reached each game would measure the crawl, not the "
        + "hobby.";

    /// <summary>
    /// What a drawn curve does and does not measure, said beside the curve rather than under it.
    /// </summary>
    /// <remarks>
    /// The same care <see cref="NoCurve"/> takes, applied to the thing that replaced it. A share over
    /// the measured set moves for two reasons — a game changing its mind, and the set changing
    /// composition — and only the first is adoption. A month in which the crawler found four hundred
    /// DikuMUDs would move every share on this page without one game having changed anything, and a
    /// reader deserves to be told that before they read a slope as a trend.
    /// </remarks>
    public const string CurveCaveat =
        "Each point is a share over the games we had measured that day, so this line moves for two "
        + "reasons: a game changing what it offers, and the set of games we can measure changing "
        + "around it. Only the first is adoption. The transition count below is the part that is "
        + "purely games changing their minds.";

    /// <summary>Why there is no headline population figure, said where somebody might look for one.</summary>
    public const string NoTotals =
        "Shares, never totals. We do not publish a figure for how many people play MU*: a ratio over "
        + "the games we measured survives the ones we cannot reach, and a headcount does not.";

    /// <summary>How many capability transitions have been recorded, and what that means for the curve.</summary>
    public static string Transitions(int transitions) => transitions == 0
        ? "No measured capability has changed yet, so there is nothing to plot."
        : $"{Recorded(transitions)} recorded so far — the material a curve is drawn from.";

    /// <summary>The basis of the busiest table, stated on the page rather than in a footnote.</summary>
    public static string BusiestBasis(Rankings rankings)
    {
        ArgumentNullException.ThrowIfNull(rankings);

        return $"Median of the player counts we measured over the last "
            + $"{(int)rankings.Window.TotalDays} days. {rankings.Eligible} of "
            + $"{Games(rankings.ListedGames)} listed produced the {rankings.MinimumSamples} counted "
            + $"samples a median needs, on at least {Days(rankings.MinimumDays)} of the window. "
            + "A measured zero counts; an unreadable count does not.";
    }

    /// <summary>
    /// Why the window can be changed, said once beside the selector.
    /// </summary>
    /// <remarks>
    /// Each window is a different claim rather than the same one at three resolutions, and the
    /// sentence says so — a reader comparing the tabs is comparing "busy now" with "busy for
    /// months", and two games can honestly swap places between them.
    /// </remarks>
    public const string SpanChoice =
        "A week says who is busy now; a quarter says who has been busy. They are different questions "
        + "and a game can lead one and not the other. Days are whole days, UTC.";

    /// <summary>The window as it is offered in the selector.</summary>
    public static string SpanLabel(RankingSpan span) => span switch
    {
        RankingSpan.Week => "7 days",
        RankingSpan.Month => "30 days",
        RankingSpan.Quarter => "90 days",
        _ => $"{span.Days()} days",
    };

    /// <summary>What the second table is, and the limit it cannot be read past.</summary>
    public const string SpellBasis =
        "Every probe since the date given found the game reachable. Reachable, not up: we measure a "
        + "socket from one host, and a game we cannot route to is perfectly alive. A spell cannot be "
        + "longer than we have been watching, so the date is the fact and the duration follows.";

    /// <summary>Said on the rankings page, because §2 makes it permanent rather than pending.</summary>
    public const string NoVote =
        "Computed from measured data only. No votes, stars or ratings, ever — vote-gaming is what "
        + "emptied the last directory that tried it. Nothing here ranks games by best; we have not "
        + "measured that and nobody can.";

    private static string Games(int n) => n == 1 ? "1 game" : $"{n} games";

    private static string Days(int n) => n == 1 ? "1 day" : $"{n} days";

    private static string Recorded(int n) => n == 1 ? "1 capability change" : $"{n} capability changes";
}
