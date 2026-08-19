using Dapper;

using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// A submitted game stays off every public surface until it is claimed (spec §8, migration 0010).
/// </summary>
/// <remarks>
/// The rule is one sentence — a game is public if nobody submitted it, or if it has been claimed —
/// but it has to hold across the listing, faceted search, all three liveness feeds, both rankings,
/// the ecosystem dashboard and both lookups. The first cut of the filter missed every query that
/// reaches <c>game</c> through <c>JOIN game g</c> rather than naming it directly, so a submitted game
/// stayed off the listing but appeared in the rankings — exactly what a per-surface test would miss.
/// This seeds a submitted game with data on every series the site reads and checks each surface.
/// </remarks>
public class SubmittedGameTests
{
    private static readonly DateTimeOffset Now = Seed.Now;

    [Test]
    public async Task AnUnclaimedSubmissionIsOnNoPublicSurface()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var queries = new NpgsqlGameQueries(db.DataSource) { Clock = () => Now };

        var found = await Seed.GameAsync(
            db, slug: "found", name: "Found By Us", firstSeenAt: Now.AddHours(-2));
        var submitted = await SubmittedAsync(db, slug: "submitted", name: "Somebody Said So");

        await FurnishAsync(db, found);
        await FurnishAsync(db, submitted);

        var listed = await queries.ListAsync(new GameFilter { IncludeArchived = true });
        var searched = await queries.SearchAsync(new GameFilter { IncludeArchived = true });

        await Assert.That(listed.Select(g => g.Id)).Contains(found);
        await Assert.That(listed.Select(g => g.Id)).DoesNotContain(submitted);
        await Assert.That(searched.Games.Select(g => g.Id)).DoesNotContain(submitted);

        // Not reachable by guessing its address either, on either lookup.
        await Assert.That(await queries.FindAsync("submitted")).IsNull();
        await Assert.That(await queries.FindByIdAsync(submitted)).IsNull();

        // A submission is not "newly discovered" — nothing discovered it. Nor did it go dark or come
        // back, which are the two feeds that reach the game table through a join.
        var feeds = await queries.FeedsAsync();

        await Assert.That(feeds.NewlyDiscovered.Select(e => e.Slug)).DoesNotContain("submitted");
        await Assert.That(feeds.WentDark.Select(e => e.Slug)).DoesNotContain("submitted");
        await Assert.That(feeds.CameBack.Select(e => e.Slug)).DoesNotContain("submitted");
        await Assert.That(feeds.NewlyDiscovered.Select(e => e.Slug)).Contains("found");

        // Neither ranking, which is where the first cut of this filter leaked. The submitted game
        // carries more counted samples than the listed one, so a missing filter cannot come back
        // green by luck of the ordering.
        var rankings = await queries.RankingsAsync();

        await Assert.That(rankings.Busiest.Select(g => g.Slug)).DoesNotContain("submitted");
        await Assert.That(rankings.LongestUnbroken.Select(g => g.Slug)).DoesNotContain("submitted");
        await Assert.That(rankings.ListedGames).IsEqualTo(1);

        // And not in the denominator of any published share, which would let its existence be
        // inferred from arithmetic even while its page is hidden.
        var ecosystem = await queries.EcosystemAsync();

