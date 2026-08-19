using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// Spec §5.1 against a real database: confirm versus change, and the source-keyed row that lets a
/// capability be measured and declared at once.
/// </summary>
public class GameFieldStorePostgresTests
{
    private static readonly DateTimeOffset Noon = Seed.Now;

    [Test]
    public async Task ARepeatedValueBumpsTheConfirmationAndAppendsNoChangeRow()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var store = new NpgsqlGameFieldStore(db.DataSource);
        var reconciler = new FieldReconciler(store);
        List<FieldObservation> observed = [new("GENRE", FieldSource.Mssp, "Fantasy")];

        await reconciler.ApplyAsync(game, observed, Noon);
        var again = await reconciler.ApplyAsync(game, observed, Noon.AddHours(1));

        var stored = (await store.ForGameAsync(game)).Single();

        await Assert.That(again.Confirmed).IsEqualTo(1);
        await Assert.That(stored.LastConfirmedAt).IsEqualTo(Noon.AddHours(1));
        await Assert.That(stored.FirstSeenAt).IsEqualTo(Noon);
        await Assert.That(await store.ChangesAsync(game, 10)).IsEmpty();
    }

    [Test]
    public async Task AFieldThatNeverMovesCostsOneRowPerSourceForever()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var store = new NpgsqlGameFieldStore(db.DataSource);
        var reconciler = new FieldReconciler(store);
        List<FieldObservation> observed = [new("GENRE", FieldSource.Mssp, "Fantasy")];

        for (var probe = 0; probe < 25; probe++)
        {
            await reconciler.ApplyAsync(game, observed, Noon.AddHours(probe));
        }

        await Assert.That(await store.ForGameAsync(game)).Count().IsEqualTo(1);
        await Assert.That(await store.ChangesAsync(game, 10)).IsEmpty();
    }

    [Test]
    public async Task OnlyATransitionReachesTheChangeFeed()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var store = new NpgsqlGameFieldStore(db.DataSource);
        var reconciler = new FieldReconciler(store);

        await reconciler.ApplyAsync(
            game, [new FieldObservation("CODEBASE", FieldSource.Mssp, "PennMUSH 1.8.7")], Noon);
        await reconciler.ApplyAsync(
            game, [new FieldObservation("CODEBASE", FieldSource.Mssp, "PennMUSH 1.8.7")], Noon.AddHours(1));
        await reconciler.ApplyAsync(
            game, [new FieldObservation("CODEBASE", FieldSource.Mssp, "PennMUSH 1.8.8p0")], Noon.AddHours(2));

        var changes = await store.ChangesAsync(game, 10);

        await Assert.That(changes).Count().IsEqualTo(1);
        await Assert.That(changes[0].OldValue).IsEqualTo("PennMUSH 1.8.7");
        await Assert.That(changes[0].NewValue).IsEqualTo("PennMUSH 1.8.8p0");
        await Assert.That((await store.ForGameAsync(game)).Single().Value).IsEqualTo("PennMUSH 1.8.8p0");
    }

    [Test]
    public async Task MeasuredAndDeclaredSurviveAsSeparateRowsWithSeparateAges()
    {
        // One row per (game, field) could not hold both, and §9's capability matrix needs both at
        // once. This is the assertion the key exists for.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var store = new NpgsqlGameFieldStore(db.DataSource);

        await store.UpsertAsync(new GameField(
            game, CapabilityFields.Measured("GMCP"), FieldSource.Handshake, "false", Noon, Noon));
        await store.UpsertAsync(new GameField(
            game, CapabilityFields.Declared("GMCP"), FieldSource.Mssp, "true",
            Noon.AddYears(-6), Noon.AddYears(-6)));

        var stored = await store.ForGameAsync(game);

        await Assert.That(stored).Count().IsEqualTo(2);
        await Assert.That(stored.Single(f => f.Source is FieldSource.Handshake).Value).IsEqualTo("false");
        await Assert.That(stored.Single(f => f.Source is FieldSource.Mssp).LastConfirmedAt)
            .IsEqualTo(Noon.AddYears(-6));
    }

    [Test]
    public async Task TwoSourcesForOneFieldAreTwoRowsAndThePrecedenceLadderPicks()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var store = new NpgsqlGameFieldStore(db.DataSource);

        await store.UpsertAsync(new GameField(game, "CODEBASE", FieldSource.Banner, "PennMUSH 1.8.7", Noon, Noon));
        await store.UpsertAsync(new GameField(game, "CODEBASE", FieldSource.Mssp, "PennMUSH 1.8.8p0", Noon, Noon));

        var stored = await store.ForGameAsync(game);

        await Assert.That(stored).Count().IsEqualTo(2);
        await Assert.That(FieldPrecedence.Winner(stored)!.Source).IsEqualTo(FieldSource.Mssp);
    }

    [Test]
    public async Task AConfirmationNeverWalksTheAgeBackwards()
    {
        // Probes for a multi-port game are not serialised against each other, so an older answer can
        // arrive after a newer one. It must not make a fresh value look stale.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var store = new NpgsqlGameFieldStore(db.DataSource);

        await store.UpsertAsync(new GameField(game, "GENRE", FieldSource.Mssp, "Fantasy", Noon, Noon.AddHours(2)));
        await store.UpsertAsync(new GameField(game, "GENRE", FieldSource.Mssp, "Fantasy", Noon, Noon));

        await Assert.That((await store.ForGameAsync(game)).Single().LastConfirmedAt)
            .IsEqualTo(Noon.AddHours(2));
    }

    [Test]
    public async Task WhenAValueLastMovedIsTheChangeFeedsQuestionAndNotTheRows()
    {
        // §5.7 asks "has this name been stable for a grace period", and the row cannot answer it:
        // first_seen_at is the age of the (game, field, source) row and survives a change on purpose.
        // Folded on the field name, because MSSP spells it NAME and IdentityFields spells it name.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var store = new NpgsqlGameFieldStore(db.DataSource);

        await store.UpsertAsync(new GameField(game, "NAME", FieldSource.Mssp, "Harbourlight", Noon.AddYears(-2), Noon));

        await Assert.That(await store.LastChangedAtAsync(game, "NAME")).IsNull();

        await store.RecordChangeAsync(new FieldChange(
            game, "NAME", FieldSource.Mssp, "Corvid", "Harbourlight", Noon.AddYears(-1)));
        await store.RecordChangeAsync(new FieldChange(
            game, "GENRE", FieldSource.Mssp, "Fantasy", "Historical", Noon));

        await Assert.That(await store.LastChangedAtAsync(game, "name")).IsEqualTo(Noon.AddYears(-1));
    }

    [Test]
    public async Task AFlappingFieldShowsOnlyItsThreeNewestTransitions()
    {
        // NAME/PLAYERNAMES/MOBILES-style fields can flip every crawl; the feed caps each field at its
        // three newest rows so one noisy field cannot crowd the rest off the game page, while every
        // transition still lands in field_change forever (spec §7.5).
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var store = new NpgsqlGameFieldStore(db.DataSource);
        var reconciler = new FieldReconciler(store);

        string[] values = ["one", "two", "one", "two", "one", "two"];
        for (var probe = 0; probe < values.Length; probe++)
        {
            await reconciler.ApplyAsync(
                game, [new FieldObservation("PLAYERNAMES", FieldSource.Mssp, values[probe])], Noon.AddHours(probe));
        }
        await reconciler.ApplyAsync(
            game, [new FieldObservation("GENRE", FieldSource.Mssp, "Fantasy")], Noon.AddHours(10));
        await reconciler.ApplyAsync(
            game, [new FieldObservation("GENRE", FieldSource.Mssp, "Historical")], Noon.AddHours(11));

        var changes = await store.ChangesAsync(game, 10);

        await Assert.That(changes.Count(c => c.Field == "PLAYERNAMES")).IsEqualTo(3);
        await Assert.That(changes.Where(c => c.Field == "PLAYERNAMES").Select(c => c.At))
            .IsEquivalentTo([Noon.AddHours(5), Noon.AddHours(4), Noon.AddHours(3)]);
        await Assert.That(changes.Count(c => c.Field == "GENRE")).IsEqualTo(1);
    }
}
