using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

using ZiggyCreatures.Caching.Fusion;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// The listing serves an assembled catalogue, not a query per request — and how a deliberate edit
/// gets onto it without waiting.
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
/// What is pinned here is ours: that the rows are cached at all, that the key separates the two
/// things which change them, that a staff edit clears it, and that concurrent readers on a cold key
/// share one assembly. That entries expire on their duration is FusionCache's behaviour and is not
/// re-tested here — a test that slept a minute to watch a library's own clock would buy nothing.
/// </para>
/// <para>
/// Everything uses one <see cref="NpgsqlGameQueries"/> instance on purpose — the cache is per
/// instance unless one is supplied, and the deployment supplies the container's, which is the same
/// instance <see cref="ListingCache"/> clears.
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

        var queries = new NpgsqlGameQueries(db.DataSource, time: new FixedClock(Now));

        await Assert.That((await queries.SearchAsync(new GameFilter())).Games).Count().IsEqualTo(1);

        // Written after the catalogue was assembled, and therefore not on it. Reading the *absence*
        // of this game is the only way to prove the second listing did not go back to the database.
        await Seed.GameAsync(db, "magpie", "Magpie", lastReachableAt: Now);

        await Assert.That((await queries.SearchAsync(new GameFilter())).Games).Count().IsEqualTo(1);
    }

    /// <summary>
    /// A staff edit does not wait the duration out.
    /// </summary>
    /// <remarks>
    /// The duration is the right answer for measurement, which arrives continuously and is never
    /// urgent. It is the wrong answer for a rename somebody just performed and is looking at the page
    /// to confirm. <c>game_field_set</c>, <c>game_rename</c> and <c>game_merge</c> each call this
    /// after they write; the cache and the queries have to be the *same* instance for it to reach
    /// anything, which is what this asserts by construction.
    /// </remarks>
    [Test]
    public async Task InvalidatingTheListingPutsAWriteOnTheVeryNextRead()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        await Seed.GameAsync(db, "corvid", "Corvid", lastReachableAt: Now);

        var cache = new FusionCache(new FusionCacheOptions());
        var queries = new NpgsqlGameQueries(db.DataSource, time: new FixedClock(Now), cache: cache);
        var listing = new ListingCache(cache);

        await Assert.That((await queries.SearchAsync(new GameFilter())).Games).Count().IsEqualTo(1);

        await Seed.GameAsync(db, "magpie", "Magpie", lastReachableAt: Now);
        await listing.InvalidateAsync();

        await Assert.That((await queries.SearchAsync(new GameFilter())).Games).Count().IsEqualTo(2);
    }

    /// <summary>
    /// The tag reaches every catalogue entry, not just the one the last reader happened to fill.
    /// </summary>
    /// <remarks>
    /// There is an entry per (archive toggle, sort window) pair, and a rename can change what any of
    /// them contains. Invalidating by tag rather than by key is what makes the caller not have to
    /// know how many are live.
    /// </remarks>
    [Test]
    public async Task InvalidatingClearsEveryCatalogueTheListingKeeps()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        await Seed.GameAsync(db, "corvid", "Corvid", lastReachableAt: Now);
        await Seed.GameAsync(db, "gone", "Gone", state: LifecycleState.Archived);

        var cache = new FusionCache(new FusionCacheOptions());
        var queries = new NpgsqlGameQueries(db.DataSource, time: new FixedClock(Now), cache: cache);
        var listing = new ListingCache(cache);

        // Two different keys, both now filled.
        await queries.SearchAsync(new GameFilter());
        await queries.SearchAsync(new GameFilter { IncludeArchived = true });

        await Seed.GameAsync(db, "magpie", "Magpie", lastReachableAt: Now);
        await listing.InvalidateAsync();

        await Assert.That((await queries.SearchAsync(new GameFilter())).Games).Count().IsEqualTo(2);

        await Assert.That((await queries.SearchAsync(new GameFilter { IncludeArchived = true })).Games)
            .Count().IsEqualTo(3);
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

        var queries = new NpgsqlGameQueries(db.DataSource, time: new FixedClock(Now));

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

        var queries = new NpgsqlGameQueries(db.DataSource, time: new FixedClock(Now));

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
    /// This is the property the cache exists for and the reason the implementation is FusionCache
    /// rather than a dictionary: its request coalescing runs one factory per key however many callers
    /// arrive together. A cache that let every reader miss at once on a cold key would leave the worst
    /// case exactly where it was. Proven by result identity — the rows are assembled once and shared,
    /// so every caller gets the same <see cref="GameSummary"/> instances, which cannot happen if each
    /// built its own.
    /// </remarks>
    [Test]
    public async Task ConcurrentReadersOfAColdCatalogueShareOneAssembly()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        await Seed.GameAsync(db, "corvid", "Corvid", lastReachableAt: Now);

        var queries = new NpgsqlGameQueries(db.DataSource, time: new FixedClock(Now));

        var listings = await Task.WhenAll(
            Enumerable.Range(0, 16).Select(_ => queries.SearchAsync(new GameFilter())));

        var first = listings[0].Games.Single();

        await Assert.That(listings.All(l => ReferenceEquals(l.Games.Single(), first))).IsTrue();
    }

    private sealed class FixedClock(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }
}
