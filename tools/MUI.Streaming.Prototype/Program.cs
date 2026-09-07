using MUI.Streaming.Prototype;

// Benchmark inputs are repeatable; the preview still captures a fresh timestamp per request.
var fixtureTime = args.Contains("--serve")
    ? DateTimeOffset.UtcNow
    : new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
var rows = FixtureCatalogue.Create(fixtureTime);
if (args.Contains("--serve"))
{
    await PreviewServer.RunAsync(rows);
    return;
}

await Compatibility.VerifyAsync(rows, fixtureTime);
await RenderBenchmark.RunAsync(ListingSnapshot.FromFixture(rows, now: fixtureTime));
