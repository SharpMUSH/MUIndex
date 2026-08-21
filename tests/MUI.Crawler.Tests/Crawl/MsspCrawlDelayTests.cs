using MUI.Crawler.Tests.Support;
using MUI.Discovery;

namespace MUI.Crawler.Tests;

/// <summary>
/// Reading the gap a server asked for (spec §7.7, §11), and composing it with the backoff.
/// </summary>
public class MsspCrawlDelayTests
{
    [Test]
    public async Task HoursAreHours()
    {
        var delay = MsspCrawlDelay.From(Probes.Answered(mssp: Probes.Mssp(("CRAWL DELAY", "24"))));

        await Assert.That(delay).IsEqualTo(TimeSpan.FromHours(24));
    }

    [Test]
    public async Task MinusOneIsNoPreferenceAndNotZero()
    {
        // MSSP's own reading. A server stating 0 has said "as often as you like"; one stating -1 has
        // said nothing, and the difference is only visible if the reader keeps it.
        await Assert.That(MsspCrawlDelay.Parse("-1")).IsNull();
        await Assert.That(MsspCrawlDelay.Parse("0")).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task AnUnreadableValueIsNotARequest()
    {
        await Assert.That(MsspCrawlDelay.Parse("soon")).IsNull();
        await Assert.That(MsspCrawlDelay.Parse(null)).IsNull();
    }

    [Test]
    public async Task AnAbsurdRequestIsClampedRatherThanHonouredIntoRetirement()
    {
        // Politeness is honoured; a typo is not allowed to remove a game from the crawl for a century,
        // which is retirement by the back door and is the one thing §7.4 forbids outright.
        await Assert.That(MsspCrawlDelay.Parse("87600")).IsEqualTo(MsspCrawlDelay.Longest);
    }

    [Test]
    public async Task AReportWeNeverReceivedStatesNoDelay()
    {
        await Assert.That(MsspCrawlDelay.From(Probes.Answered())).IsNull();
    }

    [Test]
    public async Task AStatedMonthOutranksTheWeeklyFloor()
    {
        // §7.7 resolves §7.4 against §11 in favour of politeness: the server's request is applied
        // after the weekly clamp, so a server asking for thirty days gets thirty days.
        var asked = MsspCrawlDelay.From(Probes.Answered(mssp: Probes.Mssp(("CRAWL DELAY", "720"))));

        await Assert.That(ProbeSchedule.Next(consecutiveFailures: 0, asked))
            .IsEqualTo(TimeSpan.FromDays(30));
    }
}
