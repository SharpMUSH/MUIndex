using Dapper;

using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// Spec §5.3, §7.5, §7.6 against a real database — intervals, and the two sums that feed
/// <see cref="ArchivePolicy"/>.
/// </summary>
public class AvailabilityStorePostgresTests
{
    private static readonly DateTimeOffset Now = Seed.Now;

    [Test]
    public async Task AHundredConsecutiveTimeoutsAreOneRow()
    {
        // The property this table exists for, asserted where it actually costs storage.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var store = new NpgsqlAvailabilityStore(db.DataSource);
        var writer = new AvailabilityWriter(store);

        for (var probe = 0; probe < 100; probe++)
        {
            await writer.ObserveAsync(
                game, AvailabilityState.Unreachable, FailureCause.Timeout, Now.AddHours(probe));
        }

        await using var connection = await db.DataSource.OpenConnectionAsync();
        var rows = await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM availability_interval WHERE game_id = @game", new { game });

        await Assert.That(rows).IsEqualTo(1);
        await Assert.That((await store.OpenIntervalAsync(game))!.FromAt).IsEqualTo(Now);
    }

    [Test]
    public async Task EveryCauseTheCodeCanProduceIsOneTheDatabaseAccepts()
    {
        // The two halves of one decision: the C# spelling in SqlEnums and the CHECK constraint in the
        // migrations. A member added on one side and forgotten on the other must fail at the
        // database, not become a value every downstream reader copes with differently.
        await using var db = await PostgresFixture.MigratedAsync();
        var store = new NpgsqlAvailabilityStore(db.DataSource);

        foreach (var cause in Enum.GetValues<FailureCause>().Where(c => c is not FailureCause.None))
        {
            // A game apiece, because the partial unique index allows one open interval per game.
            var slug = cause.ToString().ToLowerInvariant();
            var game = await Seed.GameAsync(db, slug: slug, name: slug);

            await store.OpenAsync(new AvailabilityInterval
            {
                GameId = game,
                State = AvailabilityState.Unreachable,
                FromAt = Now,
                Cause = cause,
            });

            await Assert.That((await store.OpenIntervalAsync(game))!.Cause).IsEqualTo(cause);
        }
    }

    [Test]
    public async Task ClosingTheOpenIntervalsEndsThemWhereWeStoppedLooking()
    {
        // Every game watching when the crawl stopped has its interval ended there, so the silence
        // that follows is a hole rather than observation.
        await using var db = await PostgresFixture.MigratedAsync();
        var store = new NpgsqlAvailabilityStore(db.DataSource);

        var watching = await Seed.GameAsync(db, slug: "watching", name: "Watching");
        var dark = await Seed.GameAsync(db, slug: "dark", name: "Dark");
        var settled = await Seed.GameAsync(db, slug: "settled", name: "Settled");

        await store.OpenAsync(new AvailabilityInterval
        {
            GameId = watching, State = AvailabilityState.Reachable, FromAt = Now, Cause = FailureCause.None,
        });
        await store.OpenAsync(new AvailabilityInterval
        {
            GameId = dark, State = AvailabilityState.Unreachable, FromAt = Now, Cause = FailureCause.Dns,
        });

        // Already closed, and must stay exactly as it is: it recorded a span that ended while we
        // were still watching.
        await store.OpenAsync(new AvailabilityInterval
        {
            GameId = settled,
            State = AvailabilityState.Reachable,
            FromAt = Now,
            ToAt = Now.AddHours(1),
            Cause = FailureCause.None,
        });

        var stoppedAt = Now.AddHours(2);
        var closed = await store.CloseOpenIntervalsAsync(stoppedAt);

        await Assert.That(closed).IsEqualTo(2);
        await Assert.That(await store.OpenIntervalAsync(watching)).IsNull();
        await Assert.That(await store.OpenIntervalAsync(dark)).IsNull();

        // The state is kept. A game that was dark when we stopped watching was dark, and rewriting
        // that would replace one false claim with another.
        var ended = (await store.ForGameAsync(dark)).Single();
        await Assert.That(ended.State).IsEqualTo(AvailabilityState.Unreachable);
        await Assert.That(ended.Cause).IsEqualTo(FailureCause.Dns);
        await Assert.That(ended.ToAt).IsEqualTo(stoppedAt);

        await Assert.That((await store.ForGameAsync(settled)).Single().ToAt).IsEqualTo(Now.AddHours(1));
    }

