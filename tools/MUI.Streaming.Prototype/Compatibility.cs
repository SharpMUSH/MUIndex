using System.Text;

using MUI.Catalog;
using MUI.Web.Localization;

namespace MUI.Streaming.Prototype;

public static class Compatibility
{
    public static async Task VerifyAsync(GameFacetRow[] source)
    {
        var varied = source.Select((row, i) => row with
        {
            Summary = row.Summary with
            {
                Name = i % 3 == 0 ? $"<Game & \"{i}\">" : i % 3 == 1 ? $"北大侠客行 {i}" : $"لعبة {i}",
                PlayersNow = i % 7 == 0 ? null : i % 100,
                HasIcon = i % 2 == 0,
                FirstSeenAt = row.Summary.LastReachableAt,
                Growth = i % 2 == 0 ? GrowthDirection.Up : GrowthDirection.Down,
                GrowthPlayers = i % 2 == 0 ? 4 : -3,
            },
        }).ToArray();
        var cases = 0;
        foreach (var tag in new[] { "en", "de", "zh-Hans" })
        foreach (var query in new[] { "", "?sort=name", "?genre=Other", "?plain=true", "?sort=invalid" })
        {
            var context = new DefaultHttpContext();
            context.Items[LocaleRouting.ItemKey] = new LocaleContext(Locales.Find(tag)!, FromPath: tag != "en");
            var experiment = new ListingExperiment(ListingSnapshot.FromFixture(varied, query, context));
            var expected = await Capture(experiment, 0);
            foreach (var batch in new[] { 1, 25, 50, 100, 900 })
            {
                var actual = await Capture(experiment, batch);
                if (!string.Equals(expected, actual, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"HTML mismatch: {tag}, {query}, batch={batch}");
                }
                cases++;
            }
        }

        var renders = 0;
        var stream = new ListingExperiment(ListingSnapshot.FromFixture(source), () => renders++);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var writes = 0;
        var pending = stream.WriteAsync(50, async _ =>
        {
            writes++;
            entered.TrySetResult();
            await release.Task;
        }, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            if (pending.IsCompleted || renders != 1)
            {
                throw new InvalidOperationException("Rows rendered before the initial write completed.");
            }
            cancellation.Cancel();
        }
        finally
        {
            release.TrySetResult();
        }
        try
        {
            await pending;
            throw new InvalidOperationException("Cancellation was ignored.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        if (writes != 1)
        {
            throw new InvalidOperationException("Rows were written after cancellation.");
        }
        Console.WriteLine($"Compatibility: {cases} exact HTML comparisons; backpressure and cancellation passed.");
    }

    private static async Task<string> Capture(ListingExperiment experiment, int batch)
    {
        var text = new StringBuilder();
        await experiment.WriteAsync(batch, part => { text.Append(part); return Task.CompletedTask; });
        return text.ToString();
    }
}
