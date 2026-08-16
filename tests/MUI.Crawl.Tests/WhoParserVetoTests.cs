using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// What the login-prompt veto may and may not suppress.
/// </summary>
/// <remarks>
/// <b>Written from a production regression.</b> The veto exists to stop a refusal — "No character by
/// that name found." — being read as a measured zero for a game with hundreds of players on it. It
/// was applied to the whole payload before any count was attempted, so widening its vocabulary
/// silently suppressed counts that games had volunteered: `the-burbs` had been reporting three
/// players and went dark on the first crawl after the change.
/// </remarks>
public class WhoParserVetoTests
{
    /// <summary>
    /// The exact shape that regressed: a count and a name prompt in one payload.
    /// </summary>
    /// <remarks>
    /// A game printing its count on the connect screen and then re-prompting is not a game that
    /// failed to understand WHO. It answered, and then asked a question.
    /// </remarks>
    [Test]
    public async Task ACountIsKeptEvenWhenTheServerAlsoAsksForAName()
    {
        var reading = new WhoParser().Parse(
            "There are three people connected to the game.\nPlease enter a name:");

        await Assert.That(reading.HasCount).IsTrue();
        await Assert.That(reading.Count).IsEqualTo(3);
    }

    [Test]
    public async Task TheSameHoldsForANumberRatherThanAWord()
    {
        var reading = new WhoParser().Parse(
            "There are 12 players connected.\nPlease enter your character name:");

        await Assert.That(reading.Count).IsEqualTo(12);
    }

    /// <summary>
    /// The case the veto was built for, and it must still hold: a refusal that a zero-reading pattern
    /// would otherwise turn into a measured zero for a busy game.
    /// </summary>
    [Test]
    [Arguments("No character by that name found.")]
    [Arguments("There is no player by that name. Please enter your account name, or \"new\" to create a new account:")]
    [Arguments("That name is reserved for a senior member of the mud.")]
    [Arguments("The MUD Administrator has found the name to be unacceptable. Name:")]
    [Arguments("Password:")]
    public async Task ARefusalIsStillNeverAZero(string payload)
    {
        var reading = new WhoParser().Parse(payload);

        await Assert.That(reading.HasCount).IsFalse();
    }

    /// <summary>
    /// A zero loses to the veto, and a positive count beats it. That asymmetry is the fix: the danger
    /// is a fabricated absence, and no login prompt fabricates a number.
    /// </summary>
    [Test]
    public async Task AZeroDoesNotSurviveALoginPromptButANumberDoes()
    {
        var suppressed = new WhoParser().Parse(
            "There are no players connected.\nNo character by that name found.");
        var kept = new WhoParser().Parse(
            "There are 4 players connected.\nNo character by that name found.");

        await Assert.That(suppressed.HasCount).IsFalse();
        await Assert.That(kept.Count).IsEqualTo(4);
    }

    /// <summary>A genuine zero, with nothing refusing anything, is still a measurement.</summary>
    [Test]
    public async Task AGenuineZeroIsUntouched()
    {
        var reading = new WhoParser().Parse("There are no players connected.");

        await Assert.That(reading.HasCount).IsTrue();
        await Assert.That(reading.Count).IsEqualTo(0);
    }

    /// <summary>
    /// Structure is not read off a login prompt. A refusal split over several lines is several rows
    /// to anything counting rows, which is how a busy game becomes a plausible-looking small number.
    /// </summary>
    [Test]
    public async Task RowsAreNotCountedOutOfARefusal()
    {
        var reading = new WhoParser().Parse(
            "Name        Idle    Status\nNo character by that name found.\nPlease enter a name:");

        await Assert.That(reading.HasCount).IsFalse();
    }
}
