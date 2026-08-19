using MUI.Catalog;
using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>
/// Every sentence the ecosystem dashboard and the rankings say, in one place.
/// </summary>
/// <remarks>
/// Shared by the graphical and plain surfaces so the wording cannot drift between them — a share is
/// meaningless without its denominator, and duplicating that qualification risks the two disagreeing.
/// Every member takes the locale first, the same convention <see cref="FacetWords"/> and
/// <see cref="ActiveFilters"/> keep.
/// </remarks>
public static class EcosystemCopy
{
    /// <summary>
    /// The MSSP field and value a game publishes to say its codebase belongs to no family.
    /// </summary>
    /// <remarks>
    /// Machine voice, so it is an argument to the lineage sentence rather than words inside it: a
    /// locale that translated <c>FAMILY Custom</c> would be naming a field no server has.
    /// </remarks>
    public const string CustomFamily = "FAMILY Custom";

    /// <summary>
    /// A share as a number over the set it was counted in, always in that order.
    /// </summary>
    /// <remarks>
    /// Count and denominator come first, percentage second — it is the derived, quotable figure. An
    /// empty denominator reads as nothing measured, not as 0% (0 of 0 is not 0%). The percentage goes
    /// through the message formatter rather than a format string, since its decimal separator and
    /// sign spacing are locale-dependent.
    /// </remarks>
    public static string Share(string tag, MeasuredShare share)
    {
        ArgumentNullException.ThrowIfNull(share);

        return share.Fraction is { } fraction
            ? Messages.Say(tag, "ecosystem.share",
                ("count", share.Count), ("total", share.Denominator), ("fraction", fraction))
            : Messages.Say(tag, "ecosystem.share.nothing",
                ("count", share.Count), ("total", share.Denominator));
    }

    /// <summary>
    /// What the codebase shares are a fraction of, with the listing beside it.
    /// </summary>
    /// <remarks>
    /// Both numbers, deliberately: showing only the identified count let a reader mistake it for the
    /// full catalogue size — the denominator rule failing in the one direction it exists to catch.
    /// </remarks>
    public static string CodebaseBasis(string tag, CodebaseUsage codebases)
    {
        ArgumentNullException.ThrowIfNull(codebases);

        return Messages.Say(tag, "ecosystem.codebases.basis",
            ("listed", codebases.Identified + codebases.NotIdentified),
            ("identified", codebases.Identified));
    }

    /// <summary>
    /// The line that folds away the codebases only one game runs, and says what it folded.
    /// </summary>
    /// <remarks>
    /// Says "folded" and lists the games under it, rather than quietly dropping them — an editorial
    /// decision must not be hidden inside somebody else's numbers. The fold's claim (one game each,
    /// still inside the denominator) stays checkable against the list it opens.
    /// </remarks>
    public static string SoleUse(string tag, CodebaseUsage codebases)
    {
        ArgumentNullException.ThrowIfNull(codebases);

        return Messages.Say(tag, "ecosystem.soleUse", ("share", Share(tag, codebases.SoleUseTotal)));
    }

    /// <summary>
    /// How to read the MSSP row, whose two counts differ and whose declared cell is empty.
    /// </summary>
    /// <remarks>
    /// The gap is "nothing is ever deleted" showing through: a report is kept after a game stops
    /// publishing one, so the reports we hold outnumber the games offering MSSP today. Left
    /// unreconciled, the two numbers read as an arithmetic error.
    /// </remarks>
    public static string MsspBasis(string tag, ProtocolAdoption mssp, int reports)
    {
        ArgumentNullException.ThrowIfNull(mssp);

        var opening = Messages.Say(tag, "ecosystem.mssp.instrument",
            ("instrument", EcosystemProtocols.Instrument));

        if (mssp.Offered is not { } offered)
        {
            return opening;
        }

        var gap = reports - offered;

        return gap <= 0
            ? opening
            : opening + " " + Messages.Say(tag, "ecosystem.mssp.gap",
                ("reports", reports), ("offered", offered), ("gap", gap));
    }

    /// <summary>What the measured column is a fraction of, spelled out wherever it is used.</summary>
    public static string Handshakes(string tag, int games) =>
        Messages.Say(tag, "ecosystem.handshakes", ("count", games), ("value", games));

    /// <summary>What the declared column is a fraction of. A different set, deliberately named apart.</summary>
    public static string MsspReports(string tag, int games) =>
        Messages.Say(tag, "ecosystem.msspReports", ("count", games), ("value", games));

    /// <summary>How many games are listed at all — the set the two denominators are drawn from.</summary>
    public static string Listed(string tag, int games) =>
        Messages.Say(tag, "ecosystem.listed", ("count", games), ("value", games));

    /// <summary>
    /// The measured side of one protocol, including the case where there is no measurement.
    /// </summary>
    /// <remarks>
    /// Four whole ids rather than a share with clauses appended — assembling fragments in English
    /// word order leaves other languages nowhere to reorder or inflect them.
    /// </remarks>
    public static string Measured(string tag, ProtocolAdoption protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);

        if (protocol.Measured is not { } share)
        {
            // Never "0%" — nothing observed is a statement about our reach, not the hobby. TLS is
            // the standing case: the crawler dials plain telnet, so TLS is never offered to it.
            return Messages.Say(tag, "ecosystem.measured.never");
        }

