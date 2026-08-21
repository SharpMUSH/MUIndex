using MUI.Crawler;
using MUI.I3;

namespace MUI.Crawler.Tests;

public class I3ServiceOptionsTests
{
    /// <summary>
    /// Off unless asked for: joining I3 registers a name on somebody else's network permanently and
    /// must never happen as a side effect of <c>compose up</c>.
    /// </summary>
    [Test]
    public async Task TheI3PassIsOffByDefault()
    {
        await Assert.That(new I3ServiceOptions().Enabled).IsFalse();
        await Assert.That(new CrawlerOptions().I3.Enabled).IsFalse();
    }

    /// <summary>
    /// Its own lock, so a long crawl cycle can't delay an I3 pass and vice versa.
    /// </summary>
    [Test]
    public async Task TheI3PassCompetesForItsOwnLock()
    {
        var keys = new[]
        {
            AdvisoryLease.CrawlKey,
            AdvisoryLease.PresenceMaintenanceKey,
            AdvisoryLease.I3Key,
        };

        await Assert.That(keys.Distinct().Count()).IsEqualTo(keys.Length);
        await Assert.That(new I3ServiceOptions().AdvisoryLockKey).IsEqualTo(AdvisoryLease.I3Key);
    }

    /// <summary>
    /// Refused at startup rather than discovered as an authentication failure every five minutes.
    /// </summary>
    [Test]
    public async Task EnablingThePassWithoutAKeyIsRefusedAtStartup()
    {
        var options = new I3ServiceOptions { Enabled = true, Gateway = new GatewayOptions() };

        await Assert.That(options.Validate).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ADisabledPassNeedsNoKey()
    {
        await Assert.That(new I3ServiceOptions().Validate).ThrowsNothing();
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task ANonPositiveIntervalIsRefused(int minutes)
    {
        var options = new I3ServiceOptions
        {
            Enabled = true,
            Gateway = new GatewayOptions { ApiKey = "k" },
            Interval = TimeSpan.FromMinutes(minutes),
        };

        await Assert.That(options.Validate).Throws<InvalidOperationException>();
    }

    /// <summary>
    /// The per-mud floor is what bounds what we send, not the pass interval — a pass reads a locally
    /// cached mudlist and then asks whichever muds are overdue.
    /// </summary>
    [Test]
    public async Task ThePerMudFloorIsLongerThanThePassInterval()
    {
        var options = new I3ServiceOptions();

        await Assert.That(options.Pass.AskEvery).IsGreaterThan(options.Interval);
    }
}
