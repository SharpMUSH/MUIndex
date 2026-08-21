using Dapper;

using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// The ecosystem dashboard and the rankings, against PostgreSQL.
/// </summary>
/// <remarks>
/// These are assertions about denominators more than numerators: an unhandshaked game is out of the
/// protocol denominator rather than counted as a no, an unidentified codebase is out rather than
/// "other", and an unoffered protocol is unmeasured rather than zero per cent.
/// </remarks>
public class EcosystemQueriesPostgresTests
{
    private static readonly DateTimeOffset Now = Seed.Now;

    private static NpgsqlGameQueries QueriesOn(TestDatabase db) =>
        new(db.DataSource, time: new FixedClock(Now));

    /// <summary>
    /// The rankings, after the rollup that now feeds them has run.
    /// </summary>
    /// <remarks>
    /// The busiest table reads <c>presence_rollup_day</c>, not <c>presence_sample</c> (migration
    /// 0019), so a ranking is only as fresh as the last rollup — samples written without rolling up
    /// correctly produce an empty table.
    /// </remarks>
    private static async Task<Rankings> RankAsync(TestDatabase db, RankingSpan span = RankingSpan.Week)
    {
        await new PresenceMaintenance(
                new NpgsqlPresenceStore(db.DataSource),
                new NpgsqlPresenceRollupStore(db.DataSource),
                new PresenceRetentionOptions())
            .RunAsync(Now);

        return await QueriesOn(db).RankingsAsync(span);
    }

    /// <summary>A game whose handshake completed, which is what a measured capability is measured in.</summary>
    private static async Task HandshakedAsync(TestDatabase db, Guid game, DateTimeOffset? at = null) =>
        await new NpgsqlAvailabilityStore(db.DataSource).OpenAsync(new AvailabilityInterval
        {
            GameId = game,
            State = AvailabilityState.Reachable,
            FromAt = at ?? Now.AddDays(-30),
        });

    private static async Task FieldAsync(
        TestDatabase db, Guid game, string field, FieldSource source, string value) =>
        await new NpgsqlGameFieldStore(db.DataSource).UpsertAsync(
            new GameField(game, field, source, value, Now.AddDays(-30), Now.AddMinutes(-5)));

    private static ProtocolAdoption Protocol(EcosystemDashboard dashboard, string name) =>
        dashboard.Protocols.Single(p => p.Protocol == name);

    [Test]
    public async Task AGameWeHaveNeverHandshakedIsOutOfTheDenominatorRatherThanInItAsANo()
    {
        // Counting an unreached game in the denominator would publish our own dial backlog as the
        // hobby's adoption curve.
        await using var db = await PostgresFixture.MigratedAsync();
        var reached = await Seed.GameAsync(db, "reached", "Reached");
        await Seed.GameAsync(db, "never-dialled", "Never Dialled");
        await HandshakedAsync(db, reached);
        await FieldAsync(db, reached, CapabilityFields.Measured("GMCP"), FieldSource.Handshake, "true");

        var dashboard = await QueriesOn(db).EcosystemAsync();
        var gmcp = Protocol(dashboard, "GMCP");

        await Assert.That(dashboard.ListedGames).IsEqualTo(2);
        await Assert.That(dashboard.Handshakes).IsEqualTo(1);
        await Assert.That(gmcp.Offered).IsEqualTo(1);
        await Assert.That(gmcp.Declined).IsEqualTo(0);
        await Assert.That(gmcp.Unobserved).IsEqualTo(0);

        // And the share is a share of the games we reached, not of the catalogue.
        await Assert.That(gmcp.Measured!.Denominator).IsEqualTo(1);
        await Assert.That(gmcp.Measured!.Fraction).IsEqualTo(1.0);
    }

    [Test]
    public async Task AHandshakeThatOfferedNothingIsStillInTheDenominator()
    {
        // "Games with capability rows" would define the denominator out of the numerator, silently
        // raising every share on the page.
        await using var db = await PostgresFixture.MigratedAsync();
        var quiet = await Seed.GameAsync(db, "quiet", "Quiet");
        var talkative = await Seed.GameAsync(db, "talkative", "Talkative");
        await HandshakedAsync(db, quiet);
        await HandshakedAsync(db, talkative);
        await FieldAsync(db, talkative, CapabilityFields.Measured("GMCP"), FieldSource.Handshake, "true");

        var dashboard = await QueriesOn(db).EcosystemAsync();

        await Assert.That(dashboard.Handshakes).IsEqualTo(2);
        await Assert.That(Protocol(dashboard, "GMCP").Measured!.Denominator).IsEqualTo(2);
        await Assert.That(Protocol(dashboard, "GMCP").Unobserved).IsEqualTo(1);
    }