        var figure = Share(tag, share);

        return (protocol.Declined > 0, protocol.Unobserved > 0) switch
        {
            (true, true) => Messages.Say(tag, "ecosystem.measured.declinedAndUnasked",
                ("share", figure), ("declined", protocol.Declined), ("unobserved", protocol.Unobserved)),
            (true, false) => Messages.Say(tag, "ecosystem.measured.declined",
                ("share", figure), ("declined", protocol.Declined)),
            (false, true) => Messages.Say(tag, "ecosystem.measured.unasked",
                ("share", figure), ("unobserved", protocol.Unobserved)),
            _ => figure,
        };
    }

    /// <summary>
    /// The declared side of one protocol. A share where there is one to state.
    /// </summary>
    /// <remarks>
    /// A missing claim is not a claim: a protocol nobody declared is 0%, not blank. Blank is reserved
    /// for MSSP, where every report we hold already proves support, leaving no population to be a
    /// share of.
    /// </remarks>
    public static string Declared(string tag, ProtocolAdoption protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);

        return protocol.DeclaredShare is { } share
            ? Share(tag, share)
            : Messages.Say(tag, "ecosystem.declared.none");
    }

    /// <summary>
    /// The sentence without which the measured column is a lie by omission.
    /// </summary>
    /// <remarks>
    /// The measured column is a floor, not a ceiling: the crawler requests MSSP alone and declines
    /// MCCP outright, so live servers implementing protocols they were never asked for read as
    /// unmeasured. Rendered without this sentence, our own instrumentation reads as a fact about
    /// somebody's game.
    /// </remarks>
    public static string Floor(string tag) => Messages.Say(tag, "ecosystem.protocols.floor");

    /// <summary>Why there is a snapshot here and not the curve §9 asks for.</summary>
    /// <remarks>
    /// Plotting <c>first_seen_at</c> would draw a confident rising line measuring our crawler
    /// reaching more games — nothing about adoption.
    /// </remarks>
    public static string NoCurve(string tag) => Messages.Say(tag, "ecosystem.snapshot");

    /// <summary>
    /// What a drawn curve does and does not measure, said beside the curve rather than under it.
    /// </summary>
    /// <remarks>
    /// A share over the measured set moves for two reasons — a game changing its mind, and the set's
    /// composition changing — and only the first is adoption. A month of finding four hundred
    /// DikuMUDs would move every share here without one game changing anything.
    /// </remarks>
    public static string CurveCaveat(string tag) => Messages.Say(tag, "ecosystem.curve.caveat");

    /// <summary>Why there is no headline population figure, said where somebody might look for one.</summary>
    public static string NoTotals(string tag) => Messages.Say(tag, "ecosystem.noTotals");

    /// <summary>How many capability transitions have been recorded, and what that means for the curve.</summary>
    public static string Transitions(string tag, int transitions) => transitions == 0
        ? Messages.Say(tag, "ecosystem.transitions.none")
        : Messages.Say(tag, "ecosystem.transitions", ("count", transitions));

    /// <summary>The basis of the busiest table, stated on the page rather than in a footnote.</summary>
    public static string BusiestBasis(string tag, Rankings rankings)
    {
        ArgumentNullException.ThrowIfNull(rankings);

        // Three whole sentences joined by a space, rather than one arithmetic-heavy sentence, so
        // each is a translator's unit.
        var days = (int)rankings.Window.TotalDays;

        var eligible = rankings.Eligible == 0
            ? Messages.Say(tag, "rankings.basis.none",
                ("samples", rankings.MinimumSamples), ("days", rankings.MinimumDays))
            : Messages.Say(tag, "rankings.basis.eligible",
                ("eligible", rankings.Eligible),
                ("listed", rankings.ListedGames),
                ("samples", rankings.MinimumSamples),
                ("days", rankings.MinimumDays));

        return string.Join(' ',
            Messages.Say(tag, "rankings.basis.median", ("days", days)),
            eligible,
            Messages.Say(tag, "rankings.basis.zero"));
    }

    /// <summary>
    /// Why the window can be changed, said once beside the selector.
    /// </summary>
    /// <remarks>
    /// Each window is a different claim, not the same one at three resolutions — two games can
    /// honestly swap places between "busy now" and "busy for months".
    /// </remarks>
    public static string SpanChoice(string tag) => Messages.Say(tag, "rankings.spanChoice");

    /// <summary>The window as it is offered in the selector.</summary>
    /// <remarks>One message for all three spans and the fallback; only the day count differs.</remarks>
    public static string SpanLabel(string tag, RankingSpan span) =>
        Messages.Say(tag, "rankings.span", ("days", span.Days()));

    /// <summary>What the second table is, and the limit it cannot be read past.</summary>
    public static string SpellBasis(string tag) => Messages.Say(tag, "rankings.spells.basis");

    /// <summary>
    /// Said on the rankings page, because §2 makes it permanent rather than pending.
    /// </summary>
    /// <remarks>
    /// States the rule, not its history — the vote-gaming story lives once, on /about. A reader at a
    /// league table wants to know what it measures before they want the reasoning.
    /// </remarks>
    public static string NoVote(string tag) => Messages.Say(tag, "rankings.noVote");
}
