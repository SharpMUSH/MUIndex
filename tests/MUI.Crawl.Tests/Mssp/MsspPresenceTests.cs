using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// Reading a player count out of an MSSP report, and knowing how well it was read.
/// </summary>
/// <remarks>
/// Every fixture here is a transcript of a real server, taken with <c>mui-probe</c> on 2026-08-28
/// and named with the address it came from. That matters more than usual: MSSP defines only
/// <c>PLAYERS</c>, so every other variable read here is a convention a codebase invented, and a
/// fixture invented to match the parser would prove nothing about any of them.
/// </remarks>
public class MsspPresenceTests
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

    [Test]
    public async Task AStatedCountIsReadExactly()
    {
        // tdome.nukefire.org:4000
        var read = MsspPresence.Read(Report(("PLAYERS", ["70"])));

        await Assert.That(read.Count).IsEqualTo(70);
        await Assert.That(read.Kind).IsEqualTo(MsspCountKind.Stated);
        await Assert.That(read.IsExact).IsTrue();
        await Assert.That(read.Variable).IsEqualTo("PLAYERS");
    }

    [Test]
    public async Task AStatedZeroIsACountAndNotAnAbsence()
    {
        // §5.4's whole point: we read the report and nobody was there.
        var read = MsspPresence.Read(Report(("PLAYERS", ["0"])));

        await Assert.That(read.Found).IsTrue();
        await Assert.That(read.Count).IsEqualTo(0);
    }

    /// <summary>
    /// <c>-1</c> is the Dragonfire/Void family's spelling of "I do not know", not a count.
    /// </summary>
    /// <remarks>
    /// <c>dragonfiremud.com:1999</c> sends <c>OBJECTS = -1</c>, <c>SKILLS = -1</c>,
    /// <c>RACES = -1</c> and <c>INTERMUD = -1</c> in the same report as a real <c>PLAYERS</c>, which
    /// is what shows the value is a sentinel rather than a broken number. Publishing it would be
    /// inventing a count (rule 4).
    /// </remarks>
    [Test]
    [Arguments("-1")]
    [Arguments("N/A")]
    [Arguments("unknown")]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("70 players")]
    public async Task AStatedCountThatIsNotANumberIsNotACount(string declared)
    {
        var read = MsspPresence.Read(Report(("PLAYERS", [declared])));

        await Assert.That(read.Found).IsFalse();
        await Assert.That(read.Kind).IsEqualTo(MsspCountKind.None);
    }

    /// <summary>A roster sent as one variable repeated per player — the Dead Souls family.</summary>
    /// <remarks><c>dead-souls.net:8000</c>, which sent this beside <c>PLAYERS = 3</c>.</remarks>
    [Test]
    public async Task ARepeatedVariableIsCountedOncePerOccurrence()
    {
        var read = MsspPresence.Roster(Report(("WHO", ["Ninja", "Cratylus", "Joshua"])));

        await Assert.That(read.Count).IsEqualTo(3);
        await Assert.That(read.Kind).IsEqualTo(MsspCountKind.Roster);
        await Assert.That(read.IsExact).IsFalse();
        await Assert.That(read.Variable).IsEqualTo("WHO");
    }

    /// <summary>
    /// An empty roster is a measured zero, not a missing reading — and not one player either.
    /// </summary>
    /// <remarks>
    /// Both <c>89.190.140.116:1414</c> (Vithasnir) and <c>100.49.67.225:7777</c> (Xanth Mud) were
    /// observed sending <c>WHO</c> with no value beside <c>PLAYERS = 0</c>. Counting the empty
    /// occurrence as a name would report one player on every quiet Dead Souls game in the catalogue.
    /// </remarks>
    [Test]
    public async Task AnEmptyRosterIsZeroPlayersRatherThanOne()
    {
        var read = MsspPresence.Roster(Report(("WHO", [""])));

        await Assert.That(read.Found).IsTrue();
        await Assert.That(read.Count).IsEqualTo(0);
        await Assert.That(read.Kind).IsEqualTo(MsspCountKind.Roster);
    }

    /// <summary>A roster sent as one comma-separated value — Circle/Nukefire.</summary>
    [Test]
    public async Task ADelimitedListIsCountedByItsEntries()
    {
        // tdome.nukefire.org:4000, abridged. The real report carries sixty-nine.
        var read = MsspPresence.Roster(Report((
            "PLAYERNAMES", ["Dobilina, Blake, Vit, Xyla, Darqjr, Minimoog, Kelsier"])));

        await Assert.That(read.Count).IsEqualTo(7);
        await Assert.That(read.Variable).IsEqualTo("PLAYERNAMES");
    }

    /// <summary>A roster whose entries carry a role — Rise of Praxis, LPMud.</summary>
    [Test]
    public async Task ARoleAnnotationIsNotPartOfTheName()
    {
        // telnet.riseofpraxis.net:6666 sent exactly this beside PLAYERS = 1.
        var read = MsspPresence.Roster(Report(("PLAYER INFO", ["Krem:arch"])));

        await Assert.That(read.Count).IsEqualTo(1);
        await Assert.That(read.Variable).IsEqualTo("PLAYER INFO");
    }

    [Test]
    public async Task OnePlayerNamedTwiceIsOnePlayer()
    {
        // A MU* cannot have two players by one name, so a repeat is the report saying the same
        // thing twice — including the case where a role annotation makes the entries differ.
        var read = MsspPresence.Roster(Report(("PLAYER INFO", ["Krem:arch, krem:builder, Ayla"])));

        await Assert.That(read.Count).IsEqualTo(2);
    }

    /// <summary>
    /// A stated count wins, and is not second-guessed against the roster beside it.
    /// </summary>
    /// <remarks>
    /// The measurement behind this: <c>tdome.nukefire.org:4000</c> states <c>PLAYERS = 70</c> and
    /// names sixty-nine, identically across three probes minutes apart. A roster leaves out whoever
    /// the game does not show, so the two disagreeing is the ordinary case and not a fault — hatching
    /// the cell over it would punish the one kind of server that answered twice.
    /// </remarks>
    [Test]
    public async Task AStatedCountOutranksARosterAndIsNotCheckedAgainstIt()
    {
        var report = Report(
            ("PLAYERS", ["70"]),
            ("PLAYERNAMES", ["Dobilina, Blake, Vit"]));

        var read = MsspPresence.Read(report);

        await Assert.That(read.Count).IsEqualTo(70);
        await Assert.That(read.Kind).IsEqualTo(MsspCountKind.Stated);

        // Both remain separately readable, which is what lets a surface show the disagreement rather
        // than hide it.
        await Assert.That(MsspPresence.Roster(report).Count).IsEqualTo(3);
    }

    [Test]
    public async Task ARosterAnswersWhereTheStatedCountIsASentinel()
    {
        var read = MsspPresence.Read(Report(
            ("PLAYERS", ["-1"]),
            ("WHO", ["Ninja", "Cratylus"])));

        await Assert.That(read.Count).IsEqualTo(2);
        await Assert.That(read.Kind).IsEqualTo(MsspCountKind.Roster);
    }

    [Test]
    public async Task AReportThatAnswersNeitherWayIsNotACount()
    {
        await Assert.That(MsspPresence.Read(Report(("NAME", ["NukeFire"]))).Found).IsFalse();
        await Assert.That(MsspPresence.Read(null).Found).IsFalse();
        await Assert.That(MsspPresence.Roster(null).Found).IsFalse();
    }

    /// <summary>
    /// A list of things that are not people is never read as a roster.
    /// </summary>
    /// <remarks>
    /// The reason rosters are read by variable name and never by the shape of the value:
    /// <c>tdome.nukefire.org:4000</c> sends <c>CLASSES - BASE 1 = Barbarian, Assassin, Slinger,
    /// Curist, Samurai, Infiltrator</c>, which is indistinguishable from a roster and is six classes.
    /// </remarks>
    [Test]
    public async Task AListOfClassesIsNotAListOfPlayers()
    {
        var read = MsspPresence.Read(Report(
            ("CLASSES - BASE 1", ["Barbarian, Assassin, Slinger, Curist, Samurai, Infiltrator"]),
            ("RACES", ["72"]),
            ("AREAS", ["408"])));

        await Assert.That(read.Found).IsFalse();
    }

    [Test]
    public async Task TheVariableThatAnsweredIsNamed()
    {
        // A surface saying "MSSP" for all of these would be hiding which one it read.
        foreach (var variable in MsspPresence.RosterVariables)
        {
            var read = MsspPresence.Roster(Report((variable, ["Ayla"])));

            await Assert.That(read.Variable).IsEqualTo(variable);
        }
    }
}