    [Test]
    public async Task AProtocolNothingHasOfferedIsUnmeasuredRatherThanNoughtPerCent()
    {
        // TLS is the standing case: the probe dials plain telnet, so "0% offer TLS" would be a limit
        // of our crawler stated as a fact about the hobby.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        await HandshakedAsync(db, game);
        await FieldAsync(db, game, CapabilityFields.Measured("GMCP"), FieldSource.Handshake, "true");
        await FieldAsync(db, game, CapabilityFields.Declared("TLS"), FieldSource.Mssp, "true");

        var dashboard = await QueriesOn(db).EcosystemAsync();
        var tls = Protocol(dashboard, "TLS");

        await Assert.That(tls.Offered).IsNull();
        await Assert.That(tls.Measured).IsNull();

        // The declared side is unaffected, and carries its own, different denominator.
        await Assert.That(tls.Declared).IsEqualTo(1);
        await Assert.That(tls.DeclaredShare!.Denominator).IsEqualTo(dashboard.MsspReports);
    }

    [Test]
    public async Task OnlyAProtocolWeAskedForCountsSilenceAsANo()
    {
        // MSSP is requested by name, so silence on it is a decline; silence on GMCP (never asked by
        // name) is unobserved. The two must never land in the same bucket.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        await HandshakedAsync(db, game);
        await FieldAsync(db, game, CapabilityFields.Measured("MSSP"), FieldSource.Handshake, "false");

        var dashboard = await QueriesOn(db).EcosystemAsync();

        await Assert.That(Protocol(dashboard, "MSSP").Declined).IsEqualTo(1);
        await Assert.That(Protocol(dashboard, "MSSP").Offered).IsEqualTo(0);
        await Assert.That(Protocol(dashboard, "MSSP").Unobserved).IsEqualTo(0);

        await Assert.That(Protocol(dashboard, "GMCP").Offered).IsNull();
    }

    [Test]
    public async Task ACodebaseShareCountsOnlyTheGamesThatToldUsOne()
    {
        // Rolling an unread codebase into a residual would publish our gap as somebody's market share.
        await using var db = await PostgresFixture.MigratedAsync();
        var penn = await Seed.GameAsync(db, "penn", "Penn");
        var mux = await Seed.GameAsync(db, "mux", "Mux");
        await Seed.GameAsync(db, "unknown", "Unknown");
        await FieldAsync(db, penn, "CODEBASE", FieldSource.Mssp, "PennMUSH 1.8.8p0");
        await FieldAsync(db, mux, "CODEBASE", FieldSource.Mssp, "TinyMUX 2.12");

        var codebases = (await QueriesOn(db).EcosystemAsync()).Codebases;

        await Assert.That(codebases.Identified).IsEqualTo(2);
        await Assert.That(codebases.NotIdentified).IsEqualTo(1);
        await Assert.That(codebases.Families.All(f => f.Denominator == 2)).IsTrue();
        await Assert.That(codebases.Families.Select(f => f.Label).ToList())
            .IsEquivalentTo(new[] { "PennMUSH", "TinyMUX" });
    }

    [Test]
    public async Task TwoPatchLevelsOfOneCodebaseAreOneShare()
    {
        // Market share is a question about codebases, not point releases.
        await using var db = await PostgresFixture.MigratedAsync();
        var older = await Seed.GameAsync(db, "older", "Older");
        var newer = await Seed.GameAsync(db, "newer", "Newer");
        await FieldAsync(db, older, "CODEBASE", FieldSource.Mssp, "PennMUSH 1.8.7");
        await FieldAsync(db, newer, "CODEBASE", FieldSource.Mssp, "PennMUSH 1.8.8p0");

        var families = (await QueriesOn(db).EcosystemAsync()).Codebases.Families;

        await Assert.That(families).Count().IsEqualTo(1);
        await Assert.That(families[0]).IsEqualTo(new MeasuredShare("PennMUSH", 2, 2));
    }

