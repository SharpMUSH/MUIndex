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
    /// A header rewritten past every structural rule — no "Name", no "Idle", no "On For".
    /// </summary>
    /// <remarks>
    /// The case §8.5's override exists for, and the one rule 2 calls the worst bug this codebase can
    /// ship: a game running perfectly well, rendered as never measured, because its operator
    /// decorated a column heading.
    /// </remarks>
    private const string InscrutableResponse = """
        ~~~ the court is in session ~~~
        Xperta               9m 21s     9m  Stuck in my Own Prison
        Thoran              11m 48s    11m
        gelatin          7h 46m 19s     7h  wibble
        """;

    [Test]
    public async Task AnInscrutableHeaderIsUnreadableUntilItsOwnerNamesIt()
    {
        // Unreadable is the honest answer without the hint — never a zero, which would render a
        // busy game as dead.
        await Assert.That(Parser.Parse(InscrutableResponse).Confidence).IsEqualTo(WhoConfidence.Unknown);

        var told = new WhoParser("the court is in session").Parse(InscrutableResponse);

        await Assert.That(told.Count).IsEqualTo(3);
        await Assert.That(told.Confidence).IsEqualTo(WhoConfidence.PerPlayer);
    }

    /// <summary>Any part of the line does, and case does not matter.</summary>
    [Test]
    public async Task ThePartOfTheHeaderTheOwnerGaveUsIsEnough()
    {
        var told = new WhoParser("COURT IS IN").Parse(InscrutableResponse);

        await Assert.That(told.Count).IsEqualTo(3);
    }

    /// <summary>
    /// The hint adds a way to find the header and never removes one.
    /// </summary>
    /// <remarks>
    /// An override goes stale the moment its author rewrites their DOING again. When it does, the
    /// game degrades to the reading it had before rather than to silence — which is the difference
    /// between a feature that helps and one that quietly takes a working measurement away.
    /// </remarks>
    [Test]
    public async Task AStaleOverrideDoesNotCostAGameTheReadingItAlreadyHad()
    {
        var stale = new WhoParser("a header this game stopped printing years ago");

        await Assert.That(stale.Parse(MushResponse).Count).IsEqualTo(Parser.Parse(MushResponse).Count);
    }

    /// <summary>
    /// An owner may say where to count and may not talk us out of the server's own total.
    /// </summary>
    /// <remarks>
    /// The summary a server prints for itself is the one statement in a WHO response it makes
    /// deliberately, so it is still consulted first. Otherwise this box would be a way to make a
    /// count disagree with the sentence printed above it — an owner editing a measurement by
    /// indirection, which is exactly the line §8.5 draws.
    /// </remarks>
    [Test]
    public async Task TheServersOwnSummaryStillWins()
    {
        const string Response = """
            ~~~ the court is in session ~~~
            Xperta               9m 21s     9m
            Thoran              11m 48s    11m
            There are 47 players connected.
            """;

        var told = new WhoParser("the court is in session").Parse(Response);

        await Assert.That(told.Count).IsEqualTo(47);
    }

    /// <summary>An empty or blank override is no override, not a header matching every line.</summary>
    [Test]
    public async Task AnEmptyOverrideChangesNothing()
    {
        await Assert.That(new WhoParser("   ").Parse(InscrutableResponse).Confidence)
            .IsEqualTo(WhoConfidence.Unknown);
        await Assert.That(new WhoParser(null).Parse(MushResponse).Count)
            .IsEqualTo(Parser.Parse(MushResponse).Count);
    }


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

    /// <summary>alteraeon.com:23, captured 2026-07-30. WHO was eaten as a character name.</summary>
    private const string AlterAeonResponse = """
        No character by that name found.
        (To log on an existing character, enter the name now.)
        Would you like to create a new character?
        """;

    [Test]
    public async Task ALoginPromptIsNeverReadAsAnAnswer()
    {
        // The worst bug this parser can have, caught on a real DIKU. Alter Aeon treats the login
        // prompt as a character-name prompt, so "WHO" returns "No character by that name found."
        // An earlier pattern matched `no characters?` and reported ZERO PLAYERS for a game with
        // hundreds online — a fabricated measurement, which is worse than admitting we cannot tell.
        var reading = Parser.Parse(AlterAeonResponse);

        await Assert.That(reading.Confidence).IsEqualTo(WhoConfidence.Unknown);
        await Assert.That(reading.HasCount).IsFalse();
        await Assert.That(reading.Count).IsNull();
    }

    [Test]
    [Arguments("No character by that name found.")]
    [Arguments("What is your name?")]
    [Arguments("Enter the name of your character:")]
    [Arguments("Password:")]
    [Arguments("Type 'new' to create a character.")]
    public async Task AnyLoginPromptSuppressesTheWholeReading(string line)
    {
        // Suppression is deliberately whole-response rather than per-line: if any part of the reply
        // is a login prompt, the server did not answer the question and nothing in the reply is an
        // answer to it.
        var reading = Parser.Parse($"Player Name  On For  Idle  Doing\n{line}");

        await Assert.That(reading.Confidence).IsEqualTo(WhoConfidence.Unknown);
    }

    [Test]
    public async Task ACountOnlyCountsWhenTheSentenceIsAboutBeingConnected()
    {
        // "3 characters deleted" is not a player count. The connectivity qualifier is what keeps a
        // number in an unrelated sentence from becoming a measurement.
        //
        // Deliberately no column header: with one present the row-counting fallback would count this
        // line as a player row, which is correct for that path and would hide what is being tested
        // here — that the *summary* pattern does not fire on a number in an unrelated sentence.
        var reading = Parser.Parse("3 characters were deleted last night.");

        await Assert.That(reading.HasCount).IsFalse();
        await Assert.That(reading.Confidence).IsEqualTo(WhoConfidence.Unknown);
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
        var coloured = "\u001b[1;37;44mPlayer Name  On For  Idle  Doing\u001b[0m\n"
            + "\u001b[32mThere are 4 players connected.\u001b[0m";

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

    [Test]
    [Arguments("There are seven people connected.", 7)]
    // "one of three players are active" reads as three, not one: three are connected and one of
    // them is doing something. Connected is what a presence sample measures, and the MOO this came
    // from lists three rows above that sentence.
    [Arguments("one of three players are active.", 3)]
    [Arguments("Two users are online.", 2)]
    [Arguments("Twenty players logged in.", 20)]
    public async Task ACountSpelledAsAWordIsStillACount(string footer, int expected)
    {
        // resort.org:2323 spells it out — "There are seven people connected." — and a MOO says
        // "one of three players are active." A digits-only pattern reads both as unparseable and
        // discards a count we could have had. Bounded to twenty: past that nobody spells it out,
        // and an open-ended word-number parser is a liability rather than a feature.
        var reading = Parser.Parse(footer);

        await Assert.That(reading.Count).IsEqualTo(expected);
    }

    [Test]
    public async Task PeopleAndFolksCountAsPlayers()
    {
        // "players" is not the only noun a server reaches for.
        await Assert.That(Parser.Parse("There are 4 people connected.").Count).IsEqualTo(4);
        await Assert.That(Parser.Parse("No folks are online.").Count).IsEqualTo(0);
    }

    [Test]
    public async Task AWordThatIsNotACountIsStillRefused()
    {
        // The word list is a fixed vocabulary, not a general parser. "Several" and "many" are not
        // numbers and must not become one.
        await Assert.That(Parser.Parse("There are several people connected.").HasCount).IsFalse();
        await Assert.That(Parser.Parse("Many players are online.").HasCount).IsFalse();
    }
}
