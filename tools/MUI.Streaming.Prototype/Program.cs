using MUI.Streaming.Prototype;

var rows = FixtureCatalogue.Create();
if (args.Contains("--serve"))
{
    await PreviewServer.RunAsync(rows);
    return;
}

await Compatibility.VerifyAsync(rows);
await RenderBenchmark.RunAsync(ListingSnapshot.FromFixture(rows));