    [Test]
    public async Task TheLadderPicksTheCodebaseAndTheSqlDoesNotInventItsOwn()
    {
        // §5.1's ladder is resolved in SQL, not in memory, and must reach the same answer as
        // FieldPrecedence: a banner reading loses to what the game itself declares.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        await FieldAsync(db, game, "CODEBASE", FieldSource.Banner, "Rhost");
        await FieldAsync(db, game, "CODEBASE", FieldSource.Mssp, "PennMUSH 1.8.8p0");

        var families = (await QueriesOn(db).EcosystemAsync()).Codebases.Families;

        await Assert.That(families).Count().IsEqualTo(1);
        await Assert.That(families[0].Label).IsEqualTo("PennMUSH");
    }

    [Test]
    public async Task AnArchivedGameIsOutOfTheDashboardAndOutOfBothRankings()
    {
        // Archiving is a presentation change and changes exactly this much (§7.5).
        await using var db = await PostgresFixture.MigratedAsync();
        var gone = await Seed.GameAsync(db, "gaslight-row", "Gaslight Row", LifecycleState.Archived);
        await HandshakedAsync(db, gone);
        await FieldAsync(db, gone, "CODEBASE", FieldSource.Mssp, "PennMUSH 1.8.5");
        await FieldAsync(db, gone, CapabilityFields.Measured("GMCP"), FieldSource.Handshake, "true");

        var presence = new NpgsqlPresenceStore(db.DataSource);
        for (var hour = 1; hour <= 30; hour++)
        {
            await presence.AppendAsync(
                PresenceSample.Counted(gone, Now.AddHours(-hour), 40, FieldSource.Who));
        }

        var dashboard = await QueriesOn(db).EcosystemAsync();
        var rankings = await RankAsync(db);

        await Assert.That(dashboard.ListedGames).IsEqualTo(0);
        await Assert.That(dashboard.Handshakes).IsEqualTo(0);
        await Assert.That(dashboard.Codebases.Families).IsEmpty();
        await Assert.That(Protocol(dashboard, "GMCP").Offered).IsNull();
        await Assert.That(rankings.Busiest).IsEmpty();
        await Assert.That(rankings.LongestUnbroken).IsEmpty();
    }

    [Test]
    public async Task TheBusiestRankingIsAMedianOverAStatedFloorOfSamples()
    {
        // A median over too few samples would let one lucky evening probe top the table.
        await using var db = await PostgresFixture.MigratedAsync();
        var steady = await Seed.GameAsync(db, "steady", "Steady");
        var spike = await Seed.GameAsync(db, "spike", "Spike");
        var presence = new NpgsqlPresenceStore(db.DataSource);

        // Spread across six days, not packed into 30 consecutive hours: eligibility has a coverage
        // clause as well as a sample floor. The multiset of counts, and so the median, is unchanged.
        for (var hour = 1; hour <= 30; hour++)
        {
            await presence.AppendAsync(PresenceSample.Counted(
                steady, Now.AddDays(-(hour % 6)).AddHours(-(hour % 11)), hour <= 15 ? 10 : 20, FieldSource.Who));
        }

        // One enormous reading, and nothing else. Not enough to be ranked at all.
        await presence.AppendAsync(PresenceSample.Counted(spike, Now.AddHours(-1), 900, FieldSource.Who));

        var rankings = await RankAsync(db);

        await Assert.That(rankings.MinimumSamples).IsEqualTo(NpgsqlGameQueries.MinimumRankingSamples);
        await Assert.That(rankings.ListedGames).IsEqualTo(2);
        await Assert.That(rankings.Eligible).IsEqualTo(1);
        await Assert.That(rankings.Busiest.Select(g => g.Slug).ToList()).IsEquivalentTo(new[] { "steady" });

        var ranked = rankings.Busiest.Single();
        await Assert.That(ranked.Samples).IsEqualTo(30);
        await Assert.That(ranked.Peak).IsEqualTo(20);

        // percentile_disc, so the median is a number some probe actually read and never an average
        // of two that nobody measured.
        await Assert.That(ranked.Median).IsEqualTo(10);
    }

