namespace MUI.Catalog.Tests.Search;

public class FacetAllocationTests
{
    [Test]
    public async Task RepeatedSelectionsDoNotAllocateMegabytesPerRequest()
    {
        var now = DateTimeOffset.UtcNow;
        var rows = Enumerable.Range(0, 900).Select(i => new GameFacetRow(
            new GameSummary(Guid.NewGuid(), $"game-{i}", $"Game {i}", null,
                LifecycleState.Active, false, 1, "PennMUSH 1.8.8", ["MSSP"], now),
            ActivityBand.PlayersNow, LastSeenBand.Day, true, "UTF-8", "English",
            "PennMUSH 1.8.8", "MUSH", "Fantasy", false, false, false, GrowthDirection.Up)).ToArray();
        var filter = new GameFilter
        {
            Band = ActivityBand.PlayersNow,
            Codebase = FacetChoice.Of("PennMUSH"),
            Language = FacetChoice.Of("English"),
            Genre = FacetChoice.Of("Fantasy"),
        };
        var catalogue = FacetedSearch.Prepare(rows);
        for (var i = 0; i < 3; i++)
        {
            FacetedSearch.Search(catalogue, filter);
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        var listing = FacetedSearch.Search(catalogue, filter);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        await Assert.That(listing.Games.Count).IsEqualTo(900);
        await Assert.That(allocated).IsLessThan(1_000_000L);

        // Reusing the snapshot must not retain selections or counts from another request.
        var empty = FacetedSearch.Search(catalogue, filter with { Genre = FacetChoice.Of("Other") });
        await Assert.That(empty.Games.Count).IsEqualTo(0);
        await Assert.That(empty.Facets.Single(f => f.Key == FacetKeys.Genre)
            .Values.Single(v => v.Token == "Fantasy").Count).IsEqualTo(900);
        await Assert.That(FacetedSearch.Search(catalogue, filter).Games.Count).IsEqualTo(900);
    }
}
