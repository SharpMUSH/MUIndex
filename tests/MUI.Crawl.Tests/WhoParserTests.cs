using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// The structural WHO parser, tested against responses captured from live servers.
/// </summary>
/// <remarks>
/// Every fixture in the first two tests is real. Inventing them would have produced a tidy
/// <c>Doing</c> header and a numeric footer, and missed both of the cases that actually matter.
/// </remarks>
public class WhoParserTests
{
    private static readonly WhoParser Parser = new();

    /// <summary>
    /// mush.pennmush.org:4201, captured 2026-07-30. Note the last column header: the operator
    /// renamed <c>DOING</c> in softcode, so no dialect table would find it.
    /// </summary>
    private const string MushResponse = """
        Player Name          On For   Idle  ThereIsNoSpoonButIWantYogurt
        Xperta               9m 21s     9m  Stuck in my Own Prison
        Thoran              11m 48s    11m
        gelatin          7h 46m 19s     7h  wibble
        Sumta               15h 58m     8m
        Thylonicus       3d  3h 51m     1m  I FINK YOU FREEKY AND I LIKE YOU A LOT
        Glass            2y  5w  3d    27w
        Raevnos          3y 29w  4d     1w  Grumpy Bear
        There are 16 players connected.
        """;

    /// <summary>eldertaleonline.com:7705, captured 2026-07-30. An empty game, stated in words.</summary>
    private const string EldertaleResponse = """
        Player Name          On For   Idle  Doing
        There are no players connected.
        """;

    [Test]
    public async Task TheServersOwnSummaryIsPreferredToCountingRows()
    {
        // The listing shows 8 rows; the server says 16. It is right and we are not — a WHO can be
        // paginated, filtered, or truncated by the client's screen width. Trust the statement the
        // server made deliberately over the one we inferred.
        var reading = Parser.Parse(MushResponse);

        await Assert.That(reading.HasCount).IsTrue();
        await Assert.That(reading.Count).IsEqualTo(16);
    }

    [Test]
    public async Task NoPlayersConnectedIsAMeasuredZeroAndNotAFailureToParse()
    {
        // The case that would have been missed. A number-only pattern reads this as unparseable and
        // stores "we could not tell", discarding a real measured zero — which then renders as a gap
        // in the heatmap instead of a filled cell, i.e. a live game shown as unreachable.
        var reading = Parser.Parse(EldertaleResponse);

        await Assert.That(reading.HasCount).IsTrue();
        await Assert.That(reading.Count).IsEqualTo(0);
        await Assert.That(reading.Confidence).IsNotEqualTo(WhoConfidence.Unknown);
    }

    [Test]
    public async Task ARenamedDoingHeaderIsStillARecognisableHeader()
    {
        // "ThereIsNoSpoonButIWantYogurt" is a real DOING header. Structural parsing keys on the
        // stable columns — Player Name, On For, Idle — not on the one the operator can rewrite.
        var withoutFooter = string.Join("\n", MushResponse.Split('\n')[..^1]);

        var reading = Parser.Parse(withoutFooter);

        await Assert.That(reading.HasCount).IsTrue();
        await Assert.That(reading.Count).IsEqualTo(7);
    }

    [Test]
    [Arguments("16 Players logged in, 41 record, no maximum.", 16)]
    [Arguments("There are 3 players connected.", 3)]
    [Arguments("Players: 5", 5)]
    [Arguments("1 player logged in.", 1)]
    [Arguments("There are no players connected.", 0)]
    [Arguments("No players are connected.", 0)]
    public async Task TheSummaryIsReadInTheShapesRealServersPrintIt(string footer, int expected)
    {
        var reading = Parser.Parse($"Player Name  On For  Idle  Doing\n{footer}");

        await Assert.That(reading.Count).IsEqualTo(expected);
    }

    [Test]
    public async Task AnUnreadableResponseYieldsUnknownAndNeverZero()
    {
        // The single most important negative in this file. A fabricated zero is indistinguishable
        // from an empty game, so a parser that guesses renders healthy servers as dead.
        var reading = Parser.Parse("Huh?  Type \"help\" for help.");

        await Assert.That(reading.Confidence).IsEqualTo(WhoConfidence.Unknown);
        await Assert.That(reading.HasCount).IsFalse();
        await Assert.That(reading.Count).IsNull();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   \r\n  \r\n")]
    public async Task NothingAtAllIsUnreadRatherThanEmpty(string? response)
    {
        var reading = Parser.Parse(response);

        await Assert.That(reading.Confidence).IsEqualTo(WhoConfidence.Unknown);
        await Assert.That(reading.Count).IsNull();
    }

    [Test]
    public async Task AnsiColourIsStrippedBeforeParsing()
    {
        // Eldertale's connect screen is dense SGR; a coloured WHO is entirely ordinary.
        var coloured = "[1;37;44mPlayer Name  On For  Idle  Doing[0m\n"
            + "[32mThere are 4 players connected.[0m";

        var reading = Parser.Parse(coloured);

        await Assert.That(reading.Count).IsEqualTo(4);
    }

    [Test]
    public async Task CountingRowsReachesPerPlayerConfidenceAndASummaryDoesNot()
    {
        // A summary gives a number and nothing else. Rows give positions, which is what §11's
        // anonymised aggregates need — and it is the only route to them.
        var byRows = Parser.Parse(string.Join("\n", MushResponse.Split('\n')[..^1]));
        var bySummary = Parser.Parse(MushResponse);

        await Assert.That(byRows.Confidence).IsEqualTo(WhoConfidence.PerPlayer);
        await Assert.That(byRows.IdentifiablePlayers).IsEqualTo(7);
        await Assert.That(bySummary.Confidence).IsEqualTo(WhoConfidence.Count);
        await Assert.That(bySummary.IdentifiablePlayers).IsNull();
    }
}
