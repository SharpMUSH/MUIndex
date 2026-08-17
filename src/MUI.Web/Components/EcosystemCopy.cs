using MUI.Catalog;
using MUI.Web.Localization;

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
/// <para>
/// <b>Every member takes the locale first.</b> These are sentences rather than fragments and the
/// numbers inside them agree with the language they are spoken in, so there is no locale-free form
/// of any of them to fall back on — the same convention <see cref="FacetWords"/> and
/// <see cref="ActiveFilters"/> already keep.
/// </para>
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
    /// The count and the denominator come first and the percentage second, because the percentage is
    /// the derived figure and the one that travels when somebody quotes the page. An empty
    /// denominator reads as nothing measured rather than as nought per cent — 0 of 0 is not 0%.
    /// The percentage goes through the message formatter rather than through a format string,
    /// because a decimal comma and the space some locales put before the sign are part of the
    /// language and not of the number.
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
    /// <b>Both numbers, because one of them read as the other.</b> This said "Share of the 144
    /// listed games that told us what they run", which puts the identified count exactly where the
    /// size of the catalogue belongs — and a reader with no reason to doubt it came away believing
    /// the site lists 144 games rather than 418. That is the denominator rule failing in the one
    /// direction it exists to catch, on the page that argues for it, so the denominator and the set
    /// it was drawn from are now in the same sentence and neither can be mistaken for the other.
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
    /// <para>
    /// The number comes first and the reason second, so the sentence is complete before it is
    /// justified: this is the panel's second-largest figure and it may not read as a footnote.
    /// </para>
    /// <para>
    /// <b>It says "folded", and the games are listed under it.</b> A page that quietly dropped
    /// fifty rows and left a chart that no longer adds up would be hiding our own editorial decision
    /// inside somebody else's numbers, which is the move this site exists to refuse. What the fold
    /// claims — one game each, still inside the denominator — is checkable against the list it opens.
    /// </para>
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
    /// The gap is the honest half of "nothing is ever deleted" showing through: a report we read
    /// once is kept when the game stops publishing one, so the set we hold reports from is larger
    /// than the set offering MSSP today. Two numbers a reader can subtract have to be reconciled on
    /// the page — left alone they read as an arithmetic error, which is how this one was found.
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
    /// Four ids and not a share with clauses appended. "· 6 games neither offered nor asked" is a
    /// fragment in English word order, and a language that puts the qualification first or inflects
    /// the noun for the clause it sits in has nowhere to say so if the sentence arrives in pieces.
    /// </remarks>
    public static string Measured(string tag, ProtocolAdoption protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);

        if (protocol.Measured is not { } share)
        {
            // Never "0%". Nothing has ever been observed to offer this, which is a statement about
            // our reach and not about the hobby — TLS is the standing case, because the crawler dials
            // plain telnet and TLS is not a telnet option.
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
    /// A missing claim is not a claim, so a protocol nobody declared is 0% and not a blank. The
    /// blank is reserved for the case where the denominator itself cannot carry the question, which
    /// is MSSP and only MSSP: every game whose report we hold has proved it supports MSSP by
    /// sending one, so there is no population left over to be a share of.
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
    /// The crawler writes a capability down when it observes one and otherwise writes nothing, and it
    /// is right to: it requests MSSP alone, declines MCCP outright, and has been measured against
    /// live servers that plainly implement protocols they never offered our handshake. So the only
    /// honest reading of the measured column is a floor, and a page that renders it without saying so
    /// publishes our own instrumentation as a fact about somebody's game.
    /// </remarks>
    public static string Floor(string tag) => Messages.Say(tag, "ecosystem.protocols.floor");

    /// <summary>Why there is a snapshot here and not the curve §9 asks for.</summary>
    /// <remarks>
    /// The alternative was available and is the reason this sentence exists: every observation
    /// carries a <c>first_seen_at</c>, and plotting those would draw a confident rising line that
    /// measures our crawler reaching more games and nothing whatever about adoption.
    /// </remarks>
    public static string NoCurve(string tag) => Messages.Say(tag, "ecosystem.snapshot");

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

        // "0 of 519 games listed produced the 24 counted samples a median needs, on at least 4 days
        // of the window" is arithmetic where a sentence would do. Where nothing qualifies, say that;
        // where something does, the count is the fact and the threshold follows it. Three whole
        // sentences joined by a space, so each one is a translator's unit rather than a clause.
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
    /// Each window is a different claim rather than the same one at three resolutions, and the
    /// sentence says so — a reader comparing the tabs is comparing "busy now" with "busy for
    /// months", and two games can honestly swap places between them.
    /// </remarks>
    public static string SpanChoice(string tag) => Messages.Say(tag, "rankings.spanChoice");

    /// <summary>The window as it is offered in the selector.</summary>
    /// <remarks>
    /// One message for all three and for the fallback alike, rather than three literals and a
    /// format string: the agreement is the same job in every case, and the day count is the only
    /// thing that differs.
    /// </remarks>
    public static string SpanLabel(string tag, RankingSpan span) =>
        Messages.Say(tag, "rankings.span", ("days", span.Days()));

    /// <summary>What the second table is, and the limit it cannot be read past.</summary>
    public static string SpellBasis(string tag) => Messages.Say(tag, "rankings.spells.basis");

    /// <summary>
    /// Said on the rankings page, because §2 makes it permanent rather than pending.
    /// </summary>
    /// <remarks>
    /// Two sentences, not four. What vote-gaming did to the last directory that tried it is the
    /// argument for the rule and it is told once, on /about, where the reasons live; here the rule
    /// itself is the fact, and a reader who has arrived at a league table wants to know what it does
    /// and does not measure before they want the history.
    /// </remarks>
    public static string NoVote(string tag) => Messages.Say(tag, "rankings.noVote");
}
