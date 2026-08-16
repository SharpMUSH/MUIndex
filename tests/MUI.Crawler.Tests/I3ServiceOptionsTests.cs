using MUI.Crawler;
using MUI.I3;

namespace MUI.Crawler.Tests;

public class I3ServiceOptionsTests
{
    /// <summary>
    /// <b>Off unless asked for.</b> The pass needs a sidecar that sits behind a compose profile,
    /// because joining I3 registers a name on somebody else's network permanently and must never
    /// happen as a side effect of <c>compose up</c>. A default of true would make every deployment
    /// without that container fail a connection on a loop for a feature nobody turned on.
    /// </summary>
    [Test]
    public async Task TheI3PassIsOffByDefault()
    {
        await Assert.That(new I3ServiceOptions().Enabled).IsFalse();
        await Assert.That(new CrawlerOptions().I3.Enabled).IsFalse();
    }

    /// <summary>
    /// Its own lock. Two replicas passing at once would ask every mud twice as often as we said we
    /// would, and sharing the crawl lease would let a long cycle delay a pass and vice versa.
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
    /// Refused at startup rather than discovered as an authentication failure every five minutes. The
    /// sidecar's shipped configuration contains example keys; a deployment that enabled this without
    /// setting one has not finished.
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
