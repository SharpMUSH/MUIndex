using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// §11's redaction, and the property the whole arrangement rests on: a parser can be replayed
/// against what is kept, and a person cannot be found in it.
/// </summary>
public class PayloadRedactionTests
{
    /// <summary>
    /// A WHO in the shape a real one has, with invented people in it.
    /// </summary>
    /// <remarks>
    /// Written rather than captured, deliberately: a real capture would put real player names into
    /// source history forever, exactly what the runtime TTL this feature is built around drops.
    /// </remarks>
    private const string Who = """
        Player Name          On For   Idle  AndNowForSomethingCompletelyElse
        Quillon              9m 21s     9m  Brooding on a parapet
        Marrow              11m 48s    11m
        4815162342       7h 46m 19s     7h  counting
        There are 3 players connected.
        """;

    /// <summary>No name survives, and neither does anything anybody typed about themselves.</summary>
    [Test]
    public async Task NoNameSurvives()
    {
        var redacted = PayloadRedaction.Structural(Who);

        foreach (var name in (string[])["Quillon", "Marrow", "counting", "4815162342"])
        {
            await Assert.That(redacted.Contains(name, StringComparison.OrdinalIgnoreCase)).IsFalse();
        }

        // Nor the free text beside them, which is a place people write about themselves.
        await Assert.That(redacted.Contains("parapet", StringComparison.OrdinalIgnoreCase)).IsFalse();
        await Assert.That(redacted.Contains("Completely", StringComparison.OrdinalIgnoreCase)).IsFalse();
    }

    /// <summary>
    /// The parser reads the redaction exactly as it read the original.
    /// </summary>
    /// <remarks>
    /// The point of the whole exercise. If this fails, the stored payload cannot be replayed and
    /// there is no reason to keep it.
    /// </remarks>
    [Test]
    public async Task TheParserReadsTheRedactionTheSameWay()
    {
        var parser = new WhoParser();

        var original = parser.Parse(Who);
        var replayed = parser.Parse(PayloadRedaction.Structural(Who));

        await Assert.That(replayed.Count).IsEqualTo(original.Count);
        await Assert.That(replayed.Confidence).IsEqualTo(original.Confidence);
        await Assert.That(replayed.Count).IsEqualTo(3);
    }

    /// <summary>
    /// A name made of digits is a name, and does not survive because it is numeric.
    /// </summary>
    /// <remarks>
    /// <c>4815162342</c> is a perfectly ordinary MU* name and also a bare digit run. A digit survives
    /// only on a line that also carries a word the parser reads (a summary sentence has one, a row of
    /// players does not), and a run with any letter in it is masked whole, so <c>A1ice</c> keeps
    /// neither its letters nor its digit.
    /// </remarks>
    [Test]
    public async Task ANameMadeOfDigitsIsStillAName()
    {
        var redacted = PayloadRedaction.Structural(Who);

        await Assert.That(redacted).DoesNotContain("4815162342");
        await Assert.That(PayloadRedaction.Structural("A1ice")).IsEqualTo("A0aaa");

        // And the count in the sentence that names what it counts is untouched.
        await Assert.That(redacted).Contains("There are 3 players connected.");
    }

    /// <summary>
    /// A shape that would parse differently is not kept at all.
    /// </summary>
    /// <remarks>
    /// The replay guarantee enforced where it is written rather than only asserted here. A shape
    /// exists to be re-parsed, so one that answers differently from the payload it came from is not
    /// evidence about anything — and keeping it would let a later vocabulary change quietly fill the
    /// window with rows that misreport what a server said.
    /// </remarks>
    [Test]
    public async Task OnlyAShapeThatReplaysIsKept()
    {
        var parser = new WhoParser();
        var shape = PayloadRedaction.Replayable(Who);

        await Assert.That(shape).IsNotNull();
        await Assert.That(parser.Parse(shape).Count).IsEqualTo(parser.Parse(Who).Count);

        await Assert.That(PayloadRedaction.Replayable(null)).IsNull();
    }

    /// <summary>
    /// A count is not a person, so digits go through where a count can be.
    /// </summary>
    /// <remarks>
    /// "There are 16 players connected" is the sentence the parser most wants to re-read, and a
    /// redaction masking the 16 would keep the sentence and destroy the measurement in it.
    /// </remarks>
    [Test]
    public async Task DigitsSurviveBecauseACountIsNotAPerson()
    {
        var redacted = PayloadRedaction.Structural("There are 16 players connected.");

        await Assert.That(redacted).IsEqualTo("There are 16 players connected.");
    }

    /// <summary>
    /// Column positions are preserved character for character.
    /// </summary>
    /// <remarks>
    /// A WHO is a column layout. A redaction that changed a run's length would move every column
    /// after it, and replaying a positional parser against that would measure the redactor rather
    /// than the parser.
    /// </remarks>
    [Test]
    public async Task EveryColumnStaysWhereItWas()
    {
        var redacted = PayloadRedaction.Structural(Who);

        var before = Who.Split('\n');
        var after = redacted.Split('\n');

        await Assert.That(after.Length).IsEqualTo(before.Length);

        for (var line = 0; line < before.Length; line++)
        {
            await Assert.That(after[line].Length).IsEqualTo(before[line].Length);
        }
    }

    /// <summary>
    /// A login prompt stays recognisable, because reading one as a game is the worst outcome.
    /// </summary>
    /// <remarks>
    /// DIKU-family games treat the login prompt as a character-name prompt, so WHO comes back as
    /// "No character by that name found." A redaction that masked those words would let a replay
    /// read a busy game as a measured zero — the fabrication the parser refuses.
    /// </remarks>
    [Test]
    public async Task ALoginPromptIsStillALoginPrompt()
    {
        var parser = new WhoParser();
        var prompt = PayloadRedaction.Structural("No character by that name found.");

        await Assert.That(parser.Parse(prompt).Confidence).IsEqualTo(WhoConfidence.LoginPrompt);
    }

    /// <summary>Nothing in, nothing out — and never a null to trip a writer.</summary>
    [Test]
    public async Task AnEmptyPayloadIsEmpty()
    {
        await Assert.That(PayloadRedaction.Structural(null)).IsEqualTo(string.Empty);
        await Assert.That(PayloadRedaction.Structural("")).IsEqualTo(string.Empty);
    }
}
