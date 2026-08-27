using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// The listing serves an assembled catalogue, not a query per request — and what that costs.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="NpgsqlGameQueries.SearchAsync"/> assembles the whole catalogue as facet rows and then
/// lets <see cref="FacetedSearch"/> do every facet in memory, so the expensive half does not vary
/// with the filter. That is what makes it cacheable, and caching it is what stops a listing whose
/// URL space is the product of a dozen combinable facets from costing seven catalogue-wide queries
/// per distinct query string.
/// </para>
/// <para>
/// The cost is staleness, and these tests are where it is written down rather than discovered: a
/// row written now is not on the listing until the snapshot ages out. Everything here uses one
/// <see cref="NpgsqlGameQueries"/> instance on purpose — the cache is per instance, and the instance
/// is a singleton in both compositions, so the test and the deployment agree.
/// </para>
/// </remarks>
public class CatalogueCachePostgresTests
{
    private static readonly DateTimeOffset Now = Seed.Now;

    [Test]
    public async Task ASecondListingIsAnsweredFromTheFirstOnesRowsRatherThanTheDatabase()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        await Seed.GameAsync(db, "corvid", "Corvid", lastReachableAt: Now);

        var queries = new NpgsqlGameQueries(db.DataSource, time: new SettableClock(Now));

        await Assert.That((await queries.SearchAsync(new GameFilter())).Games).Count().IsEqualTo(1);

        // Written after the snapshot was taken, and therefore not on it. Reading the *absence* of
        // this game is the only way to prove the second listing did not go back to the database.
        await Seed.GameAsync(db, "magpie", "Magpie", lastReachableAt: Now);

        await Assert.That((await queries.SearchAsync(new GameFilter())).Games).Count().IsEqualTo(1);
    }

    [Test]
    public async Task TheCatalogueIsAssembledAgainOnceTheSnapshotIsNoLongerFresh()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        await Seed.GameAsync(db, "corvid", "Corvid", lastReachableAt: Now);

        var clock = new SettableClock(Now);
        var queries = new NpgsqlGameQueries(db.DataSource, time: clock);

        await Assert.That((await queries.SearchAsync(new GameFilter())).Games).Count().IsEqualTo(1);

        await Seed.GameAsync(db, "magpie", "Magpie", lastReachableAt: Now);
        clock.Now = Now.AddMinutes(5);

        await Assert.That((await queries.SearchAsync(new GameFilter())).Games).Count().IsEqualTo(2);
    }

    /// <summary>
    /// A window sort is the one filter that adds a query, so it has to be part of the key.
    /// </summary>
    /// <remarks>
    /// Were it not, a listing sorted by <c>peakWeek</c> would be served the rows assembled for an
    /// unsorted one — which carry no window figures at all — and would silently rank nothing.
    /// </remarks>
    [Test]
    public async Task AWindowSortIsNotServedTheRowsAssembledForASortThatReadsNoWindow()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var corvid = await Seed.GameAsync(db, "corvid", "Corvid", lastReachableAt: Now);

        var samples = new NpgsqlPresenceStore(db.DataSource);
        await samples.EnsurePartitionAsync(Now);
        await samples.EnsurePartitionAsync(Now.AddMonths(-1));

        for (var hour = 1; hour <= SortWindows.MinimumSamples; hour++)
        {
            await samples.AppendAsync(PresenceSample.Counted(
                corvid, Now.AddHours(-hour), 7, FieldSource.Who));
        }

        var queries = new NpgsqlGameQueries(db.DataSource, time: new SettableClock(Now));

        // Assembled first and cached, with no window figures on its rows.
        await queries.SearchAsync(new GameFilter { Sort = GameSort.Name });

        var ranked = await queries.SearchAsync(new GameFilter { Sort = GameSort.PeakWeek });

        await Assert.That(ranked.Games.Single().PlayersOverWindow).IsNotNull();
    }

    /// <summary>
    /// The archive toggle is the one predicate the database still applies, so it keys too.
    /// </summary>
    [Test]
    public async Task TheArchivedListingIsNotServedTheRowsAssembledForTheDefaultOne()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        await Seed.GameAsync(db, "corvid", "Corvid", lastReachableAt: Now);
        await Seed.GameAsync(db, "gone", "Gone", state: LifecycleState.Archived);

        var queries = new NpgsqlGameQueries(db.DataSource, time: new SettableClock(Now));

        await Assert.That((await queries.SearchAsync(new GameFilter())).Games).Count().IsEqualTo(1);

        await Assert.That(
            (await queries.SearchAsync(new GameFilter { IncludeArchived = true })).Games
                .Select(g => g.Slug).ToList())
            .IsEquivalentTo(new[] { "corvid", "gone" });
    }

    /// <summary>
    /// Everyone arriving on a cold key waits for one assembly rather than starting one each.
    /// </summary>
    /// <remarks>
    /// The point of the cache is to survive many simultaneous readers of distinct URLs; a cache that
    /// let every one of them miss together on a cold key would leave the worst case exactly where it
    /// was. Proven by result identity: the rows are assembled once and shared, so every caller gets
    /// the same <see cref="GameSummary"/> instances, which cannot happen if each built its own.
    /// </remarks>
    [Test]
    public async Task ConcurrentReadersOfAColdCatalogueShareOneAssembly()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        await Seed.GameAsync(db, "corvid", "Corvid", lastReachableAt: Now);

        var queries = new NpgsqlGameQueries(db.DataSource, time: new SettableClock(Now));

        var listings = await Task.WhenAll(
            Enumerable.Range(0, 16).Select(_ => queries.SearchAsync(new GameFilter())));

        var first = listings[0].Games.Single();

        await Assert.That(listings.All(l => ReferenceEquals(l.Games.Single(), first))).IsTrue();
    }

    private sealed class SettableClock(DateTimeOffset at) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = at;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
