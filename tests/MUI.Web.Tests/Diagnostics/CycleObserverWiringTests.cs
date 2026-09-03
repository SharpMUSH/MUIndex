using Microsoft.Extensions.DependencyInjection;

using MUI.Crawler;
using MUI.Web.Diagnostics;

namespace MUI.Web.Tests.Diagnostics;

/// <summary>
/// That the crawl loop's reports actually reach the counters <c>/metrics</c> serves.
/// </summary>
/// <remarks>
/// The seam is an interface in <c>MUI.Crawler</c> rather than a direct reference, because the arrow
/// only goes one way: the crawler must not know a web tier exists, for the same reason
/// <c>MUI.Catalog</c> must not know a socket does. What can go wrong is therefore a registration —
/// the counters exist, the loop records into something, and the something is a different instance —
/// which is invisible until a graph is flat during an incident. So the wiring is what is asserted.
/// </remarks>
public class CycleObserverWiringTests
{
    [Test]
    public async Task TheCrawlLoopsObserverIsTheSameObjectMetricsReads()
    {
        var services = new ServiceCollection();
        services.AddMuiMetrics();

        await using var provider = services.BuildServiceProvider();

        var observer = provider.GetRequiredService<ICycleObserver>();
        var metrics = provider.GetRequiredService<CrawlMetrics>();

        await Assert.That(observer).IsSameReferenceAs(metrics);
    }

    /// <summary>
    /// And a report handed to the loop's seam shows up in the scrape — the two halves joined, rather
    /// than each proven alone.
    /// </summary>
    [Test]
    public async Task AReportGivenToTheSeamReachesTheScrape()
    {
        var services = new ServiceCollection();
        services.AddMuiMetrics();

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ICycleObserver>().Observe(
            new CycleReport(
                Considered: 4, Probed: 4, Answered: 4, Failed: 0, Refused: 0, OptedOut: 0,
                Errored: 0, Listed: 0, ReviewsOpened: 0, Counted: 4, Unmeasurable: 0,
                Transitions: 0, ReferralsAdded: 0));

        var text = new PrometheusText();
        provider.GetRequiredService<CrawlMetrics>().WriteTo(text);

        await Assert.That(RuntimeMetricsTests.Read(text.ToString(), "mui_crawl_targets_total"))
            .IsEqualTo(4);
    }
}