    [Test]
    public async Task AnIntervalThatBeganAfterWeStoppedLookingIsLeftAlone()
    {
        // Availability is written by both the crawl loop and mui-crawl, so an interval can exist that
        // began after the last recorded cycle — closing it at the earlier instant would end it
        // before it started, which the table forbids.
        await using var db = await PostgresFixture.MigratedAsync();
        var store = new NpgsqlAvailabilityStore(db.DataSource);
        var game = await Seed.GameAsync(db);

        await store.OpenAsync(new AvailabilityInterval
        {
            GameId = game, State = AvailabilityState.Reachable, FromAt = Now.AddHours(3), Cause = FailureCause.None,
        });

        var closed = await store.CloseOpenIntervalsAsync(Now);

        await Assert.That(closed).IsEqualTo(0);
        await Assert.That((await store.OpenIntervalAsync(game))!.ToAt).IsNull();
    }

    [Test]
    public async Task WhatTheDialSaidSurvivesTheRoundTrip()
    {
        // The message lives in the row because the only other copy is in a container that keeps half
        // an hour of logs, and a dark game outlives that by days.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var store = new NpgsqlAvailabilityStore(db.DataSource);
        var writer = new AvailabilityWriter(store);

        await writer.ObserveAsync(
            game,
            AvailabilityState.Unreachable,
            FailureCause.Dns,
            Now,
            "No such host is known (realms.example.net:4000)");

        var open = await store.OpenIntervalAsync(game);

        await Assert.That(open!.Detail).IsEqualTo("No such host is known (realms.example.net:4000)");
    }

    [Test]
    public async Task ADetailThatChangedDoesNotWriteATransition()
    {
        // §5.3 is about state and cause. The message rides along as evidence and must not become a
        // seventh thing that can split an interval — a message carrying a port number or rotated
        // address would otherwise turn one outage into a row per probe.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var store = new NpgsqlAvailabilityStore(db.DataSource);
        var writer = new AvailabilityWriter(store);

        await writer.ObserveAsync(game, AvailabilityState.Unreachable, FailureCause.Timeout, Now, "first");
        var second = await writer.ObserveAsync(
            game, AvailabilityState.Unreachable, FailureCause.Timeout, Now.AddHours(1), "second");

        await Assert.That(second).IsEqualTo(AvailabilityOutcome.Extended);
        await Assert.That((await store.OpenIntervalAsync(game))!.Detail).IsEqualTo("first");
    }

    [Test]
    public async Task OnlyAChangeOfCauseWritesATransition()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var store = new NpgsqlAvailabilityStore(db.DataSource);
        var writer = new AvailabilityWriter(store);

        await writer.ObserveAsync(game, AvailabilityState.Unreachable, FailureCause.Dns, Now);
        await writer.ObserveAsync(game, AvailabilityState.Unreachable, FailureCause.Dns, Now.AddHours(1));
        await writer.ObserveAsync(game, AvailabilityState.Unreachable, FailureCause.Refused, Now.AddHours(2));

        var intervals = await store.ForGameAsync(game);

