using System.Diagnostics;
using System.Reflection;

using MUI.Catalog;
using MUI.Web.Components.Pages;
using MUI.Web.Fixtures;
using MUI.Web.Tests;

// Synthetic inputs only. Run alone in Release; total allocation includes renderer setup/output.
var now = DateTimeOffset.UtcNow;
var rows = Enumerable.Range(0, 900).Select(i => new GameFacetRow(
    new GameSummary(Guid.NewGuid(), $"game-{i}", $"Game {i}",
        "A synthetic game for allocation measurement", LifecycleState.Active, false,
        i % 100, "PennMUSH 1.8.8", ["MSSP", "GMCP"], now),
    ActivityBand.PlayersNow, LastSeenBand.Day, true, "UTF-8", "English", "PennMUSH 1.8.8",
    "MUSH", i % 2 == 0 ? "Fantasy" : "Science Fiction", false, false, false, GrowthDirection.Up))
    .ToArray();
var catalogue = FacetedSearch.Prepare(rows);
GameFilter[] filters =
[
    new(),
    new()
    {
        Band = ActivityBand.PlayersNow,
        Codebase = FacetChoice.Of("PennMUSH"),
        Language = FacetChoice.Of("English"),
        Genre = FacetChoice.Of("Fantasy"),
    },
];
foreach (var filter in filters)
{
    for (var i = 0; i < 10; i++)
    {
        FacetedSearch.Search(catalogue, filter);
    }
    var before = GC.GetAllocatedBytesForCurrentThread();
    var timer = Stopwatch.StartNew();
    for (var i = 0; i < 100; i++)
    {
        FacetedSearch.Search(catalogue, filter);
    }
    Console.WriteLine($"Facets filtered={filter.Genre is not null}: "
        + $"{(GC.GetAllocatedBytesForCurrentThread() - before) / 100:N0} bytes/request, "
        + $"{timer.Elapsed.TotalMilliseconds / 100:F2} ms/request");
}

var queries = DispatchProxy.Create<IGameQueries, ListingQueries>();
((ListingQueries)(object)queries).Listing = FacetedSearch.Search(catalogue, new());
for (var i = 0; i < 4; i++)
{
    await Render.PageAsync<Games>([], string.Empty, queries: queries);
}
var allocatedBefore = GC.GetTotalAllocatedBytes(true);
var clock = Stopwatch.StartNew();
var html = string.Empty;
for (var i = 0; i < 10; i++)
{
    html = await Render.PageAsync<Games>([], string.Empty, queries: queries);
}
Console.WriteLine($"Render 900 rows: {(GC.GetTotalAllocatedBytes(true) - allocatedBefore) / 10:N0} "
    + $"bytes/request, {clock.Elapsed.TotalMilliseconds / 10:F2} ms/request, {html.Length:N0} characters");

public class ListingQueries : DispatchProxy
{
    private readonly FixtureGameQueries _fixture = new();
    public GameListing Listing { get; set; } = GameListing.Empty;

    protected override object? Invoke(MethodInfo? method, object?[]? args) =>
        method?.Name == nameof(IGameQueries.SearchAsync)
            ? Task.FromResult(Listing)
            : method!.Invoke(_fixture, args);
}
