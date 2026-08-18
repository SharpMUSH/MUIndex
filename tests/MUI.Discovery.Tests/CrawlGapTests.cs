namespace MUI.Discovery.Tests;

/// <summary>
/// When a silence in the crawl means we stopped looking, rather than that nothing changed.
/// </summary>
/// <remarks>
/// Pure arithmetic, like <see cref="ProbeSchedule"/> beside it, and for the same reason: the decision
/// is worth reading on its own, without a database in the way.
/// </remarks>
public class CrawlGapTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ACrawlThatNeverRanHasNotStoppedLooking()
    {
        // A deployment on its first run has no last cycle, which is not an outage. Closing every
        // open interval here would punch a hole in a catalogue on the day it was created.
        await Assert.That(CrawlGap.StoppedLookingAt(null, Now)).IsNull();
    }

    [Test]
    public async Task ARestartIsNotAnOutage()
    {
        // The case this must not fire on, and it is the common one: deploys and reboots. Measured on
        // the production host 2026-08-18, two watchtower deploys produced crawl silences of about
        // forty seconds and a reboot about two minutes.
        await Assert.That(CrawlGap.StoppedLookingAt(Now.AddSeconds(-40), Now)).IsNull();
        await Assert.That(CrawlGap.StoppedLookingAt(Now.AddMinutes(-2), Now)).IsNull();
        await Assert.That(CrawlGap.StoppedLookingAt(Now.AddMinutes(-119), Now)).IsNull();
    }

    [Test]
    public async Task ASilenceLongerThanTheTightestCadenceIsAnOutage()
    {
        // The threshold is BusyInterval and not a number picked by feel: below it, no game can have
        // missed a scheduled probe, because nothing is probed more often than that.
        await Assert.That(CrawlGap.Threshold).IsEqualTo(ProbeSchedule.BusyInterval);
        await Assert.That(CrawlGap.StoppedLookingAt(Now.AddHours(-3), Now)).IsEqualTo(Now.AddHours(-3));
    }

    [Test]
    public async Task TheAnswerIsWhenWeStoppedLookingAndNotWhenWeNoticed()
    {
        // The whole point of the instant. An interval closed at "now" would credit the outage as
        // observed time and merely stop it growing; closed at the last cycle it is the truth. This is
        // the value the 2026-08-18 repair had to reconstruct from the edges of gaps after the fact.
        var stopped = Now.AddDays(-15);

        await Assert.That(CrawlGap.StoppedLookingAt(stopped, Now)).IsEqualTo(stopped);
    }

    [Test]
    public async Task AClockThatWentBackwardsIsNotAnOutage()
    {
        // A last cycle in the future is a skewed clock, not fifteen days of silence, and reading it
        // as one would close every interval in the catalogue at a time none of them had reached.
        await Assert.That(CrawlGap.StoppedLookingAt(Now.AddHours(1), Now)).IsNull();
    }

    [Test]
    public async Task ADeploymentMaySayHowPatientToBe()
    {
        await Assert.That(CrawlGap.StoppedLookingAt(Now.AddMinutes(-10), Now, TimeSpan.FromMinutes(5)))
            .IsEqualTo(Now.AddMinutes(-10));
    }
}
