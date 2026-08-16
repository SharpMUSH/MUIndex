using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// A server that states its population and its licence in one sentence.
/// </summary>
/// <remarks>
/// <b>Read backwards, this is worse than unreadable.</b> An unreadable line is a hatched cell that
/// says so; a line read backwards arrives looking like a measurement and sits in the catalogue as
/// one. <c>retromud.org:3000</c> was recorded as having 200 players for as long as it has been
/// listed, on the strength of "There are currently 11 out of 200 users playing." — 200 is what the
/// game is licensed for.
/// </remarks>
public class WhoParserCeilingTests
{
    /// <summary>The exact line, taken off the wire with <c>mui-probe</c> rather than reconstructed.</summary>
    [Test]
    public async Task TheRetromudLineReadsThePopulationRatherThanTheLicence()
    {
        var reading = new WhoParser().Parse("""
            No such player. Check your typing and try again.
                (W)ho is currently in the game                   (Q)uit
                (C)reate a new character                         (G)ame status
                (E)vents running
                            There are currently 11 out of 200 users playing.
            """);

        await Assert.That(reading.HasCount).IsTrue();
        await Assert.That(reading.Count).IsEqualTo(11);
    }

    [Test]
    [Arguments("There are currently 11 out of 200 users playing.", 11)]
    [Arguments("12 out of 150 players connected", 12)]
    [Arguments("There are 5 of a maximum 40 players online.", 5)]
    [Arguments("There are 7 out of a maximum of 64 users logged in.", 7)]
    [Arguments("0 out of 300 players online", 0)]
    public async Task ThePopulationIsTheFirstNumber(string line, int expected)
    {
        var reading = new WhoParser().Parse(line);

        await Assert.That(reading.HasCount).IsTrue();
        await Assert.That(reading.Count).IsEqualTo(expected);
    }

    /// <summary>The same shape in words, where the ceiling is named explicitly.</summary>
    [Test]
    [Arguments("three out of 40 players are online", 3)]
    [Arguments("There are two out of twenty people connected.", 2)]
    public async Task TheWordedFormIsReadTheSameWayRound(string line, int expected)
    {
        var reading = new WhoParser().Parse(line);

        await Assert.That(reading.Count).IsEqualTo(expected);
    }

    /// <summary>
    /// <b>A bare "of" is left alone, and this is the judgement the whole change turns on.</b>
    /// "one of three players are active" means three are connected and one of them is doing
    /// something — the opposite of retromud's sentence, in identical grammar. Only an explicit
    /// marker separates them, so only an explicit marker is honoured.
    /// </summary>
    [Test]
    public async Task ABareOfStillMeansThePopulationIsTheSecondNumber()
    {
        // The one observed sentence of this shape, and nothing invented beside it: the numbered
        // variants interact with three other patterns, and a made-up string proves only what its
        // author already believed.
        var reading = new WhoParser().Parse("one of three players are active.");

        await Assert.That(reading.Count).IsEqualTo(3);
    }

    /// <summary>
    /// A plain count is untouched: there is no ceiling in the sentence, so there is nothing to
    /// prefer over it.
    /// </summary>
    [Test]
    [Arguments("There are 16 players connected.", 16)]
    [Arguments("There are seven people connected.", 7)]
    [Arguments("There are currently 39 connected players.", 39)]
    [Arguments("There are no players connected.", 0)]
    public async Task ASentenceWithNoCeilingIsUnaffected(string line, int expected)
    {
        var reading = new WhoParser().Parse(line);

        await Assert.That(reading.HasCount).IsTrue();
        await Assert.That(reading.Count).IsEqualTo(expected);
    }

    /// <summary>
    /// <c>of</c> is a common word, so the pattern must not fire on a sentence that merely contains
    /// it. Both of these state one number and it is the one to read.
    /// </summary>
    [Test]
    [Arguments("3 players of the realm are connected", 3)]
    [Arguments("There are 8 players of note online.", 8)]
    public async Task AnOfThatIsNotACeilingDoesNotMoveTheReading(string line, int expected)
    {
        var reading = new WhoParser().Parse(line);

        await Assert.That(reading.Count).IsEqualTo(expected);
    }
}
