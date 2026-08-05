using Dapper;

using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// A submitted game stays off every public surface until it is claimed (spec §8, migration 0010).
/// </summary>
/// <remarks>
/// <para>
/// The rule is one sentence — <em>a game is public if nobody submitted it, or if it has been
/// claimed</em> — and the danger is not the rule, it is the number of places it has to hold. The
/// listing, the faceted search, the three liveness feeds, the ecosystem shares, the rankings, the
/// by-slug lookup and the by-id lookup are seven surfaces, and the failure mode of forgetting one is
/// a game on a public page that nobody vouched for.
/// </para>
/// <para>
/// So the test does not check the surfaces somebody remembered. It walks <see cref="IGameQueries"/>
/// by reflection and requires every member to be covered here — a method added later fails this
/// until it is either filtered or explicitly declared to need no filtering.
/// </para>
/// </remarks>
public class SubmittedGameTests
{
    private static readonly DateTimeOffset Now = Seed.Now;

    [Test]
    public async Task AnUnclaimedSubmissionIsOnNoPublicSurface()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var queries = new NpgsqlGameQueries(db.DataSource);

        var found = await Seed.GameAsync(db, slug: "found", name: "Found By Us");
        var submitted = await SubmittedAsync(db, slug: "submitted", name: "Somebody Said So");

        var listed = await queries.ListAsync(new GameFilter { IncludeArchived = true });
        var searched = await queries.SearchAsync(new GameFilter { IncludeArchived = true });
        var feeds = await queries.FeedsAsync();

        await Assert.That(listed.Select(g => g.Id)).Contains(found);
        await Assert.That(listed.Select(g => g.Id)).DoesNotContain(submitted);
        await Assert.That(searched.Games.Select(g => g.Id)).DoesNotContain(submitted);

        // A submission is not "newly discovered" — nothing discovered it.
        await Assert.That(feeds.NewlyDiscovered.Select(e => e.Slug)).DoesNotContain("submitted");

        // Not reachable by guessing its address either, on either lookup.
        await Assert.That(await queries.FindAsync("submitted")).IsNull();
        await Assert.That(await queries.FindByIdAsync(submitted)).IsNull();

        // And not in the denominator of any published share, which would let its existence be
        // inferred from arithmetic even while its page is hidden.
        var ecosystem = await queries.EcosystemAsync();
        var everything = await queries.ListAsync(new GameFilter { IncludeArchived = true });

        await Assert.That(ecosystem.ListedGames).IsEqualTo(everything.Count);
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
    /// §7.1's auto-listing is the feature this must not break. If the new column had been read as
    /// "unclaimed games are hidden" rather than "unclaimed <em>submissions</em> are hidden", the
    /// whole catalogue would have vanished — 409 of 409 games here are unclaimed.
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

    /// <summary>
    /// Every read on <see cref="IGameQueries"/> is covered above, by name.
    /// </summary>
    /// <remarks>
    /// The list is written out so that adding a query fails this test rather than silently shipping
    /// an eighth surface nobody filtered. If a new member genuinely needs no filtering, add it here
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

            // Ranks over presence samples of listed games; it reaches game rows through the same
            // filtered path and has no lookup of its own.
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

    /// <summary>A game submitted by an account, unclaimed, exactly as the web form would make it.</summary>
    private static async Task<Guid> SubmittedAsync(TestDatabase db, string slug, string name)
    {
        var id = await Seed.GameAsync(db, slug: slug, name: name);
        var user = Guid.CreateVersion7();

        await using var connection = await db.DataSource.OpenConnectionAsync();

        await connection.ExecuteAsync(
            """
            INSERT INTO app_user (id, display_name, normalised_name, security_stamp,
                                  concurrency_stamp, created_at)
            VALUES (@user, 'submitter', 'SUBMITTER', @stamp, @stamp, @now)
            """,
            new { user, stamp = Guid.NewGuid().ToString(), now = Now });

        await connection.ExecuteAsync(
            "UPDATE game SET submitted_by = @user WHERE id = @id", new { user, id });

        return id;
    }
}
