using System.Diagnostics;
using System.Runtime;

namespace MUI.Web.Diagnostics;

/// <summary>
/// What the garbage collector and the process will say about themselves, at scrape time.
/// </summary>
/// <remarks>
/// <para>
/// This exists because of a specific dead end. On 2026-09-03 both web replicas were climbing, and
/// every measurement available — <c>container_memory_rss</c>, <c>working_set</c>, <c>smaps_rollup</c>
/// — agreed that they were and none of them could say why: from outside the process a growing live
/// set, a collector that has not been pressed into returning what it holds, and a fragmented heap
/// are the same number. The three readings that tell them apart are here, and nowhere else.
/// </para>
/// <para>
/// <b>Heap size, committed and fragmented are three different questions and all three are reported.</b>
/// The live set is what is reachable; committed is what the process has taken from the operating
/// system and is the figure a container limit is compared against; fragmented is the part of the
/// committed heap that is free but not returnable. A leak raises the first. Budget growth raises the
/// second while the first stays flat. Fragmentation raises the third.
/// </para>
/// </remarks>
public static class RuntimeMetrics
{
    public static void WriteTo(PrometheusText text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // One snapshot for every reading below, rather than one call per metric: the collector may
        // run between two calls, and a scrape whose generations came from different heaps would show
        // arithmetic that never held at any instant.
        var info = GC.GetGCMemoryInfo();

        text.Gauge(
            "mui_gc_heap_size_bytes",
            "Bytes on the managed heap the last collection found reachable.",
            info.HeapSizeBytes);

        text.Gauge(
            "mui_gc_committed_bytes",
            "Bytes the managed heap has committed from the operating system. This is the figure a "
            + "container memory limit is compared against, and it is not the live set.",
            info.TotalCommittedBytes);

        text.Gauge(
            "mui_gc_fragmented_bytes",
            "Bytes inside the committed heap that are free but not returnable.",
            info.FragmentedBytes);

        text.Gauge(
            "mui_gc_heap_limit_bytes",
            "The ceiling the collector believes it has, which in a container is derived from the "
            + "cgroup limit rather than from the host's memory.",
            info.TotalAvailableMemoryBytes);

        // Per generation, from the same snapshot. Generation 2 growing while 0 and 1 stay flat is a
        // live set that is genuinely growing; the reverse is ordinary churn.
        var generations = info.GenerationInfo;

        for (var i = 0; i < generations.Length; i++)
        {
            text.Gauge(
                "mui_gc_heap_bytes",
                "Bytes on the managed heap, by generation.",
                generations[i].SizeAfterBytes,
                ("generation", NameOf(i)));
        }

        for (var generation = 0; generation <= GC.MaxGeneration; generation++)
        {
            text.Counter(
                "mui_gc_collections_total",
                "Collections since the process started, by generation.",
                GC.CollectionCount(generation),
                ("generation", generation.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        text.Counter(
            "mui_gc_allocated_bytes_total",
            "Bytes allocated since the process started, whether or not they were collected. Read as "
            + "a rate, this is allocation pressure, which is a different question from how much is "
            + "being retained.",
            GC.GetTotalAllocatedBytes(precise: false));

        text.Counter(
            "mui_gc_pause_seconds_total",
            "Time spent paused in the collector since the process started.",
            GC.GetTotalPauseDuration().TotalSeconds);

        // How the numbers above should be read at all. Server GC sizes its budgets per core and
        // against the container's limit, so the same heap graph means different things under the two
        // modes, and a reader who does not know which one is running can misread the whole series.
        text.Gauge(
            "mui_gc_server_mode",
            "1 when this process runs Server GC, 0 for Workstation.",
            GCSettings.IsServerGC ? 1 : 0);

        // The core count rather than the heap count, and named for what it is. Server GC allocates
        // one heap per core, so this is what the heap count follows — but the runtime exposes no
        // public reading of the count itself, and a gauge called `mui_gc_heap_count` carrying
        // something else would mislead the one reader it exists for.
        //
        // Not `_count` either: promtool reserves that suffix for a histogram's own component, and
        // a plain gauge wearing it parses today and collides with that meaning the day somebody
        // adds the histogram. MetricNamingTests holds the rule.
        text.Gauge(
            "mui_process_cpus",
            "Cores this process can see. Server GC allocates a heap per core, so this is what its "
            + "budget count follows.",
            Environment.ProcessorCount);

        using var process = Process.GetCurrentProcess();

        text.Gauge(
            "mui_process_working_set_bytes",
            "This process's resident set, so one scrape carries both this and the managed heap — the "
            + "comparison a cgroup metric can only ever give one side of.",
            process.WorkingSet64);

        text.Gauge(
            "mui_process_threads",
            "Operating system threads in this process. Each one commits a stack, so a climbing count "
            + "is memory growth that never appears on the managed heap at all.",
            process.Threads.Count);
    }

    /// <summary>
    /// <see cref="GCMemoryInfo.GenerationInfo"/> is indexed by generation, with the large and pinned
    /// object heaps on the end. They are named rather than numbered because "generation 3" is not
    /// what the LOH is, and a reader of the graph should not have to know the ordering to know that.
    /// </summary>
    private static string NameOf(int index) => index switch
    {
        3 => "loh",
        4 => "poh",
        _ => index.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
