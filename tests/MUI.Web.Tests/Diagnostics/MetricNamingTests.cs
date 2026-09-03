using MUI.Web.Diagnostics;

namespace MUI.Web.Tests.Diagnostics;

/// <summary>
/// The naming rules Prometheus's own <c>promtool check metrics</c> applies.
/// </summary>
/// <remarks>
/// Written after promtool caught <c>mui_process_cpu_count</c> on the first real scrape. None of
/// these break a scrape outright — that is the problem with them. A name that violates a convention
/// parses, graphs, and then collides with the meaning tooling attaches to the suffix, at whatever
/// later moment somebody adds the histogram it was reserved for. Encoded here so the next metric
/// added does not have to be caught by a person running promtool by hand.
/// </remarks>
public class MetricNamingTests
{
    /// <summary>Every series name this endpoint can emit, read off a real scrape.</summary>
    private static IReadOnlyList<string> Names()
    {
        var text = new PrometheusText();

        RuntimeMetrics.WriteTo(text);
        new CrawlMetrics(TimeProvider.System).WriteTo(text);
        new RequestMetrics().WriteTo(text);

        return
        [
            .. text.ToString()
                .Split('\n')
                .Where(l => l.StartsWith("# TYPE ", StringComparison.Ordinal))
                .Select(l => l.Split(' ')[2]),
        ];
    }

    /// <summary>
    /// <c>_count</c>, <c>_sum</c> and <c>_bucket</c> belong to histograms and summaries. Nothing here
    /// is one, so nothing here may claim the suffix.
    /// </summary>
    [Test]
    public async Task NoNameClaimsASuffixReservedForHistograms()
    {
        var offenders = Names()
            .Where(n => n.EndsWith("_count", StringComparison.Ordinal)
                || n.EndsWith("_sum", StringComparison.Ordinal)
                || n.EndsWith("_bucket", StringComparison.Ordinal))
            .ToList();

        await Assert.That(offenders).IsEmpty();
    }

    /// <summary>
    /// A counter's name ends in <c>_total</c> and a gauge's does not, which is the convention every
    /// dashboard and every alerting rule reads before it reads the type.
    /// </summary>
    [Test]
    public async Task OnlyCountersAreNamedTotal()
    {
        var text = new PrometheusText();

        RuntimeMetrics.WriteTo(text);
        new CrawlMetrics(TimeProvider.System).WriteTo(text);
        new RequestMetrics().WriteTo(text);

        var wrong = text.ToString()
            .Split('\n')
            .Where(l => l.StartsWith("# TYPE ", StringComparison.Ordinal))
            .Select(l => l.Split(' '))
            .Where(parts => parts[2].EndsWith("_total", StringComparison.Ordinal) != (parts[3] == "counter"))
            .Select(parts => $"{parts[2]} is a {parts[3]}")
            .ToList();

        await Assert.That(wrong).IsEmpty();
    }

    /// <summary>
    /// Every name is prefixed, so a scrape mixed with node-exporter's and cadvisor's series on one
    /// dashboard says which process it came from.
    /// </summary>
    [Test]
    public async Task EveryNameCarriesTheSitesPrefix()
    {
        foreach (var name in Names())
        {
            await Assert.That(name).StartsWith("mui_");
        }
    }
}
