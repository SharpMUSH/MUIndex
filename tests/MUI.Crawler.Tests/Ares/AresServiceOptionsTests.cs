using MUI.Ares;

namespace MUI.Crawler.Tests;

/// <summary>
/// What a deployment has to say before the pass will run, and what it gets for free.
/// </summary>
public class AresServiceOptionsTests
{
    /// <summary>
    /// On by default, unlike I3's. Joining I3 registers a name on somebody else's router permanently
    /// and must never be a side effect of <c>compose up</c>; a GET against a documented API with our
    /// own credentials registers nothing at all.
    /// </summary>
    [Test]
    public async Task ThePassIsOnByDefault()
    {
        await Assert.That(new AresServiceOptions().Enabled).IsTrue();
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
