using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// A figure about the past is not a population.
/// </summary>
/// <remarks>
/// Found by parsing every connect screen the catalogue holds (918 of them, 2026-08-28) as though it
/// were a <c>WHO</c> answer. <c>down.moo.midgard.org:8888</c> prints its live count and then two
/// figures about windows of time, and the last of those — <c>0 players have connected over the past
/// twelve hours.</c> — read as a measured zero for a game with somebody in it. It reached production
/// harmlessly only by luck: <see cref="BannerCount"/> refuses a screen stating two different counts,
/// so the wrong reading and the right one cancelled out. A screen carrying only the historical line
/// would have published it.
/// </remarks>
public class WhoParserPastFigureTests
{
    /// <summary>The three lines <c>down.moo.midgard.org:8888</c> prints, in order.</summary>
    private const string DownMoo = """
        1 players are connected.
        1 players have connected over the past twenty-four hours.
        0 players have connected over the past twelve hours.
        """;

    [Test]
    [Arguments("1 players have connected over the past twenty-four hours.")]
    [Arguments("0 players have connected over the past twelve hours.")]
    [Arguments("3 players have connected in the last hour.")]
    [Arguments("40 characters have logged in since midnight.")]
    public async Task HowManyHaveConnectedIsNotHowManyAre(string line)
    {
        await Assert.That(WhoParser.TryStatedCount(line, out _)).IsFalse();
    }

    [Test]
    [Arguments("1 players are connected.")]
    [Arguments("There are currently 16 users logged on:")]
    [Arguments("There are no players connected.")]
    public async Task ThePresentTenseIsUntouched(string line)
    {
        await Assert.That(WhoParser.TryStatedCount(line, out _)).IsTrue();
    }

    /// <summary>
    /// A live count standing beside a figure about today is still read.
    /// </summary>
    /// <remarks>
    /// Why the window phrase is not the test. <c>port-of-dreams</c> prints this exact sentence, and
    /// forty-two of the 918 screens carry a word like <c>today</c> somewhere; refusing on one would
    /// throw away the count standing next to it.
    /// </remarks>
    [Test]
    public async Task ACountBesideATotalForTheDayIsStillACount()
    {
        var read = WhoParser.TryStatedCount(
            "There are 2 players currently online, and today's total is 6.", out var count);

        await Assert.That(read).IsTrue();
        await Assert.That(count).IsEqualTo(2);
    }

    [Test]
    public async Task AScreenStatingBothReadsTheLiveOne()
    {
        // The whole point, end to end: before this, the screen refused outright because its three
        // lines "disagreed". They never disagreed — two of them were answering a different question.
        await Assert.That(BannerCount.Find(DownMoo)).IsEqualTo(1);
    }

    [Test]
    public async Task ALabelledCountOnTheScreenIsUnaffected()
    {
        // moo.opal.org:7878 — a label rather than a sentence, so it never reaches the tense test at
        // all. Asserted here so a future widening of that test cannot quietly take the label shape
        // with it.
        await Assert.That(BannerCount.Find("Number of connected players: 1")).IsEqualTo(1);
    }

    /// <summary>
    /// The eight lines in the catalogue that are in the perfect tense and state no count.
    /// </summary>
    /// <remarks>
    /// They are what makes the perfect tense safe to refuse on: it is a shape servers use to talk to
    /// the person connecting, not to report a population, so nothing is lost by declining it.
    /// </remarks>
    [Test]
    [Arguments("You have connected to CaveMUCK")]
    [Arguments("You can read the rules by typing 'rules' after you have logged in.")]
    [Arguments("Once you have successfully logged in, consider changing your password.")]
    [Arguments("If you have never played MUME before, type NEW to create a new character,")]
    public async Task TheLinesThisRefusesWereNeverCountsAnyway(string line)
    {
        await Assert.That(WhoParser.TryStatedCount(line, out _)).IsFalse();
    }
}
