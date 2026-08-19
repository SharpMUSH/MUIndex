using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// The rule that a WHO parser may not invent a number. An unreadable response has to be
/// distinguishable from an empty game, or every unparseable listing renders as dead (spec §6.3) —
/// and, one level down, from a WHO that was never sent at all.
/// </summary>
public class WhoReadingTests
{
    [Test]
    public async Task AnUnreadableResponseCarriesNoCount()
    {
        await Assert.That(WhoReading.Unreadable.HasCount).IsFalse();
        await Assert.That(WhoReading.Unreadable.Count).IsNull();
    }

    [Test]
    public async Task AQuestionNeverAskedCarriesNoCountEither()
    {
        await Assert.That(WhoReading.NotAsked.HasCount).IsFalse();
        await Assert.That(WhoReading.NotAsked.Count).IsNull();
    }

    [Test]
    public async Task NotAskedAndUnreadableAreDifferentByValue()
    {
        // Both used to collapse to the same value, so no writer downstream could tell "we never
        // asked" from "we asked and could not read it" — spec §5.4's named bug, one level down.
        await Assert.That(WhoReading.NotAsked).IsNotEqualTo(WhoReading.Unreadable);
        await Assert.That(WhoReading.NotAsked.Confidence).IsNotEqualTo(WhoReading.Unreadable.Confidence);
    }

    [Test]
    public async Task OnlyOneOfThemClaimsToHaveMeasuredAnything()
    {
        await Assert.That(WhoReading.Unreadable.Attempted).IsTrue();
        await Assert.That(WhoReading.NotAsked.Attempted).IsFalse();
    }

    [Test]
    public async Task AProbeThatNeverGotAsFarAsAskingSaysSo()
    {
        // A failed dial has no WHO evidence of any kind, and must not present as one that tried.
        var failed = new ProbeResult
        {
            Host = "corvid.example",
            Port = 4201,
            ObservedAt = DateTimeOffset.UnixEpoch,
            Outcome = ProbeOutcome.Failed,
            Failure = new FailureDetail("timeout", "probe budget exhausted"),
        };

        await Assert.That(failed.Who.Attempted).IsFalse();
        await Assert.That(failed.Who).IsEqualTo(WhoReading.NotAsked);
    }

    [Test]
    public async Task TheParserNeverClaimsTheQuestionWasNotAsked()
    {
        // The parser is only ever handed the answer to a WHO that went out, so "not asked" is not a
        // verdict available to it. Silence in the WHO window is an unreadable answer, not an absent
        // question — real games do answer a pre-login WHO with nothing.
        var parser = new WhoParser();

        foreach (var response in new[] { null, "", "   ", "\r\n\r\n", "!!! nothing legible here !!!" })
        {
            await Assert.That(parser.Parse(response).Confidence).IsNotEqualTo(WhoConfidence.NotAsked);
            await Assert.That(parser.Parse(response).HasCount).IsFalse();
        }
    }

    [Test]
    public async Task AnEmptyGameIsACountOfZeroAndNotAnAbsentCount()
    {
        var empty = new WhoReading(WhoConfidence.Count, Count: 0);

        await Assert.That(empty.HasCount).IsTrue();
        await Assert.That(empty.Count).IsEqualTo(0);
    }

    [Test]
    public async Task PerPlayerConfidenceIsWhatUnlocksAggregates()
    {
        var reading = new WhoReading(WhoConfidence.PerPlayer, Count: 7, IdentifiablePlayers: 7);

        await Assert.That(reading.Confidence).IsEqualTo(WhoConfidence.PerPlayer);
        await Assert.That(reading.IdentifiablePlayers).IsEqualTo(7);
        await Assert.That(reading.HasCount).IsTrue();
    }

    [Test]
    public async Task TheDefaultConfidenceIsTheOneThatClaimsLeast()
    {
        // Enum order is load-bearing: a WhoConfidence nobody filled in has measured nothing, and the
        // zero value is what a default-constructed anything downstream will carry.
        var unset = Array.Empty<WhoConfidence>().FirstOrDefault();

        await Assert.That(unset).IsEqualTo(WhoConfidence.NotAsked);
    }
}
