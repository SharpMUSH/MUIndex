using MUI.Ares;

namespace MUI.Crawler.Tests;

/// <summary>
/// What a deployment has to say before the pass will run, and what it gets for free.
/// </summary>
public class AresServiceOptionsTests
{
    /// <summary>
    /// Off unless a host turns it on, and safe by construction rather than by a correction applied
    /// somewhere else.
    /// </summary>
    /// <remarks>
    /// <c>Validate</c> throws when the pass is on without credentials, so a default of <c>true</c>
    /// would crash any host that built <c>CrawlerOptions</c> by hand — on a feature it never asked
    /// for. <c>CrawlerSettings.Apply</c> is what turns this on once it finds a credential pair; the
    /// default may not depend on that call having happened.
    /// </remarks>
    [Test]
    public async Task ThePassIsOffUnlessAHostTurnsItOn()
    {
        await Assert.That(new AresServiceOptions().Enabled).IsFalse();
    }

    /// <summary>
    /// The graph a host gets by default has to be startable. Anything <c>AddMuiCrawlerCore</c>
    /// registers from a hand-built <c>CrawlerOptions</c> must survive its own validation, or a
    /// consumer that never touches configuration binding cannot start at all.
    /// </summary>
    [Test]
    public async Task DefaultCrawlerOptionsValidateWithNoCredentialsAnywhere()
    {
        var options = new CrawlerOptions();

        options.Ares.Validate();

        await Assert.That(options.Ares.Enabled).IsFalse();
    }

    /// <summary>
    /// Refused at startup, beside the setting that caused it, rather than discovered as an
    /// authentication failure once an hour for ever.
    /// </summary>
    [Test]
    public async Task EnabledWithoutCredentialsIsRefusedAtStartup()
    {
        await Assert.That(() => new AresServiceOptions { Enabled = true }.Validate())
            .Throws<InvalidOperationException>();
    }

    /// <summary>Half a credential pair is a typo in a compose file, not a state worth supporting.</summary>
    [Test]
    public async Task HalfACredentialPairIsRefused()
    {
        var options = new AresServiceOptions
        {
            Enabled = true,
            Hub = new AresOptions { ClientId = "muindex" },
        };

        await Assert.That(options.Validate).Throws<InvalidOperationException>();
    }

    /// <summary>A deployment that turned the pass off is not asked for a key it will never use.</summary>
    [Test]
    public async Task DisabledWithoutCredentialsIsFine()
    {
        new AresServiceOptions { Enabled = false }.Validate();

        await Assert.That(new AresServiceOptions { Enabled = false }.Enabled).IsFalse();
    }

    [Test]
    public async Task ValidCredentialsAndPositiveIntervalsPass()
    {
        var options = new AresServiceOptions
        {
            Enabled = true,
            Hub = new AresOptions { ClientId = "muindex", ApiKey = "not-a-real-key" },
        };

        options.Validate();

        await Assert.That(options.Interval).IsGreaterThan(TimeSpan.Zero);
    }

    [Test]
    public async Task ANonPositiveIntervalIsRefused()
    {
        var options = new AresServiceOptions
        {
            Enabled = false,
            Interval = TimeSpan.Zero,
        };

        await Assert.That(options.Validate).Throws<InvalidOperationException>();
    }

    /// <summary>
    /// Its own lock, so a long crawl cycle cannot delay the hourly read and a deployment running
    /// with the crawler off still keeps its listings current.
    /// </summary>
    [Test]
    public async Task ThePassCompetesForItsOwnLock()
    {
        await Assert.That(new AresServiceOptions().AdvisoryLockKey).IsEqualTo(AdvisoryLease.AresKey);

        long[] others =
        [
            AdvisoryLease.CrawlKey,
            AdvisoryLease.I3Key,
            AdvisoryLease.DnsClaimKey,
            AdvisoryLease.PresenceMaintenanceKey,
        ];

        await Assert.That(others).DoesNotContain(AdvisoryLease.AresKey);
    }
}
