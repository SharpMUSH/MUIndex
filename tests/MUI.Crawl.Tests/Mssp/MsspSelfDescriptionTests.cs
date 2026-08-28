using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// When a report leaves <c>INFO</c> and <c>VERSION</c> with nothing left to ask.
/// </summary>
/// <remarks>
/// Each condition stands in for a consumer, so the tests are written the same way: take a report
/// that answers everything, remove one thing, and the probe has to go back to asking.
/// </remarks>
public class MsspSelfDescriptionTests
{
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Report(
        params (string Variable, string[] Values)[] variables)
    {
        var report = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (variable, values) in variables)
        {
            report[variable] = values;
        }

        return report;
    }

    /// <summary>What <c>playdecay.com:3003</c> actually publishes, abridged to what is read here.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> MoralDecay() => Report(
        ("NAME", ["Moral Decay"]),

        // Repeated on the wire, and the last value is the one every reader takes.
        ("CODEBASE", ["FluffOS v2025", "Moral Decay v9.0"]),
        ("FAMILY", ["LPmud"]),
        ("PLAYERS", ["4"]));

    [Test]
    public async Task TheGameThatRaisedThisHasAnsweredEverythingWeWouldAsk()
    {
        await Assert.That(MsspSelfDescription.AnswersTheLoginCommands(MoralDecay())).IsTrue();
    }

    [Test]
    public async Task AGameWithNoReportAtAllIsStillAsked()
    {
        // game.convergencemush.org:10000 — RhostMUSH, no MSSP, and its INFO reply is the only thing
        // that names it. The reader exists for this game; it must keep being asked.
        await Assert.That(MsspSelfDescription.AnswersTheLoginCommands(null)).IsFalse();
        await Assert.That(MsspSelfDescription.AnswersTheLoginCommands(MsspReport.Empty)).IsFalse();
    }

    [Test]
    public async Task AReportThatNamesNoEngineIsStillAsked()
    {
        // VERSION and INFO are how a codebase gets read where MSSP carries none.
        var report = Report(("NAME", ["Moral Decay"]), ("PLAYERS", ["4"]));

        await Assert.That(MsspSelfDescription.AnswersTheLoginCommands(report)).IsFalse();
    }

    [Test]
    [Arguments("Unknown")]
    [Arguments("N/A")]
    [Arguments("")]
    public async Task AnEngineLeftAsTemplateTextIsNoAnswer(string codebase)
    {
        var report = Report(("NAME", ["Moral Decay"]), ("CODEBASE", [codebase]), ("PLAYERS", ["4"]));

        await Assert.That(MsspSelfDescription.AnswersTheLoginCommands(report)).IsFalse();
    }

    [Test]
    public async Task AnEngineNameIsAnAnswerEvenThoughItWouldNotBeAName()
    {
        // IsTemplate rather than IsPlaceholder: "FluffOS" is exactly what CODEBASE is for, while the
        // same string arriving as NAME means nobody filled the name in.
        var report = Report(("NAME", ["Moral Decay"]), ("CODEBASE", ["FluffOS"]), ("PLAYERS", ["4"]));

        await Assert.That(MsspSelfDescription.AnswersTheLoginCommands(report)).IsTrue();
    }

    [Test]
    [Arguments("PennMUSH")]
    [Arguments("Your MUD Name")]
    [Arguments("Unnamed")]
    public async Task AnUneditedInstallHasNotNamedItselfAndIsStillAsked(string name)
    {
        // §7.3's reason for MsspDefaults, reaching here: an unedited mush.cnf has identified its
        // codebase and not itself, so INFO may still be the only thing that says what this game is.
        var report = Report(("NAME", [name]), ("CODEBASE", ["PennMUSH 1.8.8p0"]), ("PLAYERS", ["4"]));

        await Assert.That(MsspSelfDescription.AnswersTheLoginCommands(report)).IsFalse();
    }

    [Test]
    public async Task ANameThatMerelyRestatesTheEngineIsStillAsked()
    {
        var report = Report(
            ("NAME", ["FluffOS"]), ("CODEBASE", ["FluffOS v2025"]), ("PLAYERS", ["4"]));

        await Assert.That(MsspSelfDescription.AnswersTheLoginCommands(report)).IsFalse();
    }

    [Test]
    public async Task AReportWithNoCountIsStillAsked()
    {
        // INFO's `Connected:` line is §5.2's rung below MSSP, so a report that named itself and its
        // engine but stated no count still has something left to be asked for.
        var report = Report(("NAME", ["Moral Decay"]), ("CODEBASE", ["FluffOS v2025"]));

        await Assert.That(MsspSelfDescription.AnswersTheLoginCommands(report)).IsFalse();
    }

    [Test]
    [Arguments("-1")]
    [Arguments("N/A")]
    public async Task ACountThatIsASentinelIsNoCount(string players)
    {
        var report = Report(
            ("NAME", ["Moral Decay"]), ("CODEBASE", ["FluffOS v2025"]), ("PLAYERS", [players]));

        await Assert.That(MsspSelfDescription.AnswersTheLoginCommands(report)).IsFalse();
    }

    [Test]
    public async Task ARosterCountsAsTheCount()
    {
        // Whatever answered the question answered it — the rung it lands on is PresenceChoice's
        // business, not this one's.
        var report = Report(
            ("NAME", ["Mortal Realms"]),
            ("CODEBASE", ["FluffOS v2025"]),
            ("WHO", ["Ninja", "Cratylus", "Joshua"]));

        await Assert.That(MsspSelfDescription.AnswersTheLoginCommands(report)).IsTrue();
    }

    /// <summary>
    /// A mudlib counts as an engine once the reader has been taught it.
    /// </summary>
    /// <remarks>
    /// <c>Dead Souls</c> is the widest of the roster conventions — nine games in the catalogue, all
    /// answering <c>Dead Souls 3.9</c> / <c>3.8.2</c> / <c>3.7a7</c> — and it was added to
    /// <c>LoginCommandReading</c>'s family list rather than special-cased here, because that list is
    /// the one vocabulary this project has for "an engine we recognise". The gate reads it; it does
    /// not keep its own.
    /// </remarks>
    [Test]
    public async Task AMudlibTheReaderKnowsCountsAsTheEngine()
    {
        var report = Report(
            ("NAME", ["Dead Souls Dev"]),
            ("CODEBASE", ["Dead Souls 3.9"]),
            ("WHO", ["Ninja", "Cratylus", "Joshua"]));

        await Assert.That(MsspSelfDescription.AnswersTheLoginCommands(report)).IsTrue();
    }

    [Test]
    public async Task AMudlibOutsideTheFamilyListLeavesTheQuestionsWorthAsking()
    {
        // The condition still bites for an engine nobody has taught the reader.
        var report = Report(
            ("NAME", ["Some Game"]),
            ("CODEBASE", ["Galaxy Engine 2.2"]),
            ("PLAYERS", ["3"]));

        await Assert.That(MsspSelfDescription.AnswersTheLoginCommands(report)).IsFalse();
    }

    /// <summary>
    /// A custom build string that names no engine leaves <c>INFO</c> something to say.
    /// </summary>
    /// <remarks>
    /// Two real games, and the only two the whole catalogue turned up:
    /// <c>northern-crossroads-ncmud</c> declares <c>NC-7.0.357.7940b961</c> and its <c>INFO</c> is
    /// the only thing that says <c>DikuMUD</c>; <c>primal-darkness-ii</c> declares
    /// <c>PD/NM III</c> and its <c>INFO</c> is the only thing that says <c>FluffOS</c>.
    /// </remarks>
    [Test]
    [Arguments("NC-7.0.357.7940b961")]
    [Arguments("PD/NM III")]
    [Arguments("Rapture")]
    [Arguments("Alter Aeon v2.25")]
    public async Task AReportWhoseEngineWeDoNotRecogniseIsStillAsked(string codebase)
    {
        var report = Report(
            ("NAME", ["Primal Darkness-II"]), ("CODEBASE", [codebase]), ("PLAYERS", ["3"]));

        await Assert.That(MsspSelfDescription.AnswersTheLoginCommands(report)).IsFalse();
    }

    /// <summary>
    /// A coarser <c>FAMILY</c> does not stand in for the engine.
    /// </summary>
    /// <remarks>
    /// Both protected games declare one — <c>DikuMUD</c> and <c>LPMud</c> — so accepting it would
    /// silence exactly the two the engine condition exists for.
    /// </remarks>
    [Test]
    public async Task AFamilyDoesNotStandInForTheEngine()
    {
        var report = Report(
            ("NAME", ["Primal Darkness-II"]),
            ("CODEBASE", ["PD/NM III"]),
            ("FAMILY", ["LPMud"]),
            ("PLAYERS", ["3"]));

        await Assert.That(MsspSelfDescription.AnswersTheLoginCommands(report)).IsFalse();
    }

    /// <summary>
    /// The engine may be named in any occurrence of a repeated <c>CODEBASE</c>, not just the last.
    /// </summary>
    /// <remarks>
    /// <c>playdecay.com:3003</c> sends <c>CODEBASE</c> twice — <c>FluffOS v2025</c>, then
    /// <c>Moral Decay v9.0</c>. Reading only the latest word, which is right everywhere else, would
    /// find no engine and go on typing at the login screen of the game that asked us to stop.
    /// </remarks>
    [Test]
    public async Task TheEngineCountsWhereverInTheRepeatItIsNamed()
    {
        await Assert.That(MsspSelfDescription.AnswersTheLoginCommands(MoralDecay())).IsTrue();

        var lastOnly = Report(
            ("NAME", ["Moral Decay"]), ("CODEBASE", ["Moral Decay v9.0"]), ("PLAYERS", ["4"]));

        await Assert.That(MsspSelfDescription.AnswersTheLoginCommands(lastOnly)).IsFalse();
    }

    [Test]
    public async Task TheLastValueOfARepeatedVariableIsTheOneRead()
    {
        // One reader for "which repeat wins", shared with MsspPresence and ProbeResult.MsspField:
        // Moral Decay repeats CODEBASE, and a reader picking the first would see a different engine
        // from the one the publisher stores.
        await Assert.That(MsspReport.Last(MoralDecay(), "CODEBASE")).IsEqualTo("Moral Decay v9.0");
    }
}
