using MUI.Web.Diagnostics;

namespace MUI.Web.Tests.Diagnostics;

/// <summary>
/// The exposition format, which is the whole contract with Prometheus.
/// </summary>
/// <remarks>
/// Written out by hand rather than taken from a library, so these are the tests that stand in for
/// the library's own. The format is unforgiving in one direction that matters: a scrape that fails
/// to parse is a scrape that silently records nothing, and the graph looks like a quiet day rather
/// than like a broken exporter.
/// </remarks>
public class PrometheusTextTests
{
    [Test]
    public async Task AGaugeCarriesItsHelpItsTypeAndItsValue()
    {
        var text = new PrometheusText();

        text.Gauge("mui_gc_heap_bytes", "Managed heap size, in bytes.", 1024);

        await Assert.That(text.ToString()).IsEqualTo(
            """
            # HELP mui_gc_heap_bytes Managed heap size, in bytes.
            # TYPE mui_gc_heap_bytes gauge

            """.ReplaceLineEndings("\n")
            + "mui_gc_heap_bytes 1024\n");
    }

    /// <summary>
    /// A counter is declared as one, because Prometheus reads the type: <c>rate()</c> over something
    /// declared a gauge silently gives the wrong answer rather than refusing.
    /// </summary>
    [Test]
    public async Task ACounterIsDeclaredAsACounter()
    {
        var text = new PrometheusText();

        text.Counter("mui_gc_collections_total", "Collections since start.", 7);

        await Assert.That(text.ToString()).Contains("# TYPE mui_gc_collections_total counter");
    }

    /// <summary>
    /// One HELP and one TYPE line per metric name, however many label sets follow. Repeating them
    /// is a duplicate-metric error in Prometheus, and the whole scrape is rejected — not the one
    /// series.
    /// </summary>
    [Test]
    public async Task ASeriesRepeatedUnderDifferentLabelsDeclaresItselfOnce()
    {
        var text = new PrometheusText();

        text.Counter("mui_gc_collections_total", "Collections since start.", 9, ("generation", "0"));
        text.Counter("mui_gc_collections_total", "Collections since start.", 4, ("generation", "1"));

        var lines = text.ToString().Split('\n');

        await Assert.That(lines.Count(l => l.StartsWith("# HELP mui_gc_collections_total", StringComparison.Ordinal)))
            .IsEqualTo(1);
        await Assert.That(lines.Count(l => l.StartsWith("# TYPE mui_gc_collections_total", StringComparison.Ordinal)))
            .IsEqualTo(1);
        await Assert.That(text.ToString()).Contains("mui_gc_collections_total{generation=\"0\"} 9");
        await Assert.That(text.ToString()).Contains("mui_gc_collections_total{generation=\"1\"} 4");
    }

    /// <summary>
    /// A label value is escaped, because one of ours carries a game's own text one day and a
    /// backslash or a quote in it would produce a line Prometheus rejects along with everything
    /// after it.
    /// </summary>
    [Test]
    public async Task ALabelValueIsEscaped()
    {
        var text = new PrometheusText();

        text.Counter("mui_crawl_outcomes_total", "Outcomes.", 1, ("cause", "said \"no\"\\ever\nagain"));

        await Assert.That(text.ToString())
            .Contains(@"mui_crawl_outcomes_total{cause=""said \""no\""\\ever\nagain""} 1");
    }

    /// <summary>
    /// Values are written invariantly. A German-locale process writing <c>1,5</c> is a scrape
    /// Prometheus rejects, and it would only ever have failed in one deployment.
    /// </summary>
    [Test]
    public async Task AFractionalValueIsWrittenInvariantly()
    {
        var was = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

        try
        {
            var text = new PrometheusText();

            text.Gauge("mui_gc_pause_ratio", "Pause fraction.", 1.5);

            await Assert.That(text.ToString()).Contains("mui_gc_pause_ratio 1.5");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = was;
        }
    }

    /// <summary>The body ends in a newline, which the format requires of its last line.</summary>
    [Test]
    public async Task TheBodyEndsInANewline()
    {
        var text = new PrometheusText();

        text.Gauge("mui_up", "Whether this replica answered.", 1);

        await Assert.That(text.ToString()).EndsWith("\n");
    }
}