        await Assert.That(intervals).Count().IsEqualTo(2);
        await Assert.That(intervals[0].ToAt).IsEqualTo(Now.AddHours(2));
        await Assert.That(intervals[1].Cause).IsEqualTo(FailureCause.Refused);
        await Assert.That(intervals[1].IsOpen).IsTrue();
    }

    [Test]
    public async Task ClosingAnIntervalLeavesItInTheHistory()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var store = new NpgsqlAvailabilityStore(db.DataSource);

        await store.OpenAsync(new AvailabilityInterval
        {
            GameId = game,
            State = AvailabilityState.Reachable,
            FromAt = Now.AddDays(-10),
        });
        await store.CloseAsync(game, Now);

        await Assert.That(await store.OpenIntervalAsync(game)).IsNull();
        await Assert.That(await store.ForGameAsync(game)).Count().IsEqualTo(1);
    }

    [Test]
    public async Task CumulativeReachableIsSummedNotSpanned()
    {
        // Reachable two years out of five is credited two: flapping accrues nothing for the gaps.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var store = new NpgsqlAvailabilityStore(db.DataSource);

        await Insert(store, game, AvailabilityState.Reachable, 1825, 1460);
        await Insert(store, game, AvailabilityState.Unreachable, 1460, 365, FailureCause.Dns);
        await Insert(store, game, AvailabilityState.Reachable, 365, 0);

        var cumulative = await store.CumulativeReachableAsync(game, Now);

        await Assert.That(cumulative.TotalDays).IsEqualTo(730).Within(0.001);
    }

    [Test]
    public async Task TheOpenIntervalIsCountedUpToNow()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var store = new NpgsqlAvailabilityStore(db.DataSource);

        await store.OpenAsync(new AvailabilityInterval
        {
            GameId = game,
            State = AvailabilityState.Reachable,
            FromAt = Now.AddDays(-90),
        });

        await Assert.That((await store.CumulativeReachableAsync(game, Now)).TotalDays)
            .IsEqualTo(90).Within(0.001);
    }

    [Test]
    public async Task ReachableTimeIsSummedByOriginSoADistinctionCanBeMadeLater()
    {
        // `origin` is one value today — the backfill imports no history (spec §7.6), so every
        // interval here is ours. The column survives because an undifferentiated total can't be
        // split apart later if another party's measurements are ever ingested.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var store = new NpgsqlAvailabilityStore(db.DataSource);

        await store.OpenAsync(
            new AvailabilityInterval
            {
                GameId = game,
                State = AvailabilityState.Reachable,
                FromAt = Now.AddDays(-1000),
                ToAt = Now.AddDays(-400),
            },
            IntervalOrigin.FirstParty);
        await store.OpenAsync(new AvailabilityInterval
        {
            GameId = game,
            State = AvailabilityState.Reachable,
            FromAt = Now.AddDays(-100),
        });

        var ours = await store.CumulativeReachableAsync(game, Now);

        await Assert.That(ours.TotalDays).IsEqualTo(700).Within(0.001);

        // 700 days measured is 175 days of grace, unweighted and unhalved.
        await Assert.That(ArchivePolicy.GraceFor(ours).TotalDays).IsEqualTo(175).Within(0.001);
    }

    [Test]
    public async Task AGameNobodyEverReachedIsCreditedWithNothing()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var store = new NpgsqlAvailabilityStore(db.DataSource);

        await store.OpenAsync(new AvailabilityInterval
        {
            GameId = game,
            State = AvailabilityState.Unreachable,
            FromAt = Now.AddDays(-10),
            Cause = FailureCause.Dns,
        });

        await Assert.That(await store.CumulativeReachableAsync(game, Now)).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task TheSweepArchivesAndAProbeRestoresAgainstARealDatabase()
    {
        // The same behaviour as ArchiveSweeperTests, through Postgres — because a fake agreeing with
        // the code it stands in for proves only that one person wrote both.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, lastReachableAt: Now.AddDays(-400));
        var availability = new NpgsqlAvailabilityStore(db.DataSource);
        var games = new NpgsqlGameStore(db.DataSource);

        await Insert(availability, game, AvailabilityState.Reachable, 800, 400);
        await availability.OpenAsync(new AvailabilityInterval
        {
            GameId = game,
            State = AvailabilityState.Unreachable,
            FromAt = Now.AddDays(-400),
            Cause = FailureCause.Dns,
        });

        var sweeper = new ArchiveSweeper(games, availability, availability);

        var swept = await sweeper.SweepAsync(Now);
        var archived = await games.ByIdAsync(game);

        await Assert.That(swept.Archived).IsEqualTo(1);
        await Assert.That(archived!.State).IsEqualTo(LifecycleState.Archived);
        await Assert.That(archived.ArchivedAt).IsEqualTo(Now);

        await Assert.That(await sweeper.RestoreAsync(game, Now.AddDays(1))).IsTrue();

        var restored = await games.ByIdAsync(game);

        await Assert.That(restored!.State).IsEqualTo(LifecycleState.Active);
        await Assert.That(restored.ArchivedAt).IsNull();

        // Nothing was deleted on the way through: the history that earned the grace is still there.
        await Assert.That(await availability.ForGameAsync(game)).Count().IsEqualTo(2);
    }

    private static Task Insert(
        NpgsqlAvailabilityStore store,
        Guid game,
        AvailabilityState state,
        double fromDaysAgo,
        double toDaysAgo,
        FailureCause cause = FailureCause.None) =>
        store.OpenAsync(new AvailabilityInterval
        {
            GameId = game,
            State = state,
            FromAt = Now.AddDays(-fromDaysAgo),
            ToAt = Now.AddDays(-toDaysAgo),
            Cause = cause,
        });
}
