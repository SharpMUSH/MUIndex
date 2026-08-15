using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// §11's redaction, and the property the whole arrangement rests on: a parser can be replayed
/// against what is kept, and a person cannot be found in it.
/// </summary>
public class PayloadRedactionTests
{
    /// <summary>
    /// mush.pennmush.org:4201, captured 2026-07-30 — the response the structural parser was built
    /// from, including the operator's renamed DOING column.
    /// </summary>
    private const string Mush = """
        Player Name          On For   Idle  ThereIsNoSpoonButIWantYogurt
        Xperta               9m 21s     9m  Stuck in my Own Prison
        Thoran              11m 48s    11m
        gelatin          7h 46m 19s     7h  wibble
        There are 3 players connected.
        """;

    /// <summary>No name survives, and neither does anything anybody typed about themselves.</summary>
    [Test]
    public async Task NoNameSurvives()
    {
        var redacted = PayloadRedaction.Structural(Mush);

        foreach (var name in (string[])["Xperta", "Thoran", "gelatin", "wibble"])
        {
            await Assert.That(redacted.Contains(name, StringComparison.OrdinalIgnoreCase)).IsFalse();
        }

        // Nor the free text beside them, which is a place people write about themselves.
        await Assert.That(redacted.Contains("Prison", StringComparison.OrdinalIgnoreCase)).IsFalse();
        await Assert.That(redacted.Contains("Yogurt", StringComparison.OrdinalIgnoreCase)).IsFalse();
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

        var original = parser.Parse(Mush);
        var replayed = parser.Parse(PayloadRedaction.Structural(Mush));

        await Assert.That(replayed.Count).IsEqualTo(original.Count);
        await Assert.That(replayed.Confidence).IsEqualTo(original.Confidence);
        await Assert.That(replayed.Count).IsEqualTo(3);
    }

    /// <summary>
    /// A count is not a person, so digits go through untouched.
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
        var redacted = PayloadRedaction.Structural(Mush);

        var before = Mush.Split('\n');
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

        await Assert.That(parser.Parse(prompt).Confidence).IsEqualTo(WhoConfidence.Unknown);
    }

    /// <summary>Nothing in, nothing out — and never a null to trip a writer.</summary>
    [Test]
    public async Task AnEmptyPayloadIsEmpty()
    {
        await Assert.That(PayloadRedaction.Structural(null)).IsEqualTo(string.Empty);
        await Assert.That(PayloadRedaction.Structural("")).IsEqualTo(string.Empty);
    }
}
