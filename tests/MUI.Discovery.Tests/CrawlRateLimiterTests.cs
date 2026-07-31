using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// The rate limiter, driven by a clock the test moves by hand. Nothing here sleeps: a limiter tested by
/// waiting for its own interval proves only that the machine was not too busy.
/// </summary>
public class CrawlRateLimiterTests
{
    private static readonly DiscoveryOptions Options = new()
    {
        GlobalInterval = TimeSpan.FromSeconds(2),
        PerHostInterval = TimeSpan.FromMinutes(5),
    };

    [Test]
    public async Task TheFirstConnectionIsAllowedImmediately()
    {
        var limiter = new CrawlRateLimiter(Options, new ManualTimeProvider());

        await Assert.That(limiter.DelayBefore("a.example.org")).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task TheGlobalIntervalHoldsBackTheNextConnectionToADifferentHost()
    {
        var time = new ManualTimeProvider();
        var limiter = new CrawlRateLimiter(Options, time);

        limiter.RecordStart("a.example.org");

        await Assert.That(limiter.DelayBefore("b.example.org")).IsEqualTo(TimeSpan.FromSeconds(2));
        time.Advance(TimeSpan.FromSeconds(1));
        await Assert.That(limiter.DelayBefore("b.example.org")).IsEqualTo(TimeSpan.FromSeconds(1));
        time.Advance(TimeSpan.FromSeconds(1));
        await Assert.That(limiter.DelayBefore("b.example.org")).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task TheSameHostWaitsTheLongerPerHostInterval()
    {
        var time = new ManualTimeProvider();
        var limiter = new CrawlRateLimiter(Options, time);

        limiter.RecordStart("a.example.org");
        time.Advance(TimeSpan.FromSeconds(10));

        await Assert.That(limiter.DelayBefore("b.example.org")).IsEqualTo(TimeSpan.Zero);
        await Assert.That(limiter.DelayBefore("a.example.org")).IsEqualTo(TimeSpan.FromSeconds(290));

        time.Advance(TimeSpan.FromSeconds(290));
        await Assert.That(limiter.DelayBefore("a.example.org")).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task PortsOnOneMachineShareTheHostLimitBecauseTheKeyIsTheHost()
    {
        // HostGate owns "not two at once" and the limiter owns "not two in quick succession", and both
        // mean the machine rather than the socket — six advertised ports are one operator's box, and
        // politeness is owed to the operator.
        var limiter = new CrawlRateLimiter(Options, new ManualTimeProvider());

        limiter.RecordStart("mud.example.org");

        await Assert.That(limiter.DelayBefore("MUD.Example.ORG.")).IsEqualTo(TimeSpan.FromMinutes(5));
    }

    [Test]
    public async Task AStreamOfConnectionsIsSpacedByTheGlobalInterval()
    {
        var time = new ManualTimeProvider();
        var limiter = new CrawlRateLimiter(Options, time);
        var starts = new List<DateTimeOffset>();

        for (var i = 0; i < 5; i++)
        {
            var host = $"host{i}.example.org";
            time.Advance(limiter.DelayBefore(host));
            starts.Add(time.GetUtcNow());
            limiter.RecordStart(host);
        }

        var gaps = starts.Zip(starts.Skip(1), (first, second) => second - first).ToList();

        await Assert.That(gaps).IsNotEmpty();
        foreach (var gap in gaps)
        {
            await Assert.That(gap).IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(2));
        }
    }

    [Test]
    public async Task WaitingForATurnStampsTheStartSoTheNextCallerIsHeldBack()
    {
        var limiter = new CrawlRateLimiter(Options, new ManualTimeProvider());

        await limiter.WaitForTurnAsync("a.example.org", CancellationToken.None);

        await Assert.That(limiter.DelayBefore("b.example.org")).IsEqualTo(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task WaitingForATurnReturnsOnceTheClockCatchesUp()
    {
        // The wait is driven by the injected clock, so the test moves time rather than spending it.
        var time = new ManualTimeProvider();
        var limiter = new CrawlRateLimiter(Options, time);
        limiter.RecordStart("a.example.org");

        var waiting = limiter.WaitForTurnAsync("b.example.org", CancellationToken.None);
        await Assert.That(waiting.IsCompleted).IsFalse();

        time.Advance(TimeSpan.FromSeconds(2));
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(waiting.IsCompletedSuccessfully).IsTrue();
    }

    [Test]
    public async Task ZeroIntervalsMeanNoWaitingAtAll()
    {
        var limiter = new CrawlRateLimiter(
            new DiscoveryOptions { GlobalInterval = TimeSpan.Zero, PerHostInterval = TimeSpan.Zero },
            new ManualTimeProvider());

        limiter.RecordStart("a.example.org");

        await Assert.That(limiter.DelayBefore("a.example.org")).IsEqualTo(TimeSpan.Zero);
    }
}