    [Test]
    public async Task AnUncountableProbeIsNotAZeroInARanking()
    {
        // Rule 4: an unparseable DOING header must not sink a game's ranking.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "midnight-sun", "Midnight Sun II");
        var presence = new NpgsqlPresenceStore(db.DataSource);

        for (var hour = 1; hour <= 30; hour++)
        {
            var at = Now.AddDays(-(hour % 6)).AddHours(-(hour % 11));

            await presence.AppendAsync(PresenceSample.Counted(game, at, 12, FieldSource.Who));
            await presence.AppendAsync(PresenceSample.Unmeasurable(
                game, at.AddMinutes(30), UnmeasurableReason.WhoUnparseable));
        }

        var ranked = (await RankAsync(db)).Busiest.Single();

        await Assert.That(ranked.Samples).IsEqualTo(30);
        await Assert.That(ranked.Median).IsEqualTo(12);
    }

    [Test]
    public async Task AWeekendOfHardProbingDoesNotRankInAQuarter()
    {
        // The clause a sample floor alone can't express: a game measured intensively over one weekend
        // clears any sample floor, and ranking it in a "last 90 days" table describes a quarter out
        // of a weekend.
        await using var db = await PostgresFixture.MigratedAsync();
        var burst = await Seed.GameAsync(db, "burst", "Burst");
        var spread = await Seed.GameAsync(db, "spread", "Spread");
        var presence = new NpgsqlPresenceStore(db.DataSource);

        // Forty samples, all inside two days.
        for (var i = 0; i < 40; i++)
        {
            await presence.AppendAsync(PresenceSample.Counted(
                burst, Now.AddDays(-(i % 2)).AddMinutes(-20 * i), 50, FieldSource.Who));
        }

        // Sixty samples across sixty consecutive days: clears the 45-day coverage a 90-day window
        // requires, where forty would not.
        for (var i = 0; i < 60; i++)
        {
            await presence.AppendAsync(PresenceSample.Counted(
                spread, Now.AddDays(-i - 1).AddMinutes(-(i % 7) * 5), 6, FieldSource.Who));
        }

        var quarter = await RankAsync(db, RankingSpan.Quarter);

        await Assert.That(quarter.Span).IsEqualTo(RankingSpan.Quarter);
        await Assert.That(quarter.Window).IsEqualTo(TimeSpan.FromDays(90));
        await Assert.That(quarter.MinimumDays).IsEqualTo(45);
        await Assert.That(quarter.Busiest.Select(g => g.Slug).ToList()).IsEquivalentTo(new[] { "spread" });

        // Over a week neither qualifies: burst covers only two of seven days, spread can't clear the
        // sample floor at one probe a day. An empty table is the honest answer.
        var week = await RankAsync(db);

        await Assert.That(week.MinimumDays).IsEqualTo(4);
        await Assert.That(week.Busiest).IsEmpty();
        await Assert.That(week.Eligible).IsEqualTo(0);
    }

    [Test]
    public async Task EachWindowIsItsOwnQuestionAndTheyCanDisagree()
    {
        // Two games that swap places between week and quarter — the reason for offering both.
        await using var db = await PostgresFixture.MigratedAsync();
        var veteran = await Seed.GameAsync(db, "veteran", "Veteran");
        var newcomer = await Seed.GameAsync(db, "newcomer", "Newcomer");
        var presence = new NpgsqlPresenceStore(db.DataSource);

        for (var day = 0; day < 80; day++)
        {
            for (var probe = 0; probe < 4; probe++)
            {
                await presence.AppendAsync(PresenceSample.Counted(
                    veteran, Now.AddDays(-day).AddHours(-probe * 5), 12, FieldSource.Who));
            }
        }

        for (var day = 0; day < 6; day++)
        {
            for (var probe = 0; probe < 6; probe++)
            {
                await presence.AppendAsync(PresenceSample.Counted(
                    newcomer, Now.AddDays(-day).AddHours(-probe * 3), 30, FieldSource.Who));
            }
        }

        var week = await RankAsync(db);
        var quarter = await RankAsync(db, RankingSpan.Quarter);

        // Over a week the newcomer leads on the count it actually has.
        await Assert.That(week.Busiest[0].Slug).IsEqualTo("newcomer");

        // Over a quarter it has not been measured on enough of the window to be ranked at all.
        await Assert.That(quarter.Busiest.Select(g => g.Slug).ToList()).IsEquivalentTo(new[] { "veteran" });
        await Assert.That(quarter.Busiest[0].Days).IsGreaterThanOrEqualTo(45);
    }

