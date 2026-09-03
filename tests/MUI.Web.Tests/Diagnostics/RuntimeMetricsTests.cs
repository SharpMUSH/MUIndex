using MUI.Web.Diagnostics;

namespace MUI.Web.Tests.Diagnostics;

/// <summary>
/// The GC numbers, which are the whole reason this endpoint exists.
/// </summary>
/// <remarks>
/// A container's resident memory cannot distinguish a growing live set from a heap the collector has
/// simply not been pressed into returning, and on 2026-09-03 that ambiguity was the entire
/// investigation: every outside measurement agreed the process was climbing and none of them could
/// say why. These are the readings that separate the two, so what they must prove is that each one
/// is the runtime's own figure and not a plausible-looking constant.
/// </remarks>
public class RuntimeMetricsTests
{
    private static string Scrape()
    {
        var text = new PrometheusText();
        RuntimeMetrics.WriteTo(text);
        return text.ToString();
    }

    [Test]
    public async Task TheHeapIsReportedByGeneration()
    {
        var scrape = Scrape();

        foreach (var generation in new[] { "0", "1", "2", "loh", "poh" })
        {
            await Assert.That(scrape).Contains($"mui_gc_heap_bytes{{generation=\"{generation}\"}}");
        }
    }

    /// <summary>
    /// Committed is the number the container's own limit is compared against, and it is not the heap
    /// size: the gap between them is what "the GC has not given it back" looks like from inside.
    /// </summary>
    [Test]
    public async Task CommittedAndFragmentedAreReportedApartFromTheLiveHeap()
    {
        var scrape = Scrape();

        await Assert.That(scrape).Contains("mui_gc_committed_bytes ");
        await Assert.That(scrape).Contains("mui_gc_fragmented_bytes ");
        await Assert.That(scrape).Contains("mui_gc_heap_size_bytes ");
    }

    /// <summary>
    /// The allocation counter has to be the process's real running total, because the whole quarrel
    /// this endpoint settles is between allocation rate and retention.
    /// </summary>
    [Test]
    public async Task TheAllocationTotalRisesWhenSomethingIsAllocated()
    {
        var before = Read(Scrape(), "mui_gc_allocated_bytes_total");

        // Kept alive to the end, so the allocation cannot be optimised away.
        var ballast = new byte[8 * 1024 * 1024];
        ballast[^1] = 1;

        var after = Read(Scrape(), "mui_gc_allocated_bytes_total");

        await Assert.That(after).IsGreaterThan(before);
        await Assert.That(ballast[^1]).IsEqualTo((byte)1);
    }

    [Test]
    public async Task CollectionsAreCountedPerGeneration()
    {
        var before = Read(Scrape(), "mui_gc_collections_total", ("generation", "0"));

        GC.Collect(0, GCCollectionMode.Forced, blocking: true);

        var after = Read(Scrape(), "mui_gc_collections_total", ("generation", "0"));

        await Assert.That(after).IsGreaterThan(before);
    }

    /// <summary>
    /// Whether this process is running Server GC, which decides how its budgets are sized and so how
    /// a graph of its memory should be read at all.
    /// </summary>
    [Test]
    public async Task TheCollectorSaysWhichModeItIsIn()
    {
        var scrape = Scrape();

        await Assert.That(scrape).Contains("mui_gc_server_mode ");

        // The core count, not a heap count: the runtime publishes no reading of the latter, and
        // Server GC's budgets follow the former.
        await Assert.That(Read(scrape, "mui_process_cpus"))
            .IsEqualTo(Environment.ProcessorCount);
    }

    /// <summary>
    /// The process's own resident set, so one scrape carries both halves of the comparison that
    /// cgroup metrics could only ever give one side of.
    /// </summary>
    [Test]
    public async Task TheProcessWorkingSetIsReportedBesideTheHeap()
    {
        var scrape = Scrape();

        await Assert.That(scrape).Contains("mui_process_working_set_bytes ");
        await Assert.That(Read(scrape, "mui_process_working_set_bytes")).IsGreaterThan(0);
    }

    /// <summary>Reads one series' value out of a scrape, so a test asserts on the number.</summary>
    internal static double Read(string scrape, string name, params (string Key, string Value)[] labels)
    {
        var prefix = labels.Length == 0
            ? name + " "
            : name + "{" + string.Join(",", labels.Select(l => $"{l.Key}=\"{l.Value}\"")) + "} ";

        var line = scrape.Split('\n').FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"No series '{prefix.TrimEnd()}' in:\n{scrape}");

        return double.Parse(
            line[prefix.Length..], System.Globalization.CultureInfo.InvariantCulture);
    }
}
