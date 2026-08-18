namespace MUI.Discovery.Tests;

/// <summary>
/// The schedule is pure arithmetic and is tested as such. The one behaviour worth naming twice: it has
/// no "never" (spec §7.4).
/// </summary>
public class ProbeScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AHealthyGameIsProbedAtTheBaseInterval()
    {
        await Assert.That(ProbeSchedule.Next(0, null)).IsEqualTo(TimeSpan.FromHours(6));
    }

    [Test]
    public async Task AGameWithSomebodyOnItIsProbedSooner()
    {
        // §7.7: "tightened for games with recent activity".
        await Assert.That(ProbeSchedule.Next(0, null, ActivityBand.Busy)).IsEqualTo(TimeSpan.FromHours(2));
        await Assert.That(ProbeSchedule.Next(0, null, ActivityBand.Quiet)).IsEqualTo(TimeSpan.FromHours(6));
        await Assert.That(ProbeSchedule.Next(0, null, ActivityBand.Unknown)).IsEqualTo(TimeSpan.FromHours(6));
    }

    [Test]
    public async Task AFirstFailureIsRecheckedSoonRatherThanBackedOffForHours()
    {
        // A single failure is a question, not an answer, so it buys a recheck rather than the
        // six-hour backoff.
        await Assert.That(ProbeSchedule.Next(1, null)).IsEqualTo(ProbeSchedule.RecheckInterval);
        await Assert.That(ProbeSchedule.RecheckInterval).IsEqualTo(TimeSpan.FromMinutes(10));
    }

    [Test]
    public async Task TheRecheckIsNotAnExcuseToIgnoreWhatTheServerAskedFor()
    {
        // §7.7 again: the recheck shortens *our* backoff and is not licence to knock every ten
        // minutes at a server that asked for an hour. max(CRAWL DELAY, interval) still holds.
        await Assert.That(ProbeSchedule.Next(1, TimeSpan.FromHours(1))).IsEqualTo(TimeSpan.FromHours(1));
    }

    [Test]
    [Arguments(2, 6)]
    [Arguments(3, 12)]
    [Arguments(4, 24)]
    [Arguments(5, 48)]
    [Arguments(6, 96)]
    public async Task EachConfirmedFailureDoublesTheInterval(int failures, int expectedHours)
    {
        // The ladder starts at the *second* failure, which is the first one we believe. Its rungs
        // are unchanged; only their entry point moved.
        await Assert.That(ProbeSchedule.Next(failures, null)).IsEqualTo(TimeSpan.FromHours(expectedHours));
    }

    [Test]
    public async Task ActivityDoesNotTightenAFailingGameBecauseThereWasNoActivityToRead()
    {
        await Assert.That(ProbeSchedule.Next(4, null, ActivityBand.Busy)).IsEqualTo(TimeSpan.FromHours(24));
    }

    [Test]
    public async Task TheIntervalIsClampedAtOneWeekAndStaysThereForEver()
    {
        // The whole product differentiator. A game dark for two years is still probed weekly, and after
        // a decade of failures it is still probed weekly — there is no retirement and no "never".
        await Assert.That(ProbeSchedule.Next(7, null)).IsEqualTo(ProbeSchedule.LongestInterval);
        await Assert.That(ProbeSchedule.Next(50, null)).IsEqualTo(ProbeSchedule.LongestInterval);
        await Assert.That(ProbeSchedule.Next(100_000, null)).IsEqualTo(ProbeSchedule.LongestInterval);
        await Assert.That(ProbeSchedule.LongestInterval).IsEqualTo(TimeSpan.FromDays(7));
    }

    [Test]
    public async Task ThereIsNoNeverInThisSchedule()
    {
        // Whatever a caller passes, the answer is a finite gap: nothing here can express "stop
        // probing this" (§7.4).
        var everyFailureCount = new[] { 0, 1, 7, 20, 21, 1_000, int.MaxValue };

        foreach (var failures in everyFailureCount)
        {
            var interval = ProbeSchedule.Next(failures, null);

            await Assert.That(interval).IsLessThanOrEqualTo(ProbeSchedule.LongestInterval);
            await Assert.That(interval).IsGreaterThan(TimeSpan.Zero);
            await Assert.That(ProbeSchedule.NextProbeAt(Now, failures, null)).IsNotEqualTo(DateTimeOffset.MaxValue);
        }
    }

    [Test]
    public async Task AServerAskingForALongerGapGetsIt()
    {
        // "CRAWL DELAY — preferred minimum number of hours between crawls" (spec §11), honoured as a
        // floor under the interval.
        await Assert.That(ProbeSchedule.Next(0, TimeSpan.FromHours(24))).IsEqualTo(TimeSpan.FromHours(24));
    }

    [Test]
    public async Task AServerAskingForAShorterGapDoesNotGetVisitedMoreOften()
    {
        // It is a minimum a server asks for, not a permission it grants.
        await Assert.That(ProbeSchedule.Next(0, TimeSpan.FromMinutes(5))).IsEqualTo(TimeSpan.FromHours(6));
    }

    [Test]
    public async Task AServerAskingForLongerThanTheCeilingGetsIt()
    {
        // Where §7.4 (weekly ceiling) and §11 (CRAWL DELAY as floor) disagree, §7.7 rules for
        // politeness: clamp first, then apply the server's request — max(CRAWL DELAY, backoff).
        await Assert.That(ProbeSchedule.Next(9, TimeSpan.FromDays(30))).IsEqualTo(TimeSpan.FromDays(30));
        await Assert.That(ProbeSchedule.Next(0, TimeSpan.FromDays(30))).IsEqualTo(TimeSpan.FromDays(30));
    }

    [Test]
    public async Task NoPreferenceMeansOurOwnInterval()
    {
        await Assert.That(ProbeSchedule.Next(0, crawlDelay: null)).IsEqualTo(ProbeSchedule.BaseInterval);
    }

    [Test]
    public async Task NextProbeAtIsJustNowPlusTheInterval()
    {
        await Assert.That(ProbeSchedule.NextProbeAt(Now, 2, null)).IsEqualTo(Now + TimeSpan.FromHours(6));
    }

    [Test]
    public async Task ANegativeFailureCountIsAProgrammingError()
    {
        await Assert.That(() => ProbeSchedule.Next(-1, null)).Throws<ArgumentOutOfRangeException>();
    }
}
