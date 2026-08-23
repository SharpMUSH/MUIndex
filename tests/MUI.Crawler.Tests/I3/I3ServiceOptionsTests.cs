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

    /// <summary>
    /// Root validation has to catch an enabled pass with no key, because root validation is what
    /// <c>AddMuiCrawler</c> calls.
    /// </summary>
    /// <remarks>
    /// <c>I3ServiceOptions.Validate</c> has always refused this, but nothing called it until
    /// <c>I3Service.ExecuteAsync</c> — by which point the hosted service is running, and an
    /// exception out of a <c>BackgroundService</c> takes the whole web tier with it. A deployment
    /// that enabled the pass and forgot the key got a dead site rather than a startup error naming
    /// the setting. <c>CrawlerSettings.Apply</c> already validated for the configuration path; this
    /// closes the same hole for a host that builds <c>CrawlerOptions</c> directly.
    /// </remarks>
    [Test]
    public async Task RootValidationRefusesAnEnabledPassWithNoKey()
    {
        var options = new CrawlerOptions { I3 = new I3ServiceOptions { Enabled = true } };

        await Assert.That(options.Validate).Throws<InvalidOperationException>();
    }

    /// <summary>
    /// The default graph a host gets must still validate. I3 is off unless asked for, so root
    /// validation is a no-op on it — this is the assertion that lets the one above be safe to add.
    /// </summary>
    [Test]
    public async Task DefaultCrawlerOptionsStillValidate()
    {
        var options = new CrawlerOptions();

        options.Validate();

        await Assert.That(options.I3.Enabled).IsFalse();
    }

    /// <summary>An enabled pass with a key passes root validation, so the gate is not simply "off".</summary>
    [Test]
    public async Task RootValidationAcceptsAnEnabledPassWithAKey()
    {
        var options = new CrawlerOptions
        {
            I3 = new I3ServiceOptions
            {
                Enabled = true,
                Gateway = new GatewayOptions { ApiKey = "not-a-real-key" },
            },
        };

        options.Validate();

        await Assert.That(options.I3.Enabled).IsTrue();
    }

    /// <summary>
    /// The DNS claim sweep had the same gap, and it is closed in the same place.
    /// </summary>
    /// <remarks>
    /// <c>DnsClaimSweeper</c> validated its own options inside <c>ExecuteAsync</c> too, so a
    /// non-positive interval was a dead web tier rather than a startup error. Same defect, same
    /// method, same fix — and unlike the two passes it is on by default, so a deployment does not
    /// have to have opted into anything to be exposed to it.
    /// </remarks>
    [Test]
    public async Task RootValidationRefusesANonPositiveSweepInterval()
    {
        var options = new CrawlerOptions
        {
            DnsClaims = new DnsClaimSweepOptions { Interval = TimeSpan.Zero },
        };

        await Assert.That(options.Validate).Throws<ArgumentException>();
    }
}
