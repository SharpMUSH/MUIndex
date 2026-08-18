using Dapper;

using MUI.Crawler.Persistence;
using MUI.Crawler.Tests.Support;
using MUI.Discovery;

using Npgsql;

namespace MUI.Crawler.Tests;

/// <summary>
/// <c>i3_mud</c> against a real PostgreSQL (migration 0021).
/// </summary>
/// <remarks>
/// The constraints here are the ones that keep a count attached to the right game, and a constraint
/// asserted in C# is a constraint that is not there. Two muds resolving onto one game would double a
/// game's presence series; a refresh clearing a binding would detach it silently.
/// </remarks>
public class I3BindingPostgresTests
{
    private static readonly DateTimeOffset At = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AMudIsRecordedAndRefreshedWithoutLosingWhenWeFirstSawIt()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var repository = new NpgsqlI3BindingRepository(db.DataSource);

        await repository.UpsertAsync("Nightfall", "82.153.225.173", 4242, true, true, At, default);
        await repository.UpsertAsync("Nightfall", "82.153.225.173", 4243, false, true, At.AddDays(1), default);

        var row = (await repository.AllAsync(default)).Single();

        await Assert.That(row.Port).IsEqualTo(4243);
        await Assert.That(row.AnswersWho).IsFalse();
        await Assert.That(row.FirstSeenAt).IsEqualTo(At);
        await Assert.That(row.LastSeenAt).IsEqualTo(At.AddDays(1));
    }

    /// <summary>
    /// A routine mudlist refresh has learned nothing about identity, so it must not be able to unset
    /// one. The router relisting a mud at a new address is a fact about the address.
    /// </summary>
    [Test]
    public async Task ARefreshNeverClearsAnEstablishedBinding()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var repository = new NpgsqlI3BindingRepository(db.DataSource);
        var game = await GameAsync(db);

        await repository.UpsertAsync("Nightfall", "82.153.225.173", 4242, true, true, At, default);
        await repository.BindAsync("Nightfall", game, default);
        await repository.UpsertAsync("Nightfall", "198.51.100.7", 4242, true, true, At.AddDays(1), default);

        await Assert.That((await repository.AllAsync(default)).Single().GameId).IsEqualTo(game);
    }

    /// <summary>
    /// One game, one mud — and the second name is turned away with an answer rather than an exception.
    /// </summary>
    /// <remarks>
    /// <b>Measured on the live network, not imagined.</b> One game routinely holds several I3 names:
    /// <c>The Zone</c> is also <c>The Zone-dalet</c>, <c>The Zone-i4</c> and <c>The Zone-wpr</c> — one
    /// mud registered once per router — and <c>Battlespace MUD</c> is also <c>Battlespace_MUD</c>.
    /// Two names claiming one game would double-count it, so the index refuses; letting that refusal
    /// throw meant every game with a second name tore down the whole pass, for ever, on every retry.
    /// The index is still the guarantee. What changed is that a caller learns somebody else has it.
    /// </remarks>
    [Test]
    public async Task ASecondNameForOneGameIsTurnedAwayRatherThanThrowing()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var repository = new NpgsqlI3BindingRepository(db.DataSource);
        var game = await GameAsync(db);

        await repository.UpsertAsync("The Zone", "136.144.155.250", 8888, true, true, At, default);
        await repository.UpsertAsync("The Zone-i4", "136.144.155.250", 8888, true, true, At, default);

        await Assert.That(await repository.BindAsync("The Zone", game, default)).IsTrue();
        await Assert.That(await repository.BindAsync("The Zone-i4", game, default)).IsFalse();

        var bound = (await repository.AllAsync(default))
            .Where(b => b.GameId is not null)
            .Select(b => b.MudName)
            .ToList();

        await Assert.That(bound).IsEquivalentTo(new[] { "The Zone" });
    }

    /// <summary>Binding is establish-once: a second call cannot move it somewhere else.</summary>
    [Test]
    public async Task ABindingIsNotMovedBySecondThoughts()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var repository = new NpgsqlI3BindingRepository(db.DataSource);
        var first = await GameAsync(db);
        var second = await GameAsync(db);

        await repository.UpsertAsync("Nightfall", "82.153.225.173", 4242, true, true, At, default);
        await Assert.That(await repository.BindAsync("Nightfall", first, default)).IsTrue();
        await Assert.That(await repository.BindAsync("Nightfall", second, default)).IsFalse();

        await Assert.That((await repository.AllAsync(default)).Single().GameId).IsEqualTo(first);
    }

    /// <summary>
    /// The cycle's question: bound, consenting, and not asked recently. An unbound mud is not a game
    /// yet, and a mud that does not advertise <c>who</c> has said not to ask.
    /// </summary>
    [Test]
    public async Task OnlyBoundConsentingAndOverdueMudsAreAskable()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var repository = new NpgsqlI3BindingRepository(db.DataSource);
        var bound = await GameAsync(db);
        var quiet = await GameAsync(db);
        var recent = await GameAsync(db);

        await repository.UpsertAsync("Bound", "203.0.113.1", 4000, true, true, At, default);
        await repository.BindAsync("Bound", bound, default);

        await repository.UpsertAsync("Unbound", "203.0.113.2", 4000, true, true, At, default);

        await repository.UpsertAsync("NoWho", "203.0.113.3", 4000, false, true, At, default);
        await repository.BindAsync("NoWho", quiet, default);

        await repository.UpsertAsync("JustAsked", "203.0.113.4", 4000, true, true, At, default);
        await repository.BindAsync("JustAsked", recent, default);
        await repository.MarkAskedAsync("JustAsked", At, default);

        var askable = await repository.AskableAsync(At.AddMinutes(-30), 50, default);

        await Assert.That(askable.Select(b => b.MudName)).IsEquivalentTo(new[] { "Bound" });
    }

    /// <summary>
    /// I3 never removes a listing, only marks it down — so without this gate a mud the router has
    /// given up on would be asked forever on the ordinary pacing floor.
    /// </summary>
    [Test]
    public async Task ADownMudIsNotAskable()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var repository = new NpgsqlI3BindingRepository(db.DataSource);
        var down = await GameAsync(db);
        var up = await GameAsync(db);

        await repository.UpsertAsync("Fallen", "203.0.113.5", 4000, true, false, At, default);
        await repository.BindAsync("Fallen", down, default);

        await repository.UpsertAsync("Standing", "203.0.113.6", 4000, true, true, At, default);
        await repository.BindAsync("Standing", up, default);

        var askable = await repository.AskableAsync(At.AddMinutes(-30), 50, default);

        await Assert.That(askable.Select(b => b.MudName)).IsEquivalentTo(new[] { "Standing" });
    }

    [Test]
    public async Task BeingAskedIsRecordedSoThePacingFloorSurvivesARestart()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var repository = new NpgsqlI3BindingRepository(db.DataSource);

        await repository.UpsertAsync("Nightfall", "82.153.225.173", 4242, true, true, At, default);
        await repository.MarkAskedAsync("Nightfall", At, default);

        await Assert.That((await repository.AllAsync(default)).Single().LastAskedAt).IsEqualTo(At);
    }

    /// <summary>A minimal listed game, so a binding has something real to point at.</summary>
    private static async Task<Guid> GameAsync(TestDatabase db)
    {
        var id = Guid.CreateVersion7();
        await using var connection = await db.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO game (id, slug, name, state, is_claimed, first_seen_at)
            VALUES (@id, @slug, @name, 'active', false, @at)
            """,
            // The whole GUID. Truncating a v7 keeps only its timestamp prefix, so two games minted in
            // the same millisecond collide on game_slug_key — which they do, in a test.
            new { id, slug = $"g-{id:N}", name = "A Game", at = At });

        return id;
    }
}
