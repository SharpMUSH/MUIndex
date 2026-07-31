using MUI.Catalog;
using MUI.Crawl;
using MUI.Crawler.Tests.Support;

namespace MUI.Crawler.Tests;

/// <summary>
/// Which of a probe's three possible counts becomes the one presence row (spec §5.2's ladder).
/// </summary>
public class PresenceChoiceTests
{
    [Test]
    public async Task WhoBeatsMsspBecauseItIsLiveRatherThanCached()
    {
        var reading = PresenceChoice.From(Probes.Answered(
            mssp: Probes.Mssp(("PLAYERS", "48")),
            who: new WhoReading(WhoConfidence.Count, 3)));

        await Assert.That(reading.Count).IsEqualTo(3);
        await Assert.That(reading.Source).IsEqualTo(FieldSource.Who);
    }

    [Test]
    public async Task AZeroFromWhoStillBeatsANonZeroFromMssp()
    {
        // The uncomfortable case, and the one the ladder is for. A measured zero is a measurement; an
        // MSSP PLAYERS is whatever the codebase last cached. Preferring the larger number would be
        // preferring the more flattering one.
        var reading = PresenceChoice.From(Probes.Answered(
            mssp: Probes.Mssp(("PLAYERS", "48")),
            who: new WhoReading(WhoConfidence.Count, 0)));

        await Assert.That(reading.Count).IsEqualTo(0);
        await Assert.That(reading.Source).IsEqualTo(FieldSource.Who);
    }

    [Test]
    public async Task MsspIsReadWhenWhoCouldNotBe()
    {
        var reading = PresenceChoice.From(Probes.Answered(
            mssp: Probes.Mssp(("PLAYERS", "48")), who: WhoReading.Unreadable));

        await Assert.That(reading.Count).IsEqualTo(48);
        await Assert.That(reading.Source).IsEqualTo(FieldSource.Mssp);
    }

    [Test]
    public async Task TheConnectScreenIsReadLastAndOnlyWhenBothOthersFailed()
    {
        // Aardwolf: no MSSP, no pre-login WHO, and "Players Currently Online: 215" on the screen it
        // hands every anonymous connection. The only rung that reaches it, and last for a reason.
        var reading = PresenceChoice.From(Probes.Answered(
            banner: "Players Currently Online: 215", who: WhoReading.Unreadable));

        await Assert.That(reading.Count).IsEqualTo(215);
        await Assert.That(reading.Source).IsEqualTo(FieldSource.Banner);
    }

    [Test]
    public async Task ANonNumericMsspPlayersIsSaidOutLoudRatherThanTreatedAsAbsent()
    {
        var reading = PresenceChoice.From(Probes.Answered(
            mssp: Probes.Mssp(("PLAYERS", "lots")), who: WhoReading.Unreadable));

        await Assert.That(reading.Count).IsNull();
        await Assert.That(reading.Reason).IsEqualTo(UnmeasurableReason.PlayersNotNumeric);
    }

    [Test]
    public async Task AnUnreadableWhoIsUncountableAndNeverZero()
    {
        var reading = PresenceChoice.From(Probes.Answered(who: WhoReading.Unreadable));

        await Assert.That(reading.Count).IsNull();
        await Assert.That(reading.Reason).IsEqualTo(UnmeasurableReason.WhoUnparseable);
    }

    [Test]
    public async Task AGameThatWasNeverAskedIsADifferentReasonFromOneWeCouldNotRead()
    {
        var reading = PresenceChoice.From(Probes.Answered());

        await Assert.That(reading.Reason).IsEqualTo(UnmeasurableReason.WhoNotOffered);
    }

    [Test]
    public async Task ANegativeMsspPlayersIsRefusedRatherThanStored()
    {
        // The schema refuses it too (presence_sample_count_is_not_negative), and a reading that got
        // that far would fail at the database with no way back. Refuse where the value is read.
        var reading = PresenceChoice.From(Probes.Answered(
            mssp: Probes.Mssp(("PLAYERS", "-4")), who: WhoReading.Unreadable));

        await Assert.That(reading.Count).IsNull();
        await Assert.That(reading.Reason).IsEqualTo(UnmeasurableReason.PlayersNotNumeric);
    }

    [Test]
    public async Task AFailedProbeHasNoReadingAtAll()
    {
        // §5.4's third state is the absence of a row, not a value in one. Asking for a reading is a
        // caller mistake and is refused rather than answered with something plausible.
        await Assert.That(() => PresenceChoice.From(Probes.Failed())).Throws<ArgumentException>();
    }
}
