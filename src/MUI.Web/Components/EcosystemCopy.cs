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

    /// <summary>What the measured column is a fraction of, spelled out wherever it is used.</summary>
    public static string Handshakes(int games) =>
        $"{Games(games)} whose handshake we have completed";

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
            return "not measured — nothing has been observed to offer it";
        }

        var rest = protocol.Declined > 0
            ? $" · {Games(protocol.Declined)} declined it when asked"
            : string.Empty;

        return protocol.Unobserved > 0
            ? $"{Share(share)}{rest} · {Games(protocol.Unobserved)} neither offered nor were asked"
            : $"{Share(share)}{rest}";
    }

    /// <summary>The declared side of one protocol. Always a share; a missing claim is not a claim.</summary>
    public static string Declared(ProtocolAdoption protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);

        return Share(protocol.DeclaredShare);
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
        "A protocol we did not see is not a protocol a game lacks. MSSP is asked for by name on "
        + "every probe, so silence there is an answer and is counted as one; nothing else on this "
        + "list is requested at all, and a server is free to support a protocol and never offer it. "
        + "Read every measured figure below as a floor.";

    /// <summary>Why there is a snapshot here and not the curve §9 asks for.</summary>
    /// <remarks>
    /// The alternative was available and is the reason this paragraph exists: every observation
    /// carries a <c>first_seen_at</c>, and plotting those would draw a confident rising line that
    /// measures our crawler reaching more games and nothing whatever about adoption.
    /// </remarks>
    public const string NoCurve =
        "This is a snapshot of what we can measure now, not a trend. A protocol adoption curve is a "
        + "plot of games changing their minds, and the catalogue records a change only when it "
        + "happens — so the curve becomes drawable once enough transitions have been recorded, and "
        + "not before. Plotting when we first reached each game instead would draw a rising line "
        + "measuring the crawl rather than the hobby.";

    /// <summary>Why there is no headline population figure, said where somebody might look for one.</summary>
    public const string NoTotals =
        "Shares, never totals. How many people play MU* is a number this site deliberately does not "
        + "publish: a ratio over the games we measured survives the ones we cannot reach and the ones "
        + "nobody has claimed, and a headcount does not survive either.";

    /// <summary>How many capability transitions have been recorded, and what that means for the curve.</summary>
    public static string Transitions(int transitions) => transitions == 0
        ? "No measured capability has changed since we started watching, so there is nothing to plot yet."
        : $"{Recorded(transitions)} recorded so far. That is the material a curve is drawn from.";

    /// <summary>The basis of the busiest table, stated on the page rather than in a footnote.</summary>
    public static string BusiestBasis(Rankings rankings)
    {
        ArgumentNullException.ThrowIfNull(rankings);

        return $"Ranked on the median of the player counts we measured over the last "
            + $"{(int)rankings.Window.TotalDays} days — {rankings.Eligible} of the "
            + $"{Games(rankings.ListedGames)} listed produced the {rankings.MinimumSamples} counted "
            + "samples a median needs. A probe that got in and could not read a number is not a "
            + "zero and is not among them; a measured zero is a count and is.";
    }

    /// <summary>What the second table is, and the limit it cannot be read past.</summary>
    public const string SpellBasis =
        "Every probe since the date given found the game reachable. Reachable, not up — we measured "
        + "a socket from one vantage point, and a game with a routing problem to our host is "
        + "unreachable and perfectly alive. A spell cannot be longer than we have been watching, "
        + "which is why the date is the fact and the duration is derived from it.";

    /// <summary>Said on the rankings page, because §2 makes it permanent rather than pending.</summary>
    public const string NoVote =
        "Computed from measured data only. There is no vote, star or rating anywhere on this site "
        + "and there never will be: vote-gaming is what reduced the last directory that tried it to "
        + "a link graveyard. Nothing here ranks games by better or best, because we have not "
        + "measured that and nobody can.";

    private static string Games(int n) => n == 1 ? "1 game" : $"{n} games";

    private static string Recorded(int n) => n == 1 ? "1 capability change" : $"{n} capability changes";
}
