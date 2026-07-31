namespace MUI.Discovery.Tests;

/// <summary>
/// These numbers reach other people's game servers. Lowering one should be a deliberate act with a
/// test to change (spec §11).
/// </summary>
public class DiscoveryOptionsTests
{
    [Test]
    public async Task TheDefaultsAreConservative()
    {
        var defaults = new DiscoveryOptions();

        await Assert.That(defaults.MaxDepth).IsEqualTo(4);
        await Assert.That(defaults.MaxFanOutPerSource).IsEqualTo(50);
        await Assert.That(defaults.FollowReferrals).IsTrue();
        await Assert.That(defaults.MaxConcurrency).IsEqualTo(8);
        await Assert.That(defaults.GlobalInterval).IsEqualTo(TimeSpan.FromMilliseconds(250));
        await Assert.That(defaults.PerHostInterval).IsEqualTo(TimeSpan.FromSeconds(30));
        await Assert.That(defaults.BatchSize).IsEqualTo(200);
        await Assert.That(defaults.PollInterval).IsEqualTo(TimeSpan.FromSeconds(30));
        await Assert.That(defaults.LeaseRetryInterval).IsEqualTo(TimeSpan.FromSeconds(30));
        await Assert.That(defaults.ProbeTimeout).IsEqualTo(TimeSpan.FromSeconds(60));
    }

    [Test]
    public async Task TheThresholdsDefaultToTheWeightsSoThereIsOneSourceOfTheDefault()
    {
        // Spec §15.5: the auto-merge threshold needs calibration against real data. It ships as a
        // configurable option so it can be tuned without a redeploy of the constants, and it defaults
        // to the conservative constant.
        var defaults = new DiscoveryOptions();

        await Assert.That(defaults.AutoMergeThreshold).IsEqualTo(IdentityWeights.AutoMergeThreshold);
        await Assert.That(defaults.ReviewThreshold).IsEqualTo(IdentityWeights.ReviewThreshold);
    }

    [Test]
    public async Task AReviewThresholdAboveTheMergeThresholdIsRefused()
    {
        var options = new DiscoveryOptions { AutoMergeThreshold = 0.4, ReviewThreshold = 0.9 };

        await Assert.That(options.Validate).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0, 8)]
    [Arguments(8, 0)]
    public async Task ZeroConcurrencyOrZeroBatchIsRefused(int concurrency, int batch)
    {
        var options = new DiscoveryOptions { MaxConcurrency = concurrency, BatchSize = batch };

        await Assert.That(options.Validate).Throws<ArgumentException>();
    }

    [Test]
    public async Task ANegativeDepthIsRefusedButZeroIsNot()
    {
        // Zero is "seeds only", which is a real deployment and not a mistake.
        await Assert.That(new DiscoveryOptions { MaxDepth = -1 }.Validate).Throws<ArgumentException>();
        await Assert.That(new DiscoveryOptions { MaxDepth = 0 }.Validate).ThrowsNothing();
    }

    [Test]
    public async Task ANonPositiveProbeTimeoutIsRefusedBecauseAWedgedProbeMustBeBounded()
    {
        // Spec §12: bounding is a correctness requirement, not hygiene — the crawler shares a process
        // with the web tier.
        var options = new DiscoveryOptions { ProbeTimeout = TimeSpan.Zero };

        await Assert.That(options.Validate).Throws<ArgumentException>();
    }

    [Test]
    public async Task ANegativeRateLimitIsRefused()
    {
        await Assert.That(new DiscoveryOptions { GlobalInterval = TimeSpan.FromSeconds(-1) }.Validate)
            .Throws<ArgumentException>();
        await Assert.That(new DiscoveryOptions { PerHostInterval = TimeSpan.FromSeconds(-1) }.Validate)
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task TheDefaultsThemselvesValidate()
    {
        await Assert.That(new DiscoveryOptions().Validate).ThrowsNothing();
    }
}
