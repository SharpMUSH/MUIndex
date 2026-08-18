using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// The structural WHO parser, tested against responses captured from live servers.
/// </summary>
/// <remarks>
/// The first two fixtures are real captures — an invented one would have a tidy header and footer
/// and miss the cases that actually matter.
/// </remarks>
public class WhoParserTests
{
    private static readonly WhoParser Parser = new();

    /// <summary>
    /// Real capture. The last column header was renamed by the operator in softcode.
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

    /// <summary>Real capture: an empty game, stated in words.</summary>
    private const string EldertaleResponse = """
        Player Name          On For   Idle  Doing
        There are no players connected.
        """;

    [Test]
    public async Task TheServersOwnSummaryIsPreferredToCountingRows()
    {
        // The listing shows 8 rows; the server says 16. A WHO can be paginated or truncated by
        // screen width, so trust the server's own statement over a row count we inferred.
        var reading = Parser.Parse(MushResponse);

        await Assert.That(reading.HasCount).IsTrue();
        await Assert.That(reading.Count).IsEqualTo(16);
    }

    [Test]
    public async Task NoPlayersConnectedIsAMeasuredZeroAndNotAFailureToParse()
    {
        // A number-only pattern would read this as unparseable, discarding a real measured zero —
        // which renders as a hatched cell instead of a filled one (a live game shown as unmeasured).
        var reading = Parser.Parse(EldertaleResponse);

        await Assert.That(reading.HasCount).IsTrue();
        await Assert.That(reading.Count).IsEqualTo(0);
        await Assert.That(reading.Confidence).IsNotEqualTo(WhoConfidence.Unknown);
    }

