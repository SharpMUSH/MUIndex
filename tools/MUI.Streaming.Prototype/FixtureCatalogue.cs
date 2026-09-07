using MUI.Catalog;

namespace MUI.Streaming.Prototype;

public static class FixtureCatalogue
{
    public static GameFacetRow[] Create(DateTimeOffset now)
    {
        return Enumerable.Range(0, 900).Select(i => new GameFacetRow(
            new GameSummary(Guid.NewGuid(), $"game-{i}", $"Game {i}", "Synthetic streaming fixture",
                LifecycleState.Active, false, i % 100, "PennMUSH 1.8.8", ["MSSP", "GMCP"], now),
            ActivityBand.PlayersNow, LastSeenBand.Day, true, "UTF-8", "English", "PennMUSH 1.8.8",
            "MUSH", "Fantasy", false, false, false, GrowthDirection.Up)).ToArray();
    }
}
