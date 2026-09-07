using System.Diagnostics;
using System.Text;

namespace MUI.Streaming.Prototype;

public static class RenderBenchmark
{
    private static readonly int[] BatchSizes = [0, 25, 50, 100];
    private const int Iterations = 10;

    public static async Task RunAsync(ListingSnapshot snapshot)
    {
        var experiment = new ListingExperiment(snapshot);
        foreach (var batchSize in BatchSizes)
        {
            for (var warmup = 0; warmup < 3; warmup++)
            {
                await experiment.WriteAsync(batchSize, Discard);
            }
        }

        var expected = await CaptureAsync(experiment, 0);
        foreach (var batchSize in BatchSizes)
        {
            var actual = await CaptureAsync(experiment, batchSize);
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Output differs for batch {batchSize}.");
            }

            var allocatedBefore = GC.GetTotalAllocatedBytes(true);
            var timer = Stopwatch.StartNew();
            double firstWriteMilliseconds = 0;
            for (var iteration = 0; iteration < Iterations; iteration++)
            {
                var request = Stopwatch.StartNew();
                var first = true;
                await experiment.WriteAsync(batchSize, _ =>
                {
                    if (first)
                    {
                        firstWriteMilliseconds += request.Elapsed.TotalMilliseconds;
                        first = false;
                    }
                    return Task.CompletedTask;
                });
            }
            var bytes = (GC.GetTotalAllocatedBytes(true) - allocatedBefore) / Iterations;
            var elapsed = timer.Elapsed.TotalMilliseconds / Iterations;

            // Forced collections are deliberately outside the timing and allocation pass.
            var baseline = GC.GetTotalMemory(true);
            long peak = 0;
            var sampled = new ListingExperiment(snapshot,
                () => peak = Math.Max(peak, GC.GetTotalMemory(true) - baseline));
            await sampled.WriteAsync(batchSize, Discard);
            Console.WriteLine($"batch={batchSize}: allocated={bytes:N0} bytes, total={elapsed:F2} ms, "
                + $"first-write={firstWriteMilliseconds / Iterations:F2} ms, "
                + $"peak-render-live-delta={peak:N0} bytes; exact HTML match");
        }
    }

    private static Task Discard(string _) => Task.CompletedTask;

    private static async Task<string> CaptureAsync(ListingExperiment experiment, int batchSize)
    {
        var buffer = new StringBuilder();
        await experiment.WriteAsync(batchSize, text =>
        {
            buffer.Append(text);
            return Task.CompletedTask;
        });
        return buffer.ToString();
    }
}