        await Assert.That(ecosystem.ListedGames).IsEqualTo(listed.Count);
        await Assert.That(ecosystem.Handshakes).IsEqualTo(1);
        await Assert.That(ecosystem.MsspReports).IsEqualTo(1);
        await Assert.That(ecosystem.Codebases.Identified).IsEqualTo(1);
    }

    /// <summary>Claiming it lists it, by the same rule and with nothing else changed.</summary>
    [Test]
    public async Task ClaimingASubmissionListsIt()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var queries = new NpgsqlGameQueries(db.DataSource);
        var submitted = await SubmittedAsync(db, slug: "submitted", name: "Somebody Said So");

        await Assert.That(await queries.FindAsync("submitted")).IsNull();

        await new NpgsqlGameStore(db.DataSource).SetClaimedAsync(submitted, true);

        await Assert.That(await queries.FindAsync("submitted")).IsNotNull();
        await Assert.That((await queries.ListAsync(new GameFilter())).Select(g => g.Id))
            .Contains(submitted);
    }

    /// <summary>
    /// A game the crawler found for itself is listed on sight, claimed or not.
    /// </summary>
    /// <remarks>
    /// §7.1's auto-listing must not break: reading the new column as "unclaimed games are hidden"
    /// rather than "unclaimed <em>submissions</em> are hidden" would vanish the whole catalogue,
    /// since every game found so far is unclaimed.
    /// </remarks>
    [Test]
    public async Task AnUnclaimedGameTheCrawlerFoundIsStillListed()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var queries = new NpgsqlGameQueries(db.DataSource);
        var found = await Seed.GameAsync(db, slug: "found", name: "Found By Us");

        await Assert.That((await queries.ListAsync(new GameFilter())).Select(g => g.Id))
            .Contains(found);
        await Assert.That(await queries.FindAsync("found")).IsNotNull();
    }

    /// <summary>The marker survives a round trip through the store rather than being write-only.</summary>
    [Test]
    public async Task TheMarkerIsStoredAndReadBack()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var store = new NpgsqlGameStore(db.DataSource);
        var id = Guid.CreateVersion7();

        await store.InsertAsync(new GameRecord(
            id, "submitted", "Somebody Said So", null, LifecycleState.Active, false, Now,
            SubmittedAt: Now));

        var read = await store.ByIdAsync(id);

        await Assert.That(read!.SubmittedAt).IsEqualTo(Now);
        await Assert.That((await store.BySlugAsync("submitted"))!.SubmittedAt).IsEqualTo(Now);
    }

    /// <summary>
    /// Every read on <see cref="IGameQueries"/> is covered above, by name.
    /// </summary>
    /// <remarks>
    /// The list is written out so that adding a query fails this test rather than silently shipping
    /// another surface nobody filtered. If a new member genuinely needs no filtering, add it here
    /// with the reason — the point is that the decision is made, not that the list is long.
    /// </remarks>
    [Test]
    public async Task EveryQueryOnTheInterfaceHasBeenConsidered()
    {
        var covered = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(IGameQueries.ListAsync),
            nameof(IGameQueries.SearchAsync),
            nameof(IGameQueries.FeedsAsync),
            nameof(IGameQueries.FindAsync),
            nameof(IGameQueries.FindByIdAsync),
            nameof(IGameQueries.EcosystemAsync),
            nameof(IGameQueries.RankingsAsync),
        };

        var declared = typeof(IGameQueries).GetMethods().Select(m => m.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var member in declared)
        {
            await Assert.That(covered.Contains(member))
                .IsTrue()
                .Because($"{member} is a public read and nothing here says whether a submitted game "
                    + "can reach it");
        }
    }

    /// <summary>A game whose address somebody handed us, unclaimed — what the form produces.</summary>
    private static async Task<Guid> SubmittedAsync(TestDatabase db, string slug, string name)
    {
        // Recent, so that "newly discovered" would have shown it had the feed not been filtered.
        var id = await Seed.GameAsync(db, slug: slug, name: name, firstSeenAt: Now.AddHours(-1));

        await using var connection = await db.DataSource.OpenConnectionAsync();

        await connection.ExecuteAsync(
            "UPDATE game SET submitted_at = @now WHERE id = @id", new { now = Now, id });

        return id;
    }

    /// <summary>
    /// Gives a game something on every series the public surfaces read.
    /// </summary>
    /// <remarks>
    /// A game with no data is invisible to the rankings and ecosystem shares whether it's filtered or
    /// not, so a bare row would pass against a filter that didn't exist.
    /// </remarks>
    private static async Task FurnishAsync(TestDatabase db, Guid gameId)
    {
        // An unbroken reachable spell, which is the second ranking and one of the feeds' joins.
        await new NpgsqlAvailabilityStore(db.DataSource).OpenAsync(new AvailabilityInterval
        {
            GameId = gameId,
            State = AvailabilityState.Reachable,
            FromAt = Now.AddDays(-30),
        });

        var fields = new NpgsqlGameFieldStore(db.DataSource);

        await fields.UpsertAsync(new GameField(
            gameId, "CODEBASE", FieldSource.Mssp, "PennMUSH", Now.AddDays(-30), Now.AddMinutes(-5)));
        await fields.UpsertAsync(new GameField(
            gameId, CapabilityFields.Measured("GMCP"), FieldSource.Handshake, "true",
            Now.AddDays(-30), Now.AddMinutes(-5)));

        // Enough counted samples to clear the ranking's minimum, all inside its window.
        var presence = new NpgsqlPresenceStore(db.DataSource);

        for (var hour = 0; hour < NpgsqlGameQueries.MinimumRankingSamples; hour++)
        {
            await presence.AppendAsync(
                PresenceSample.Counted(gameId, Now.AddHours(-hour - 1), 40, FieldSource.Who));
        }
    }
}
