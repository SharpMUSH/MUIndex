using Dapper;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawl;
using MUI.Crawler.Persistence;
using MUI.Crawler.Tests.Support;
using MUI.Discovery;

using Npgsql;

namespace MUI.Crawler.Tests;

/// <summary>
/// A whole crawl cycle against a real PostgreSQL, with the socket replaced and nothing else.
/// </summary>
/// <remarks>
/// This is the suite that answers "does the site show measured data": it drives the same graph the
/// hosted service builds, and then asks the database what is in it — not the code what it thinks it
/// wrote. Half the design lives in <c>CHECK</c> constraints, so the assertions that matter here are
/// the ones an in-memory fake could not have made.
/// </remarks>
public class CrawlCyclePostgresTests
{
    private static CrawlCycle Build(
        NpgsqlDataSource source,
        IProbe probe,
        TimeProvider time,
        IHostResolver? resolver = null,
        DiscoveryOptions? options = null,
        TimeSpan? grace = null,
        IDnsTxtResolver? dns = null)
    {
        var discovery = options ?? new DiscoveryOptions
        {
            // No rate floor: these tests assert on what was written, and a real 250 ms gap between
            // dials would make the suite slow for nothing. The limiter has its own tests upstream.
            GlobalInterval = TimeSpan.Zero,
            PerHostInterval = TimeSpan.Zero,
        };

        var games = new NpgsqlGameStore(source);
        var endpoints = new NpgsqlEndpointStore(source);
        var fields = new NpgsqlGameFieldStore(source);
        var availability = new NpgsqlAvailabilityStore(source);
        var targets = new NpgsqlCrawlTargetRepository(source);
        var slugs = new NpgsqlSlugHistoryStore(source);

        return new CrawlCycle(
            targets,
            probe,
            // §11's gate, against the real register in the real database — "an opt-out wrote no
            // availability row" is a claim about storage and only Postgres can answer it.
            new OptOutGate(new NpgsqlCrawlOptOutRepository(source), dns ?? new ScriptedDns(), time),
            new HostScopeGuard(resolver ?? new FakeHostResolver()),
            new ProbeIngestor(
                new PresenceWriter(new NpgsqlPresenceStore(source)),
                new AvailabilityWriter(availability),
                new FieldReconciler(fields),
                games,
                new ArchiveSweeper(games, availability, availability),
                new SlugMinter(games, fields, slugs, grace)),
            new CatalogueBinder(
                games,
                endpoints,
                fields,
                slugs,
                new IdentityMatcher(
                    new CatalogueGameDirectory(games),
                    new CatalogueEndpointDirectory(endpoints),
                    fields,
                    new NpgsqlGameFieldIndex(source),
                    discovery),
                new NpgsqlDuplicateReviewRepository(source),
                time),
            new ReferralGraphWriter(new NpgsqlReferralRepository(source), targets, discovery, time),
            new CrawlRateLimiter(discovery, time),
            new HostGate(),
            discovery,
            time);
    }

    private static async Task SeedAsync(NpgsqlDataSource source, params CrawlSeed[] seeds) =>
        await CrawlSeeds.PlantAsync(new NpgsqlCrawlTargetRepository(source), seeds, TimeProvider.System);

    [Test]
    public async Task AGameThatAnsweredIsListedWithItsMeasuredAndDeclaredFacts()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await SeedAsync(source, new CrawlSeed("mush.example.org", 4201));

        var probe = new ScriptedProbe(target => Probes.Answered(
            host: target.Host,
            port: target.Port,
            offered: Probes.Offered("MSSP", "CHARSET"),
            mssp: Probes.Mssp(
                ("NAME", "Tidewater Nights"),
                ("PLAYERS", "7"),
                ("CODEBASE", "PennMUSH 1.8.8p0"),
                ("GENRE", "Fantasy"),
                ("GMCP", "1")),
            banner: "Welcome to Tidewater Nights",
            who: new WhoReading(WhoConfidence.Count, 4)));

        var report = await Build(source, probe, TimeProvider.System).RunAsync();

