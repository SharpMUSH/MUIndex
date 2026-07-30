using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// The rule that a WHO parser may not invent a number. An unreadable response has to be
/// distinguishable from an empty game, or every unparseable listing renders as dead (spec §6.3).
/// </summary>
public class WhoReadingTests
{
    [Test]
    public async Task AnUnreadResponseCarriesNoCount()
    {
        await Assert.That(WhoReading.Unread.HasCount).IsFalse();
        await Assert.That(WhoReading.Unread.Count).IsNull();
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
    }
}