    [Test]
    public async Task ARenamedDoingHeaderIsStillARecognisableHeader()
    {
        // Structural parsing keys on the stable columns — Player Name, On For, Idle — not the
        // renameable DOING header.
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

    /// <summary>Real capture: WHO was eaten as a character name.</summary>
    private const string AlterAeonResponse = """
        No character by that name found.
        (To log on an existing character, enter the name now.)
        Would you like to create a new character?
        """;

    [Test]
    public async Task ALoginPromptIsNeverReadAsAnAnswer()
    {
        // The documented false-zero bug: an earlier pattern matched "no characters?" and reported
        // ZERO PLAYERS for a game with hundreds online. A fabricated measurement is worse than
        // admitting we cannot tell.
        var reading = Parser.Parse(AlterAeonResponse);

        // LoginPrompt rather than Unknown: Alter Aeon has no pre-login WHO, so it isn't a dialect
        // gap the parser needs to close.
        await Assert.That(reading.Confidence).IsEqualTo(WhoConfidence.LoginPrompt);
        await Assert.That(reading.Attempted).IsTrue();
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
        // Suppression is whole-response rather than per-line: if any part is a login prompt, the
        // server did not answer the question at all.
        var reading = Parser.Parse($"Player Name  On For  Idle  Doing\n{line}");

        await Assert.That(reading.Confidence).IsEqualTo(WhoConfidence.LoginPrompt);
        await Assert.That(reading.HasCount).IsFalse();
    }

    [Test]
    public async Task ACountOnlyCountsWhenTheSentenceIsAboutBeingConnected()
    {
        // "3 characters deleted" is not a player count — the connectivity qualifier is what keeps an
        // unrelated number from becoming a measurement. No column header on purpose, so the
        // row-counting fallback can't mask what's being tested: that the summary pattern doesn't fire.
        var reading = Parser.Parse("3 characters were deleted last night.");

        await Assert.That(reading.HasCount).IsFalse();
        await Assert.That(reading.Confidence).IsEqualTo(WhoConfidence.Unknown);
    }

    [Test]
    public async Task AnUnreadableResponseYieldsUnknownAndNeverZero()
    {
        // A fabricated zero is indistinguishable from an empty game — a guessing parser renders
        // healthy servers as dead.
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
        // A summary gives a number and nothing else; rows give positions, which is what §11's
        // anonymised aggregates need.
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
    // them is doing something. Connected is what a presence sample measures.
    [Arguments("one of three players are active.", 3)]
    [Arguments("Two users are online.", 2)]
    [Arguments("Twenty players logged in.", 20)]
    public async Task ACountSpelledAsAWordIsStillACount(string footer, int expected)
    {
        // Bounded to twenty: past that nobody spells it out, and an open-ended word-number parser
        // is a liability rather than a feature.
        var reading = Parser.Parse(footer);

        await Assert.That(reading.Count).IsEqualTo(expected);
    }

    [Test]
    public async Task PeopleAndFolksCountAsPlayers()
    {
        await Assert.That(Parser.Parse("There are 4 people connected.").Count).IsEqualTo(4);
        await Assert.That(Parser.Parse("No folks are online.").Count).IsEqualTo(0);
    }

    [Test]
    public async Task AWordThatIsNotACountIsStillRefused()
    {
        // Fixed vocabulary, not a general parser: "several" and "many" are not numbers.
        await Assert.That(Parser.Parse("There are several people connected.").HasCount).IsFalse();
        await Assert.That(Parser.Parse("Many players are online.").HasCount).IsFalse();
    }

    [Test]
    [Arguments("There are currently 39 connected players.", 39)]
    [Arguments("There are 4 online players.", 4)]
    public async Task TheConnectivityWordMayStandBetweenTheNumberAndTheNoun(string footer, int expected)
    {
        // Real payload: the pattern wanted the noun straight after the number, so an adjective in
        // between — and the adjective here *is* the connectivity word — read as no count at all.
        var reading = Parser.Parse(footer);

        await Assert.That(reading.Count).IsEqualTo(expected);
    }

    [Test]
    public async Task ANounFollowedByABareOnIsACount()
    {
        // "characters" is already a People noun; the sentence never reaches a connectivity word
        // because it says "on" and stops.
        var reading = Parser.Parse("There are 11 characters on, of which are visible to you.");

        await Assert.That(reading.Count).IsEqualTo(11);
    }

    [Test]
    [Arguments("No character by that name found.")]
    [Arguments("There are 3 messages on the board.")]
    [Arguments("There are 2 exits on this room's south wall.")]
    public async Task ABareOnDoesNotTurnAnUnrelatedSentenceIntoACount(string line)
    {
        // Bare "on" is the loosest word this parser admits — it counts only immediately after a
        // noun that means people, never after a number in an unrelated sentence.
        var reading = Parser.Parse(line);

        await Assert.That(reading.HasCount).IsFalse();
        await Assert.That(reading.Count).IsNull();
    }

    /// <summary>
    /// A real payload, redacted to shapes. Fifteen names, no number anywhere in the reply.
    /// </summary>
    private const string NameListResponse =
        "Connected players: Aaaaa, Aaaa, Aaa'aaa, Aaaaaaa, Aaa, Aaaaaa, Aaaaaaaa, Aaaa, Aaaaa, "
        + "Aaa, Aaaaaaa, Aaaaa, Aaaa, Aaaaaa and Aaaaaa";

    [Test]
    public async Task AListOfWhoIsOnIsCountedWhenTheHeaderSaysWhatTheListIs()
    {
        // The header is what makes the list countable — People beside a connectivity word — without
        // it the same commas could be anything.
        var reading = Parser.Parse(NameListResponse);

        await Assert.That(reading.Count).IsEqualTo(15);
    }

    [Test]
    public async Task TheSameListReadsUnderADifferentHeader()
    {
        var reading = Parser.Parse("The following people are logged on: Aaaa, Aaa, Aaaaaaa, Aaaa, Aaaaa.");

        await Assert.That(reading.Count).IsEqualTo(5);
    }

    [Test]
    public async Task AWrappedListIsFollowedOnlyWhileItIsPlainlyUnfinished()
    {
        // Servers wrap. A line ending in a comma has not finished its list; one that does not, has,
        // and the next line is something else entirely.
        var wrapped = "Connected players: Aaaa, Aaa, Aaaaaaa,\nAaaa, Aaaaa\nType WHO for more.";

        await Assert.That(Parser.Parse(wrapped).Count).IsEqualTo(5);
    }

    [Test]
    public async Task AListOfSomethingOtherThanNamesIsRefused()
    {
        // Every item has to look like a name, or the whole list is something else wearing a
        // recognised header.
        var reading = Parser.Parse("Connected players: see the web site, or ask a wizard for help");

        await Assert.That(reading.HasCount).IsFalse();
    }

    [Test]
    public async Task ANoLoggedPlayersLineIsAMeasuredZero()
    {
        // Same argument as "There are no players connected.": reading this as unparseable would
        // throw away a genuine measured zero.
        var reading = Parser.Parse("There are no logged players.");

        await Assert.That(reading.HasCount).IsTrue();
        await Assert.That(reading.Count).IsEqualTo(0);
    }

    [Test]
    public async Task LoggedOutIsNotAConnectivityWord()
    {
        // "logged out" is the opposite claim and must not become a count of who is on.
        await Assert.That(Parser.Parse("There are 6 players logged out today.").HasCount).IsFalse();
        await Assert.That(Parser.Parse("There are no players logged off since noon.").HasCount).IsFalse();
    }

    [Test]
    [Arguments("That name is reserved for a senior member of the mud.")]
    [Arguments("There is no player by that name. Please enter your account name, or \"new\" to create a new account:")]
    [Arguments("The MUD Administrator has found the name to be unacceptable. Name:")]
    [Arguments("Password:")]
    public async Task TheLoginPromptStillNeverProducesACount(string line)
    {
        // The game ate the word WHO as a name — a count read out of a login prompt is exactly the
        // fabrication this parser must never commit.
        var reading = Parser.Parse(line);

        await Assert.That(reading.HasCount).IsFalse();
        await Assert.That(reading.Count).IsNull();
        await Assert.That(reading.Confidence).IsEqualTo(WhoConfidence.LoginPrompt);
    }

    [Test]
    public async Task AFuzzyCountIsLeftUnmeasurable()
    {
        // No number in the sentence — inventing one from "loads" would be fabrication.
        var reading = Parser.Parse("Loads of people are on now.");

        await Assert.That(reading.HasCount).IsFalse();
    }

    /// <summary>
    /// Real capture: a server announcing its population with no connectivity word any other shape admits.
    /// </summary>
    [Test]
    public async Task AnAnnouncedFigureIsACountEvenWithNoConnectivityWord()
    {
        var reading = Parser.Parse("""
            TELEHACK STATUS  2026-Aug-17
            There are 122 local users.  There are 26649 hosts on the network.
            """);

        await Assert.That(reading.Count).IsEqualTo(122);
    }

    [Test]
    [Arguments("Your name must be between 6 and 12 characters long.")]
    [Arguments("There are 20 new players registered today.")]
    [Arguments("There are 6 to 12 characters allowed.")]
    public async Task AnAnnouncementThatIsReallyARuleIsNotACount(string line)
    {
        // A sentence that ends at the noun has finished counting; one that carries on into
        // "registered today" is counting something else.
        var reading = Parser.Parse(line);

        await Assert.That(reading.HasCount).IsFalse();
    }

    [Test]
    public async Task TheCeilingStillWinsOverTheAnnouncement()
    {
        // The ceiling pattern must win here — "11 out of 200" should read as 11, the population, not 200.
        await Assert.That(Parser.Parse("There are currently 11 out of 200 users playing.").Count)
            .IsEqualTo(11);
    }

    /// <summary>
    /// A Fuzzball MUCK's table rule, which carries the total — a whole codebase family's footer shape.
    /// </summary>
    [Test]
    public async Task TheFooterAMuckDrawsUnderItsTableIsACount()
    {
        var reading = Parser.Parse("""
            User         Name Idle
            --[Sat Aug 19 03:14:07 2696]--------------------------------[0 users; 0d 00h]--
            """);

        await Assert.That(reading.HasCount).IsTrue();
        await Assert.That(reading.Count).IsEqualTo(0);
    }

    /// <summary>
    /// Real capture: a perfectly ordinary column layout in Dutch.
    /// </summary>
    [Test]
    public async Task AColumnHeaderIsFoundInAGameThatIsNotInEnglish()
    {
        // "Naam" is not "Name" — Online beside Idle is the same header in any language, since those
        // two are borrowed rather than translated.
        var reading = Parser.Parse("""
            R Naam                   S Klasse     Online  Idle    Bezig
            - ---------------------- - --------  ------  ------  --------------------
            W Wizard-Person (#12345) A -         14 uur  02 min  Rondkijken
            P Speler-Twee    (#4242) A -         03 uur  00 min
            """);

        await Assert.That(reading.Count).IsEqualTo(2);
        await Assert.That(reading.Confidence).IsEqualTo(WhoConfidence.PerPlayer);
    }

    [Test]
    public async Task OnlineOnItsOwnIsNotAColumnHeader()
    {
        // Both words are required — "online" alone appears in many connect-screen sentences and
        // would falsely trigger the table-header path.
        var reading = Parser.Parse("Come online and join us today\nsomething\nsomething else");

        await Assert.That(reading.HasCount).IsFalse();
    }
}