        await Assert.That(report.Answered).IsEqualTo(1);
        await Assert.That(report.Listed).IsEqualTo(1);
        await Assert.That(report.Counted).IsEqualTo(1);

        await using var connection = await source.OpenConnectionAsync();

        var game = await connection.QuerySingleAsync<(Guid Id, string Slug, string Name, string State)>(
            "SELECT id, slug, name, state FROM game");

        await Assert.That(game.Slug).IsEqualTo("tidewater-nights");
        await Assert.That(game.Name).IsEqualTo("Tidewater Nights");
        await Assert.That(game.State).IsEqualTo("active");

        // WHO outranks MSSP, and the row records which source won so the ladder can be audited.
        var presence = await connection.QuerySingleAsync<(int? Count, string Source, string? Reason)>(
            "SELECT count, source, unmeasurable_reason FROM presence_sample WHERE game_id = @id",
            new { id = game.Id });

        await Assert.That(presence.Count).IsEqualTo(4);
        await Assert.That(presence.Source).IsEqualTo("who");
        await Assert.That(presence.Reason).IsNull();

        // Measured beside declared, in two rows with two sources, which is the whole reason
        // game_field is keyed (game, field, source).
        var capabilities = (await connection.QueryAsync<(string Field, string Source, string Value)>(
            "SELECT field, source, value FROM game_field WHERE field LIKE 'capability.%' ORDER BY field, source"))
            .ToList();

        await Assert.That(capabilities)
            .Contains(row => row is { Field: "capability.mssp.measured", Source: "handshake", Value: "true" });
        await Assert.That(capabilities)
            .Contains(row => row is { Field: "capability.gmcp.declared", Source: "mssp", Value: "true" });
        await Assert.That(capabilities)
            .DoesNotContain(row => row.Field == "capability.gmcp.measured");

        var reach = await connection.QuerySingleAsync<(string State, string Cause, DateTimeOffset? ToAt)>(
            "SELECT state, cause, to_at FROM availability_interval WHERE game_id = @id",
            new { id = game.Id });

        await Assert.That(reach.State).IsEqualTo("reachable");
        await Assert.That(reach.Cause).IsEqualTo("none");
        await Assert.That(reach.ToAt).IsNull();

        var endpoint = await connection.QuerySingleAsync<(string Host, int Port, string Kind)>(
            "SELECT host, port, kind FROM game_endpoint WHERE game_id = @id", new { id = game.Id });

        await Assert.That(endpoint.Host).IsEqualTo("mush.example.org");
        await Assert.That(endpoint.Port).IsEqualTo(4201);

        // The connect screen is stored, which is what the game page renders as evidence.
        var screen = await connection.ExecuteScalarAsync<string>(
            "SELECT value FROM game_field WHERE game_id = @id AND field = 'connect_screen'",
            new { id = game.Id });

