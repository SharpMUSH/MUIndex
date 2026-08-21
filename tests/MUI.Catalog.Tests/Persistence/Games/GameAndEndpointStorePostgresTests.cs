using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

using Npgsql;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>Spec §5, §5.5, §5.7 — the game row and the addresses it answers on.</summary>
public class GameAndEndpointStorePostgresTests
{
    private static readonly DateTimeOffset Now = Seed.Now;

    [Test]
    public async Task AGameIsFoundByIdAndBySlug()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var store = new NpgsqlGameStore(db.DataSource);
        var id = await Seed.GameAsync(db);

        var byId = await store.ByIdAsync(id);
        var bySlug = await store.BySlugAsync("corvid");

        await Assert.That(byId!.Name).IsEqualTo("Corvid");
        await Assert.That(bySlug!.Id).IsEqualTo(id);
    }

    [Test]
    public async Task AGameNobodyInsertedIsNullRatherThanAThrow()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var store = new NpgsqlGameStore(db.DataSource);

        await Assert.That(await store.ByIdAsync(Guid.NewGuid())).IsNull();
        await Assert.That(await store.BySlugAsync("nobody")).IsNull();
    }

    [Test]
    public async Task ASlugIsUniqueForeverBecauseAUrlThatWorkedKeepsWorking()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        await Seed.GameAsync(db);

        await Assert.That(async () => await Seed.GameAsync(db, name: "Corvid Two"))
            .Throws<PostgresException>();
    }

    [Test]
    public async Task LastReachableAtOnlyEverMovesForward()
    {
        // Probes for a multi-port game are not serialised, so an older answer can arrive late. It
        // must not make a game that answered ten minutes ago look a week dark.
        await using var db = await PostgresFixture.MigratedAsync();
        var store = new NpgsqlGameStore(db.DataSource);
        var id = await Seed.GameAsync(db);

        await store.MarkReachableAsync(id, Now);
        await store.MarkReachableAsync(id, Now.AddDays(-7));

        await Assert.That((await store.ByIdAsync(id))!.LastReachableAt).IsEqualTo(Now);
    }

    [Test]
    public async Task TheSweepConsidersEverythingNotAlreadyArchived()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var store = new NpgsqlGameStore(db.DataSource);
        await Seed.GameAsync(db, "one", "One");
        await Seed.GameAsync(db, "two", "Two", LifecycleState.Dark);
        await Seed.GameAsync(db, "three", "Three", LifecycleState.Archived);

        var unarchived = await store.UnarchivedAsync();

        await Assert.That(unarchived.Select(g => g.Slug).Order().ToList())
            .IsEquivalentTo(new[] { "one", "two" });
    }

    [Test]
    public async Task AnEndpointIsFoundByItsAddressHoweverItIsSpelled()
    {
        // §7.3 calls a previously-seen endpoint the strongest identity signal, and it is only a
        // signal if one machine has one spelling. 'MUD.Example.ORG.' and 'mud.example.org' are the
        // same host and must not become two rows and two games.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var store = new NpgsqlEndpointStore(db.DataSource);

        await store.UpsertAsync(new GameEndpoint(
            game, "MUD.Example.ORG.", 4201, EndpointKind.Telnet, Now, Now, EndpointState.Active));
        await store.UpsertAsync(new GameEndpoint(
            game, "mud.example.org", 4201, EndpointKind.Telnet, Now, Now.AddDays(1), EndpointState.Active));

        var found = await store.ByAddressAsync("Mud.Example.Org", 4201);

        await Assert.That(await store.ForGameAsync(game)).Count().IsEqualTo(1);
        await Assert.That(found!.Host).IsEqualTo("mud.example.org");
        await Assert.That(found.LastSeenAt).IsEqualTo(Now.AddDays(1));
        await Assert.That(found.FirstSeenAt).IsEqualTo(Now);
    }

    [Test]
    public async Task OneAddressCannotBeClaimedByTwoGames()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var first = await Seed.GameAsync(db, "first", "First");
        var second = await Seed.GameAsync(db, "second", "Second");
        var store = new NpgsqlEndpointStore(db.DataSource);

        await store.UpsertAsync(new GameEndpoint(
            first, "mud.example.org", 4201, EndpointKind.Telnet, Now, Now, EndpointState.Active));

        await Assert.That(async () => await store.UpsertAsync(new GameEndpoint(
                second, "mud.example.org", 4201, EndpointKind.Telnet, Now, Now, EndpointState.Active)))
            .Throws<PostgresException>();
    }

    [Test]
    public async Task AnEndpointAGameHasMovedOffIsKeptRatherThanDeleted()
    {
        // Old endpoints are still probed at the §7.4 floor, so a game that moves back is found again
        // rather than minted as a duplicate.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var store = new NpgsqlEndpointStore(db.DataSource);

        await store.UpsertAsync(new GameEndpoint(
            game, "old.example.org", 4201, EndpointKind.Telnet, Now.AddYears(-3), Now.AddYears(-1),
            EndpointState.Gone));
        await store.UpsertAsync(new GameEndpoint(
            game, "new.example.org", 4201, EndpointKind.Telnet, Now.AddYears(-1), Now,
            EndpointState.Active));

        var endpoints = await store.ForGameAsync(game);

        await Assert.That(endpoints).Count().IsEqualTo(2);
        await Assert.That(endpoints.Single(e => e.State is EndpointState.Gone).Host)
            .IsEqualTo("old.example.org");
    }
}
