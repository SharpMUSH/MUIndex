using MUI.Catalog.Persistence;

namespace MUI.Catalog.Tests.Persistence.Support;

/// <summary>Rows the persistence tests need before they can say anything.</summary>
internal static class Seed
{
    public static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    public static async Task<Guid> GameAsync(
        TestDatabase db,
        string slug = "corvid",
        string name = "Corvid",
        LifecycleState state = LifecycleState.Active,
        bool isClaimed = false,
        DateTimeOffset? firstSeenAt = null,
        DateTimeOffset? lastReachableAt = null)
    {
        var id = Guid.NewGuid();

        await new NpgsqlGameStore(db.DataSource).InsertAsync(new GameRecord(
            id,
            slug,
            name,
            Tagline: null,
            state,
            isClaimed,
            firstSeenAt ?? Now.AddYears(-2),
            lastReachableAt,
            ArchivedAt: state is LifecycleState.Archived ? Now : null));

        return id;
    }
}
