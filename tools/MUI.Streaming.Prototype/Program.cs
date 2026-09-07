using System.Diagnostics;
using System.Reflection;
using System.Text;

using MUI.Catalog;
using MUI.Streaming.Prototype;
using MUI.Web.Components.Pages;
using MUI.Web.Api;
using MUI.Web.Fixtures;
using MUI.Web.Localization;
using MUI.Web.Tests;

var now = DateTimeOffset.UtcNow;
var rows = Enumerable.Range(0, 900).Select(i => new GameFacetRow(
    new GameSummary(Guid.NewGuid(), $"game-{i}", $"Game {i}", "Synthetic streaming fixture",
        LifecycleState.Active, false, i % 100, "PennMUSH 1.8.8", ["MSSP", "GMCP"], now),
    ActivityBand.PlayersNow, LastSeenBand.Day, true, "UTF-8", "English", "PennMUSH 1.8.8",
    "MUSH", "Fantasy", false, false, false, GrowthDirection.Up)).ToArray();
var queries = DispatchProxy.Create<IGameQueries, SnapshotQueries>();
((SnapshotQueries)(object)queries).Listing = FacetedSearch.Search(rows, new());
var experiment = new ListingExperiment(queries, clock: new SnapshotClock(now));

if (args.Contains("--serve"))
{
    var builder = WebApplication.CreateSlimBuilder();
    builder.WebHost.UseUrls("http://127.0.0.1:5187");
    var app = builder.Build();
    app.MapGet("/{mode}", async (string mode, HttpContext http) =>
    {
        if (mode is not ("buffered" or "streamed")) { http.Response.StatusCode = 404; return; }
        var batch = int.TryParse(http.Request.Query["batch"], out var value) ? value : 50;
        if (batch is < 1 or > 900) { http.Response.StatusCode = 400; return; }
        http.Response.ContentType = "text/html; charset=utf-8";
        http.Response.Headers.CacheControl = "no-store";
        await new ListingExperiment(queries).WriteAsync(mode == "buffered" ? 0 : batch, async text =>
        {
            await http.Response.WriteAsync(text, http.RequestAborted);
            await http.Response.Body.FlushAsync(http.RequestAborted);
        }, http.RequestAborted);
    });
    await app.RunAsync();
    return;
}

await Compatibility.VerifyAsync(rows);

foreach (var batch in new[] { 0, 25, 50, 100 })
{
    for (var i = 0; i < 3; i++) await experiment.WriteAsync(batch, _ => Task.CompletedTask);
}
var expected = new StringBuilder();
await experiment.WriteAsync(0, text => { expected.Append(text); return Task.CompletedTask; });
foreach (var batch in new[] { 0, 25, 50, 100 })
{
    var actual = new StringBuilder();
    await experiment.WriteAsync(batch, text => { actual.Append(text); return Task.CompletedTask; });
    if (actual.ToString() != expected.ToString())
    {
        File.WriteAllText($"/tmp/mui-stream-{batch}.html", actual.ToString());
        File.WriteAllText("/tmp/mui-stream-expected.html", expected.ToString());
        throw new InvalidOperationException($"Output differs for batch {batch}.");
    }
    var allocated = GC.GetTotalAllocatedBytes(true);
    var timer = Stopwatch.StartNew();
    double first = 0;
    for (var i = 0; i < 10; i++)
    {
        var request = Stopwatch.StartNew();
        var firstWrite = true;
        await experiment.WriteAsync(batch, _ =>
        {
            if (firstWrite) { first += request.Elapsed.TotalMilliseconds; firstWrite = false; }
            return Task.CompletedTask;
        });
    }
    var bytes = (GC.GetTotalAllocatedBytes(true) - allocated) / 10;
    var elapsed = timer.Elapsed.TotalMilliseconds / 10;
    // Separate forced-GC pass: do not mix these pauses into latency/allocation timings.
    var probe = new RenderProbe(GC.GetTotalMemory(true));
    RenderProbe.Current.Value = probe;
    await experiment.WriteAsync(batch, _ => Task.CompletedTask);
    RenderProbe.Current.Value = null;
    Console.WriteLine($"batch={batch}: allocated={bytes:N0} bytes, total={elapsed:F2} ms, "
        + $"first-write={first / 10:F2} ms, peak-render-live-delta={probe.Peak:N0} bytes; exact HTML match");
}