    [Test]
    public async Task ARankingReadsTheRollupAndNotTheRawSamples()
    {
        // Migration 0019: retention drops raw partitions once aggregated, so the table must still
        // stand with the samples gone.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "enduring", "Enduring");
        var presence = new NpgsqlPresenceStore(db.DataSource);

        // Spacing avoids two traps: `Now` is midday, so a 4-hour step could walk the oldest day's
        // last probe out of the day-aligned window; and the rollup's interval is half-open, so a
        // sample at exactly `Now` belongs to the next pass, not this one.
        for (var day = 0; day < 7; day++)
        {
            for (var probe = 1; probe <= 5; probe++)
            {
                await presence.AppendAsync(PresenceSample.Counted(
                    game, Now.AddDays(-day).AddHours(-probe * 2), 9, FieldSource.Who));
            }
        }

        var before = await RankAsync(db);
        await Assert.That(before.Busiest.Single().Median).IsEqualTo(9);

        await using (var connection = await db.DataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync("DELETE FROM presence_sample");
        }

        var after = await QueriesOn(db).RankingsAsync();

        await Assert.That(after.Busiest.Single().Median).IsEqualTo(9);
        await Assert.That(after.Busiest.Single().Samples).IsEqualTo(35);
    }

    [Test]
    public async Task OnlyAnOpenReachableSpellCounts()
    {
        // "Reachable on every probe since": unreachable now has no spell; a run that ended last week
        // has an ended run.
        await using var db = await PostgresFixture.MigratedAsync();
        var running = await Seed.GameAsync(db, "running", "Running");
        var down = await Seed.GameAsync(db, "down", "Down");
        var store = new NpgsqlAvailabilityStore(db.DataSource);

        await HandshakedAsync(db, running, Now.AddDays(-100));
        await store.OpenAsync(new AvailabilityInterval
        {
            GameId = down,
            State = AvailabilityState.Reachable,
            FromAt = Now.AddDays(-300),
            ToAt = Now.AddDays(-7),
        });
        await store.OpenAsync(new AvailabilityInterval
        {
            GameId = down,
            State = AvailabilityState.Unreachable,
            FromAt = Now.AddDays(-7),
            Cause = FailureCause.Refused,
        });

        var spells = (await RankAsync(db)).LongestUnbroken;

        await Assert.That(spells.Select(s => s.Slug).ToList()).IsEquivalentTo(new[] { "running" });
        await Assert.That(spells[0].LengthAt(Now)).IsEqualTo(TimeSpan.FromDays(100));
    }

    [Test]
    public async Task AnEmptyCatalogueReportsEmptySetsRatherThanZeroPerCent()
    {
        await using var db = await PostgresFixture.MigratedAsync();

        var dashboard = await QueriesOn(db).EcosystemAsync();
        var rankings = await RankAsync(db);

        await Assert.That(dashboard.Handshakes).IsEqualTo(0);
        await Assert.That(dashboard.OldestHandshake).IsNull();
        await Assert.That(dashboard.CapabilityTransitions).IsEqualTo(0);

        // Nothing measured is not nought per cent, and the view model has to be able to say so.
        await Assert.That(Protocol(dashboard, "UTF-8").DeclaredShare!.Fraction).IsNull();
        await Assert.That(rankings.Eligible).IsEqualTo(0);
    }

    [Test]
    public async Task ACapabilityTransitionIsCountedBecauseItIsWhatACurveWouldBeDrawnFrom()
    {
        // The number that says when the page can start drawing an adoption curve.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var fields = new NpgsqlGameFieldStore(db.DataSource);

        await fields.RecordChangeAsync(new FieldChange(
            game, CapabilityFields.Measured("GMCP"), FieldSource.Handshake, "false", "true", Now.AddDays(-2)));
        await fields.RecordChangeAsync(new FieldChange(
            game, "GENRE", FieldSource.Mssp, "Fantasy", "Modern", Now.AddDays(-1)));

        var dashboard = await QueriesOn(db).EcosystemAsync();

        // Only capability transitions. A game renaming its genre is a change and is not this curve.
        await Assert.That(dashboard.CapabilityTransitions).IsEqualTo(1);
    }

    private sealed class FixedClock(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }
}