        await Assert.That(screen).IsEqualTo("Welcome to Tidewater Nights");
    }

    /// <summary>
    /// The crawl loop's own ceiling firing is a fault in our probe, and is recorded against the
    /// cycle rather than against the game.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two ceilings, and only one of them is a measurement.</b> <c>ProbeOptions.Timeout</c> is
    /// what the probe promises the far end will get — twenty seconds of our patience — and it
    /// expiring is a fact about that host, stored with cause <c>timeout</c>
    /// (<c>ProbeSessionTests.TheProbesOwnBudgetExpiringIsStillATimeoutWeMeasured</c>).
    /// <c>DiscoveryOptions.ProbeTimeout</c> is sixty seconds and is documented as something else
    /// entirely: <em>"the crawl loop's own hard bound on one probe, applied on top of whatever the
    /// probe promises … the loop does not get to trust a collaborator for that"</em>.
    /// </para>
    /// <para>
    /// So a probe that reaches the outer bound has overrun its own promise by forty seconds. That is
    /// a bug in our probe, and writing it into a game's public reachability history as though the
    /// game had timed out is precisely rule 5 — our limitation published as their downtime. It is
    /// counted on the cycle instead, which is where decisions and failures of ours belong, and the
    /// target is backed off so it cannot re-burn a batch slot every cycle.
    /// </para>
    /// </remarks>
    [Test]
    public async Task TheCrawlLoopsOwnCeilingIsCountedOnTheCycleAndNotAgainstTheGame()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await SeedAsync(source, new CrawlSeed("mush.example.org", 4201));

        // Longer than the ceiling below, so the loop gives up before this probe ever answers.
        var probe = new ScriptedProbe(target => Probes.Answered(host: target.Host, port: target.Port))
        {
            Latency = TimeSpan.FromSeconds(30),
        };

        var report = await Build(
            source,
            probe,
            TimeProvider.System,
            options: new DiscoveryOptions
            {
                GlobalInterval = TimeSpan.Zero,
                PerHostInterval = TimeSpan.Zero,
                ProbeTimeout = TimeSpan.FromMilliseconds(200),
            }).RunAsync();

        await Assert.That(report.Errored).IsEqualTo(1);
        await Assert.That(report.Answered).IsEqualTo(0);
        await Assert.That(report.Failed).IsEqualTo(0);

        await using var connection = await source.OpenConnectionAsync();

        // Nothing about the game: no listing minted from a probe that never landed, and above all no
        // availability row saying a host we never finished dialling was unreachable.
        await Assert.That(await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM game"))
            .IsEqualTo(0L);
        await Assert.That(
                await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM availability_interval"))
            .IsEqualTo(0L);

        // The one thing that must move, or the target is due for ever and crowds out the batch.
        var target = await connection.QuerySingleAsync<(int Failures, DateTimeOffset? Last)>(
            "SELECT consecutive_failures, last_probed_at FROM crawl_target");

        await Assert.That(target.Failures).IsEqualTo(1);
        await Assert.That(target.Last).IsNotNull();
    }

    [Test]
    public async Task AGameThatRenamesItselfTakesANewUrlAndKeepsTheOldOneWorking()
    {
        // §5.7 end to end, through the real graph and against the real schema: a game renames itself,
        // nothing moves while the new name is fresh, and once it has held for the grace period the URL
        // follows — with the old one recorded beside the game rather than in somebody's config file.
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await SeedAsync(source, new CrawlSeed("mush.example.org", 4201));

        var name = "Tidewater Nights";
        var observed = Probes.Observed;

        var probe = new ScriptedProbe(target => Probes.Answered(
            host: target.Host,
            port: target.Port,
            mssp: Probes.Mssp(("NAME", name), ("CODEBASE", "PennMUSH 1.8.8p0")),
            at: observed));

        var cycle = Build(source, probe, new StepClock(), grace: TimeSpan.FromDays(14));

        await cycle.RunAsync();

        // It renames itself. The change is measured immediately; the URL is not.
        name = "Harbourlight";
        observed = Probes.Observed.AddDays(1);
        await MakeDueAsync(source);
        await cycle.RunAsync();

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<string>("SELECT slug FROM game"))
            .IsEqualTo("tidewater-nights");
        await Assert.That(await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM game_slug_history"))
            .IsEqualTo(0L);
        await Assert.That(await connection.ExecuteScalarAsync<string>(
                "SELECT value FROM game_field WHERE field = 'NAME'"))
            .IsEqualTo("Harbourlight");

        // A fortnight on, still Harbourlight.
        observed = Probes.Observed.AddDays(20);
        await MakeDueAsync(source);
        await cycle.RunAsync();

        var game = await connection.QuerySingleAsync<(Guid Id, string Slug, string Name)>(
            "SELECT id, slug, name FROM game");

        await Assert.That(game.Slug).IsEqualTo("harbourlight");
        await Assert.That(game.Name).IsEqualTo("Harbourlight");

        var retired = await connection.QuerySingleAsync<(string Slug, Guid GameId)>(
            "SELECT slug, game_id FROM game_slug_history");

        await Assert.That(retired.Slug).IsEqualTo("tidewater-nights");
        await Assert.That(retired.GameId).IsEqualTo(game.Id);

        // And the rename is an event in its own right, which is what the change feed is for.
        await Assert.That(await connection.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM field_change WHERE field = 'NAME'"))
            .IsEqualTo(1L);
    }

    [Test]
    public async Task ABannerThatStatesItsOwnPlayerCountDoesNotWriteAChangeRowEveryProbe()
    {
        // Measured on aardmud.org:4000 during a live verification run, and it is the exact cost §5.1
        // exists to avoid: a change feed that fills with an unreadable diff of one game's welcome
        // screen buries every event that actually happened.
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await SeedAsync(source, new CrawlSeed("aard.example.org", 4000));

        var online = 206;
        var hour = 0;

        var probe = new ScriptedProbe(target => Probes.Answered(
            host: target.Host,
            port: target.Port,
            banner: $"Welcome to Aardwolf{Environment.NewLine}Players Currently Online: {online++}",
            at: Probes.Observed.AddHours(hour++)));

        var cycle = Build(source, probe, new StepClock());

        await cycle.RunAsync();
        await MakeDueAsync(source);
        await cycle.RunAsync();

        await using var connection = await source.OpenConnectionAsync();

        // The current screen is stored and is the new one …
        await Assert.That(await connection.ExecuteScalarAsync<string>(
                "SELECT value FROM game_field WHERE field = 'connect_screen'"))
            .Contains("207");

        // … and the change feed records the endpoint sighting and nothing else.
        await Assert.That(await connection.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM field_change WHERE field = 'connect_screen'"))
            .IsEqualTo(0L);

        // The count still came off the banner: the fix is about the feed, not about the measurement.
        await Assert.That(await connection.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM presence_sample WHERE source = 'banner'"))
            .IsEqualTo(2L);
    }

    [Test]
    public async Task ASecondProbeConfirmsRatherThanDuplicates()
    {
        // §5.1's whole economics: a game whose GENRE never moves costs one row per source for ever,
        // not one per probe, and the change feed stays a table of events that actually happened.
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await SeedAsync(source, new CrawlSeed("mush.example.org", 4201));

        var clock = new StepClock();
        var hour = 0;


        var probe = new ScriptedProbe(target => Probes.Answered(
            host: target.Host,
            port: target.Port,
            mssp: Probes.Mssp(("NAME", "Tidewater Nights"), ("GENRE", "Fantasy")),
            who: new WhoReading(WhoConfidence.Count, 4),
            at: Probes.Observed.AddHours(hour++)));

        var cycle = Build(source, probe, clock);

        await cycle.RunAsync();
        await MakeDueAsync(source);
        await cycle.RunAsync();

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM game"))
            .IsEqualTo(1L);
        await Assert.That(await connection.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM game_field WHERE field = 'GENRE'"))
            .IsEqualTo(1L);
        await Assert.That(await connection.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM field_change WHERE field = 'GENRE'"))
            .IsEqualTo(0L);

        // Two probes, two presence rows: the series is the one thing that does grow per probe.
        await Assert.That(await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM presence_sample"))
            .IsEqualTo(2L);

        // And one availability interval, still open: only a change of state or cause writes a
        // transition.
        await Assert.That(await connection.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM availability_interval"))
            .IsEqualTo(1L);
    }

    [Test]
    public async Task AnUncountableGameGetsAHatchedCellAndNotADarkOne()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await SeedAsync(source, new CrawlSeed("diku.example.org", 4000));

        var probe = new ScriptedProbe(target => Probes.Answered(
            host: target.Host,
            port: target.Port,
            mssp: Probes.Mssp(("NAME", "Elder Tale")),
            who: WhoReading.Unreadable));

        await Build(source, probe, TimeProvider.System).RunAsync();

        await using var connection = await source.OpenConnectionAsync();

        var row = await connection.QuerySingleAsync<(int? Count, string? Reason, string Source)>(
            "SELECT count, unmeasurable_reason, source FROM presence_sample");

        await Assert.That(row.Count).IsNull();
        await Assert.That(row.Reason).IsEqualTo("who_unparseable");

        // And it is reachable, because it answered. Uncountable is not absent.
        await Assert.That(await connection.ExecuteScalarAsync<string>(
                "SELECT state FROM availability_interval"))
            .IsEqualTo("reachable");
    }

    [Test]
    public async Task AFailedProbeAgainstAKnownGameWritesDowntimeAndNoPresenceRow()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await SeedAsync(source, new CrawlSeed("mush.example.org", 4201));

        var up = true;
        var hour = 0;

        var probe = new ScriptedProbe(target => up
            ? Probes.Answered(
                host: target.Host,
                port: target.Port,
                mssp: Probes.Mssp(("NAME", "Tidewater Nights")),
                who: new WhoReading(WhoConfidence.Count, 4),
                at: Probes.Observed.AddHours(hour++))
            : Probes.Failed(target.Host, target.Port, "refused", at: Probes.Observed.AddHours(hour++)));

        var cycle = Build(source, probe, new StepClock());

        await cycle.RunAsync();
        up = false;
        await MakeDueAsync(source);
        await cycle.RunAsync();

        await using var connection = await source.OpenConnectionAsync();

        // One presence row, from the probe that answered. The failed one wrote none.
        await Assert.That(await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM presence_sample"))
            .IsEqualTo(1L);

        var intervals = (await connection.QueryAsync<(string State, string Cause, DateTimeOffset? ToAt)>(
            "SELECT state, cause, to_at FROM availability_interval ORDER BY from_at")).ToList();

        await Assert.That(intervals).Count().IsEqualTo(2);
        await Assert.That(intervals[0].State).IsEqualTo("reachable");
        await Assert.That(intervals[0].ToAt).IsNotNull();
        await Assert.That(intervals[1].State).IsEqualTo("unreachable");
        await Assert.That(intervals[1].Cause).IsEqualTo("refused");
        await Assert.That(intervals[1].ToAt).IsNull();
    }

    [Test]
    public async Task ADialWeRefusedIsNotInAnyGamesRecord()
    {
        // §7.2. We declined to dial; we did not measure. Recording it as downtime would put our own
        // security policy into a game's public reachability history.
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await SeedAsync(source, new CrawlSeed("internal.example.org", 4201));

        var resolver = new FakeHostResolver().Resolving("internal.example.org", "169.254.169.254");
        var probe = new ScriptedProbe(_ => throw new InvalidOperationException("A refused dial must not reach the probe."));

        var report = await Build(source, probe, TimeProvider.System, resolver).RunAsync();

        await Assert.That(report.Refused).IsEqualTo(1);
        await Assert.That(report.Probed).IsEqualTo(0);
        await Assert.That(probe.Asked).IsEmpty();

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM game")).IsEqualTo(0L);
        await Assert.That(await connection.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM availability_interval"))
            .IsEqualTo(0L);
        await Assert.That(await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM presence_sample"))
            .IsEqualTo(0L);

        // But the target is rescheduled rather than left due for ever, or it burns a batch slot every
        // cycle and never stops.
        await Assert.That(await connection.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM crawl_target WHERE next_probe_at > now()"))
            .IsEqualTo(1L);
    }

    [Test]
    public async Task AMixedResolutionRefusesTheWholeTargetRatherThanPickingTheGoodAddress()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await SeedAsync(source, new CrawlSeed("rebind.example.org", 4201));

        var resolver = new FakeHostResolver().Resolving("rebind.example.org", "198.51.100.7", "127.0.0.1");
        var probe = new ScriptedProbe(_ => throw new InvalidOperationException("Refused targets are not dialled."));

        var report = await Build(source, probe, TimeProvider.System, resolver).RunAsync();

        await Assert.That(report.Refused).IsEqualTo(1);
    }

    [Test]
    public async Task AnOperatorSeedIsExemptAndNothingElseIs()
    {
        // "Operator-supplied seeds may be exempted, and nothing else may." Someone pointing the
        // crawler at their own server has said what they mean; a stranger's REFERRAL has not.
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await SeedAsync(source, new CrawlSeed("127.0.0.1", 4201, IsOperatorSeed: true));

        var probe = new ScriptedProbe(target => Probes.Answered(
            host: target.Host, port: target.Port, mssp: Probes.Mssp(("NAME", "My Own Server"))));

        var report = await Build(source, probe, TimeProvider.System).RunAsync();

        await Assert.That(report.Refused).IsEqualTo(0);
        await Assert.That(report.Answered).IsEqualTo(1);
    }

    [Test]
    public async Task AReferralBecomesATargetAndAnEdgeButNotAListingUntilItNamesItself()
    {
        // §7.1 and §7.2: the moment a host answers it is promoted to a target with its own schedule,
        // and a referred host must independently answer MSSP with its own NAME before it is listed.
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await SeedAsync(source, new CrawlSeed("mush.example.org", 4201));

        var probe = new ScriptedProbe(target => target.Host switch
        {
            "mush.example.org" => Probes.Answered(
                host: target.Host,
                port: target.Port,
                mssp: Probes.Mssp(("NAME", "Tidewater Nights"), ("REFERRAL", "shy.example.net 4444"))),

            // Answers, but says nothing about itself. A hostname somebody claimed is a game.
            _ => Probes.Answered(host: target.Host, port: target.Port),
        });

        var cycle = Build(source, probe, TimeProvider.System);

        await cycle.RunAsync();
        await cycle.RunAsync();

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM crawl_target WHERE host = 'shy.example.net'"))
            .IsEqualTo(1L);
        await Assert.That(await connection.ExecuteScalarAsync<bool>(
                "SELECT present FROM referral_edge WHERE to_host = 'shy.example.net'"))
            .IsTrue();

        // Answered, probed, and still not listed — which is what "verify, don't trust" costs.
        await Assert.That(await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM game"))
            .IsEqualTo(1L);
        await Assert.That(await connection.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM crawl_target WHERE host = 'shy.example.net' AND game_id IS NULL"))
            .IsEqualTo(1L);
    }

    [Test]
    public async Task AReferralThatNamesItselfBecomesASecondListedGameWithoutBeingSeeded()
    {
        // The other half of the rule above, and the whole reason the graph exists: one seed, two
        // games. Nothing tells the crawler about hollow.example.net — it is named only by the
        // game it was referred from, and it earns its listing by answering with a name of its own.
        //
        // Live counterpart, measured: mud.kharkov.org:3000 (Virtustan MUD, CircleMUD) publishes
        // REFERRAL for two ports of tbamud.com, and both became depth-1 targets and were probed on
        // their own schedule. They are not listed, because tbaMUD's only self-description is
        // NAME "tbaMUD" — its codebase's name, which MsspDefaults reads as unset. That is the arm
        // the previous test pins; this one pins the arm a well-described game takes.
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await SeedAsync(source, new CrawlSeed("mush.example.org", 4201));

        var probe = new ScriptedProbe(target => target.Host switch
        {
            "mush.example.org" => Probes.Answered(
                host: target.Host,
                port: target.Port,
                mssp: Probes.Mssp(("NAME", "Tidewater Nights"), ("REFERRAL", "hollow.example.net 4444"))),

            _ => Probes.Answered(
                host: target.Host,
                port: target.Port,
                mssp: Probes.Mssp(("NAME", "The Hollow Coast"))),
        });

        var cycle = Build(source, probe, TimeProvider.System);

        await cycle.RunAsync();
        var second = await cycle.RunAsync();

        await Assert.That(second.Listed).IsEqualTo(1);

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM game"))
            .IsEqualTo(2L);

        // Listed as its own game, at depth 1, attributed to the game that named it — the provenance
        // that makes the graph a graph rather than a bag of hosts.
        await Assert.That(await connection.ExecuteScalarAsync<long>(
                """
                SELECT count(*)
                  FROM crawl_target t
                  JOIN game referrer ON referrer.id = t.discovered_from_game_id
                 WHERE t.host = 'hollow.example.net'
                   AND t.depth = 1
                   AND t.game_id IS NOT NULL
                   AND t.game_id <> referrer.id
                """))
            .IsEqualTo(1L);

        await Assert.That(await connection.ExecuteScalarAsync<string>(
                "SELECT name FROM game WHERE id = (SELECT game_id FROM crawl_target "
                + "WHERE host = 'hollow.example.net')"))
            .IsEqualTo("The Hollow Coast");
    }

    [Test]
    public async Task AKnownEndpointAttachesTheProbeToTheGameItAlreadyIsRatherThanMintingASecond()
    {
        // §7.3's strongest signal, exercised through the whole loop: the same address probed from a
        // registry entry that has lost its game_id must not produce a duplicate listing.
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await SeedAsync(source, new CrawlSeed("mush.example.org", 4201));

        var probe = new ScriptedProbe(target => Probes.Answered(
            host: target.Host,
            port: target.Port,
            mssp: Probes.Mssp(("NAME", "Tidewater Nights"), ("CREATED", "2004"))));

        var cycle = Build(source, probe, TimeProvider.System);

        await cycle.RunAsync();

        await using (var connection = await source.OpenConnectionAsync())
        {
            await connection.ExecuteAsync("UPDATE crawl_target SET game_id = NULL");
        }

        await MakeDueAsync(source);
        await cycle.RunAsync();

        await using var check = await source.OpenConnectionAsync();

        await Assert.That(await check.ExecuteScalarAsync<long>("SELECT count(*) FROM game")).IsEqualTo(1L);
    }

    [Test]
    public async Task OneProbeAtATimePerHostAndNoMoreThanTheConcurrencyCapOverall()
    {
        // Spec §12, and neither bound is hygiene: the crawler shares a process with the web tier, and
        // a game advertising six ports is one machine and one operator.
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await SeedAsync(
            source,
            new CrawlSeed("busy.example.org", 4201),
            new CrawlSeed("busy.example.org", 4202),
            new CrawlSeed("busy.example.org", 4203),
            new CrawlSeed("other.example.org", 4201),
            new CrawlSeed("third.example.org", 4201));

        var probe = new ScriptedProbe(target => Probes.Answered(host: target.Host, port: target.Port))
        {
            Latency = TimeSpan.FromMilliseconds(120),
        };

        var options = new DiscoveryOptions
        {
            MaxConcurrency = 2,
            GlobalInterval = TimeSpan.Zero,
            PerHostInterval = TimeSpan.Zero,
        };

        await Build(source, probe, TimeProvider.System, options: options).RunAsync();

        await Assert.That(probe.Asked).Count().IsEqualTo(5);
        await Assert.That(probe.Peak).IsLessThanOrEqualTo(2);
    }

    [Test]
    public async Task AProbeThatThrowsCostsOneTargetAndNotTheCycle()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await SeedAsync(
            source, new CrawlSeed("bad.example.org", 4201), new CrawlSeed("good.example.org", 4201));

        var probe = new ScriptedProbe(target => target.Host is "bad.example.org"
            ? throw new InvalidOperationException("the probe exploded")
            : Probes.Answered(host: target.Host, port: target.Port, mssp: Probes.Mssp(("NAME", "Corvid"))));

        var report = await Build(source, probe, TimeProvider.System).RunAsync();

        await Assert.That(report.Errored).IsEqualTo(1);
        await Assert.That(report.Listed).IsEqualTo(1);
    }

    [Test]
    public async Task AServersCrawlDelayIsRememberedAndOutranksTheWeeklyFloor()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await SeedAsync(source, new CrawlSeed("polite.example.org", 4201));

        var probe = new ScriptedProbe(target => Probes.Answered(
            host: target.Host,
            port: target.Port,
            mssp: Probes.Mssp(("NAME", "Slow Burn"), ("CRAWL DELAY", "720"))));

        await Build(source, probe, TimeProvider.System).RunAsync();

        await using var connection = await source.OpenConnectionAsync();

        var target = await connection.QuerySingleAsync<(TimeSpan? Delay, DateTimeOffset Next)>(
            "SELECT crawl_delay, next_probe_at FROM crawl_target");

        await Assert.That(target.Delay).IsEqualTo(TimeSpan.FromDays(30));
        await Assert.That(target.Next - DateTimeOffset.UtcNow).IsGreaterThan(TimeSpan.FromDays(29));
    }

    /// <summary>Brings every target forward so a second cycle has something to do.</summary>
    [Test]
    public async Task ATwinThatOnlyBecomesRecognisableLaterIsStillReviewed()
    {
        // aardmud.org:23 and aardmud.org:4000 are one game, with byte-identical connect screens, two
        // listings and no duplicate review between them. Identity was decided once, on first sighting,
        // and never revisited: BindAsync returned at its first branch whenever the target already had
        // a game, so the matcher never ran again. A second port that happened to look different the
        // one time it was compared — behind a maintenance screen, mid-reboot, or simply not yet
        // carrying the name — was a separate listing for ever, and no later evidence could reach it.
        //
        // §7.3's middle band is "both pages live and a person decides". That has to stay reachable
        // after the first probe, or the band only ever catches twins discovered minutes apart.
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await SeedAsync(source, new CrawlSeed("mush.example.org", 4201), new CrawlSeed("mush.example.org", 4000));

        const string screen = "Welcome to Tidewater Nights.\nA harbour town, and a long memory.";
        var closed = "This port is closed for maintenance. Please try again later on.";

        var probe = new ScriptedProbe(target => Probes.Answered(
            host: target.Host,
            port: target.Port,
            banner: target.Port == 4201 ? screen : closed));

        var cycle = Build(source, probe, new StepClock());

        var first = await cycle.RunAsync();

        // Two listings, and rightly so: on the evidence of that cycle they were two different servers.
        await Assert.That(first.Listed).IsEqualTo(2);
        await Assert.That(first.ReviewsOpened).IsEqualTo(0);

        // The second port comes out of maintenance and serves the same screen as the first.
        closed = screen;
        await MakeDueAsync(source);
        var second = await cycle.RunAsync();

        await Assert.That(second.ReviewsOpened).IsEqualTo(1);

        await using var connection = await source.OpenConnectionAsync();

        // Nothing is merged behind anyone's back — the addresses keep the games they were bound to
        // and both pages stay live. What must exist is the open pair.
        await Assert.That(await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM game"))
            .IsEqualTo(2L);

        var pair = await connection.QuerySingleAsync<(Guid Left, Guid Right, double Score)>(
            "SELECT left_game_id, right_game_id, score FROM duplicate_review WHERE resolved_at IS NULL");

        await Assert.That(pair.Left).IsNotEqualTo(pair.Right);
        await Assert.That(pair.Score).IsEqualTo(IdentityWeights.BannerHash);

        // And it stays one row however long the twin goes on matching, or the operator gets a fresh
        // pair to judge every time the crawler comes round.
        await MakeDueAsync(source);
        await cycle.RunAsync();

        await Assert.That(await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM duplicate_review"))
            .IsEqualTo(1L);
    }

    private static async Task MakeDueAsync(NpgsqlDataSource source)
    {
        await using var connection = await source.OpenConnectionAsync();
        await connection.ExecuteAsync("UPDATE crawl_target SET next_probe_at = now() - interval '1 minute'");
    }

    /// <summary>
    /// A clock that moves one second per reading, so ordered writes get ordered timestamps.
    /// </summary>
    /// <remarks>
    /// It starts from the real now rather than from a fixed instant, because the seed list is planted
    /// with the system clock and a scheduler reading a date in the past would find nothing due —
    /// which fails as "no game was created" and takes a while to recognise. What the writers stamp
    /// rows with is <c>ProbeResult.ObservedAt</c>, which the fixtures set and this does not touch.
    /// </remarks>
    private sealed class StepClock : TimeProvider
    {
        private readonly DateTimeOffset _start = DateTimeOffset.UtcNow;

        private long _ticks;

        public override DateTimeOffset GetUtcNow() =>
            _start.AddSeconds(Interlocked.Increment(ref _ticks));
    }
}
