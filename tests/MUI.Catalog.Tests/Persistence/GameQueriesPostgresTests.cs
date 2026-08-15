using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// <see cref="IGameQueries"/> against PostgreSQL — the interface the site was built against a
/// fixture, so that swapping the fixture for this changes no page.
/// </summary>
public class GameQueriesPostgresTests
{
    private static readonly DateTimeOffset Now = Seed.Now;

    private static NpgsqlGameQueries QueriesOn(TestDatabase db) =>
        new(db.DataSource) { Clock = () => Now };

    [Test]
    public async Task ArchivedGamesLeaveTheDefaultListingAndNothingElse()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        await Seed.GameAsync(db, "corvid", "Corvid");
        await Seed.GameAsync(db, "gaslight-row", "Gaslight Row", LifecycleState.Archived);
        var queries = QueriesOn(db);

        var listed = await queries.ListAsync(new GameFilter());
        var withArchive = await queries.ListAsync(new GameFilter { IncludeArchived = true });
        var page = await queries.FindAsync("gaslight-row");

        await Assert.That(listed.Select(g => g.Slug).ToList()).IsEquivalentTo(new[] { "corvid" });
        await Assert.That(withArchive).Count().IsEqualTo(2);

        // Its page survives, which is the half of §7.5 that is easy to lose.
        await Assert.That(page).IsNotNull();
        await Assert.That(page!.Summary.State).IsEqualTo(LifecycleState.Archived);
    }

    [Test]
    public async Task AGameNobodyHasIsNullRatherThanAThrow()
    {
        await using var db = await PostgresFixture.MigratedAsync();

        await Assert.That(await QueriesOn(db).FindAsync("nobody")).IsNull();
    }

    [Test]
    public async Task ACountIsShownOnlyWhileItIsStillACountOfNow()
    {
        // The window is the field registry's own for PLAYERS, so a count on a page and a count in the
        // API age out at the same moment and neither invents its own idea of fresh.
        await using var db = await PostgresFixture.MigratedAsync();
        var fresh = await Seed.GameAsync(db, "fresh", "Fresh");
        var stale = await Seed.GameAsync(db, "stale", "Stale");
        var presence = new NpgsqlPresenceStore(db.DataSource);

        await presence.AppendAsync(PresenceSample.Counted(fresh, Now.AddMinutes(-10), 15, FieldSource.Who));
        await presence.AppendAsync(PresenceSample.Counted(stale, Now.AddDays(-3), 40, FieldSource.Who));

        var listed = (await QueriesOn(db).ListAsync(new GameFilter())).ToDictionary(g => g.Slug);

        await Assert.That(listed["fresh"].PlayersNow).IsEqualTo(15);
        await Assert.That(listed["stale"].PlayersNow).IsNull();
    }

    [Test]
    public async Task AGameWhoseCountsAreAllUnmeasurableIsQuietAndNeverDark()
    {
        // §5.2, and the whole reason the middle presence state exists. Being uncountable is not being
        // absent, and a game whose DOING header we cannot parse is running perfectly well.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "midnight-sun", "Midnight Sun II", lastReachableAt: Now.AddHours(-1));
        var presence = new NpgsqlPresenceStore(db.DataSource);

        for (var hour = 1; hour <= 6; hour++)
        {
            await presence.AppendAsync(
                PresenceSample.Unmeasurable(game, Now.AddHours(-hour), UnmeasurableReason.WhoUnparseable));
        }

        var quiet = await QueriesOn(db).ListAsync(new GameFilter { Band = ActivityBand.Quiet });
        var dark = await QueriesOn(db).ListAsync(new GameFilter { Band = ActivityBand.Dark });

        await Assert.That(quiet.Select(g => g.Slug).ToList()).IsEquivalentTo(new[] { "midnight-sun" });
        await Assert.That(dark).IsEmpty();
    }

    [Test]
    public async Task AGameWeCannotReachIsDarkAndOneWithPlayersIsNot()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var busy = await Seed.GameAsync(db, "busy", "Busy", lastReachableAt: Now.AddMinutes(-5));
        await Seed.GameAsync(db, "gone", "Gone", lastReachableAt: Now.AddDays(-200));
        await new NpgsqlPresenceStore(db.DataSource)
            .AppendAsync(PresenceSample.Counted(busy, Now.AddMinutes(-5), 12, FieldSource.Who));

        var queries = QueriesOn(db);

        await Assert.That((await queries.ListAsync(new GameFilter { Band = ActivityBand.PlayersNow }))
            .Select(g => g.Slug).ToList()).IsEquivalentTo(new[] { "busy" });
        await Assert.That((await queries.ListAsync(new GameFilter { Band = ActivityBand.Dark }))
            .Select(g => g.Slug).ToList()).IsEquivalentTo(new[] { "gone" });
    }

    [Test]
    public async Task AMeasuredZeroIsACountAndKeepsAGameOutOfPlayersNow()
    {
        // We got in and nobody was there. A filled cell on the heatmap, and not a game with players.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "eldertale", "Eldertale Online", lastReachableAt: Now);
        await new NpgsqlPresenceStore(db.DataSource)
            .AppendAsync(PresenceSample.Counted(game, Now.AddMinutes(-5), 0, FieldSource.Who));

        var summary = (await QueriesOn(db).ListAsync(new GameFilter())).Single();
        var page = await QueriesOn(db).FindAsync("eldertale");
        var cell = page!.Activity.Single(c =>
            c.DayOfWeek == (int)Now.AddMinutes(-5).DayOfWeek && c.Hour == Now.AddMinutes(-5).Hour);

        await Assert.That(summary.PlayersNow).IsEqualTo(0);
        await Assert.That(cell.IsCounted).IsTrue();
        await Assert.That(cell.IsGap).IsFalse();
    }

    [Test]
    public async Task TheCapabilityMatrixShowsMeasuredBesideDeclaredAndSaysWhenTheyDisagree()
    {
        // "Declared GMCP, never offered in the handshake" is the single most useful thing a
        // capability matrix can say, and it is why a capability is two fields rather than one.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var fields = new NpgsqlGameFieldStore(db.DataSource);

        await fields.UpsertAsync(new GameField(
            game, CapabilityFields.Measured("GMCP"), FieldSource.Handshake, "false", Now, Now));
        await fields.UpsertAsync(new GameField(
            game, CapabilityFields.Declared("GMCP"), FieldSource.Mssp, "true",
            Now.AddYears(-6), Now.AddYears(-6)));
        await fields.UpsertAsync(new GameField(
            game, CapabilityFields.Measured("MSSP"), FieldSource.Handshake, "true", Now, Now));

        var page = await QueriesOn(db).FindAsync("corvid");
        var gmcp = page!.Capabilities.Single(c => c.Protocol == "GMCP");

        await Assert.That(gmcp.Measured).IsEqualTo(CapabilityState.Absent);
        await Assert.That(gmcp.Declared).IsEqualTo(CapabilityState.Present);
        await Assert.That(gmcp.Disagrees).IsTrue();
        await Assert.That(page.DisagreementCount).IsEqualTo(1);
        await Assert.That(page.Summary.MeasuredProtocols).IsEquivalentTo(new[] { "MSSP" });
    }

    [Test]
    public async Task ACapabilityNobodySaidAnythingAboutIsAbsentFromTheMatrixRatherThanAbsentFromTheGame()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        await new NpgsqlGameFieldStore(db.DataSource).UpsertAsync(new GameField(
            game, CapabilityFields.Measured("GMCP"), FieldSource.Handshake, "true", Now, Now));

        var page = await QueriesOn(db).FindAsync("corvid");

        await Assert.That(page!.Capabilities.Select(c => c.Protocol).ToList())
            .IsEquivalentTo(new[] { "GMCP" });
    }

    [Test]
    public async Task AProvenanceChipCarriesItsSourceAndItsOwnStaleness()
    {
        // There is no unlabelled data on this site, and "old" is not one duration: a six-year-old
        // CREATED is stale and a two-week-old GENRE is not.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var fields = new NpgsqlGameFieldStore(db.DataSource);

        await fields.UpsertAsync(new GameField(
            game, "GENRE", FieldSource.Mssp, "Modern Supernatural", Now.AddDays(-14), Now.AddDays(-14)));
        await fields.UpsertAsync(new GameField(
            game, "CREATED", FieldSource.Mssp, "2009", Now.AddYears(-6), Now.AddYears(-6)));

        var page = await QueriesOn(db).FindAsync("corvid");

        await Assert.That(page!.Declared["genre"].Source).IsEqualTo(FieldSource.Mssp);
        await Assert.That(page.Declared["genre"].IsStale).IsFalse();
        await Assert.That(page.Declared["created"].IsStale).IsTrue();
        await Assert.That(page.Declared["genre"].IsMeasured).IsFalse();
    }

    [Test]
    public async Task TheListingLabelsItsCountAndItsCodebaseWithHowWeKnowThem()
    {
        // Spec §10.1: the listing shipped a count and a codebase as bare values while the page
        // labelled every field. A row has to carry the label, because the listing is a surface a
        // consumer may read on its own.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, lastReachableAt: Now);
        var counted = Now.AddMinutes(-10);

        await new NpgsqlPresenceStore(db.DataSource)
            .AppendAsync(PresenceSample.Counted(game, counted, 15, FieldSource.Who));

        // A codebase last confirmed forty days ago, against a thirty-day window. Old, not wrong.
        await new NpgsqlGameFieldStore(db.DataSource).UpsertAsync(new GameField(
            game, "CODEBASE", FieldSource.Mssp, "PennMUSH 1.8.8p0",
            Now.AddYears(-2), Now.AddDays(-40)));

        var summary = (await QueriesOn(db).ListAsync(new GameFilter())).Single();

        var players = summary.PlayersNowProvenance!;
        await Assert.That(players.Value).IsEqualTo("15");
        await Assert.That(players.Source).IsEqualTo(FieldSource.Who);
        await Assert.That(players.IsMeasured).IsTrue();
        await Assert.That(players.LastConfirmedAt).IsEqualTo(counted);
        await Assert.That(players.IsStale).IsFalse();

        var codebase = summary.CodebaseProvenance!;
        await Assert.That(codebase.Value).IsEqualTo(summary.Codebase);
        await Assert.That(codebase.Source).IsEqualTo(FieldSource.Mssp);
        await Assert.That(codebase.IsMeasured).IsFalse();
        await Assert.That(codebase.LastConfirmedAt).IsEqualTo(Now.AddDays(-40));

        // Staleness is the registry's answer about CODEBASE's own window, not a judgement made here.
        await Assert.That(codebase.IsStale).IsTrue();

        // And the page reads the same fact the same way, from the same rows.
        var page = await QueriesOn(db).FindAsync("corvid");
        await Assert.That(page!.Summary.CodebaseProvenance).IsEqualTo(codebase);
        await Assert.That(page.Declared["codebase"]).IsEqualTo(codebase);
    }

    [Test]
    public async Task ACountAGameAssertedAboutItselfIsNeverLabelledAsOneWeMeasured()
    {
        // Rule 5, at the point it is most tempting to break: an MSSP PLAYERS line is the game's own
        // claim, and a listing that dressed it as a reading of ours would be the exact confusion the
        // incumbents' directories run on. Same number, different fact.
        await using var db = await PostgresFixture.MigratedAsync();
        var measured = await Seed.GameAsync(db, "measured", "Measured");
        var asserted = await Seed.GameAsync(db, "asserted", "Asserted");
        var presence = new NpgsqlPresenceStore(db.DataSource);

        await presence.AppendAsync(PresenceSample.Counted(measured, Now.AddMinutes(-4), 12, FieldSource.Who));
        await presence.AppendAsync(PresenceSample.Counted(asserted, Now.AddMinutes(-4), 12, FieldSource.Mssp));

        var listed = (await QueriesOn(db).ListAsync(new GameFilter())).ToDictionary(g => g.Slug);

        await Assert.That(listed["measured"].PlayersNow).IsEqualTo(listed["asserted"].PlayersNow);
        await Assert.That(listed["measured"].PlayersNowProvenance!.IsMeasured).IsTrue();
        await Assert.That(listed["asserted"].PlayersNowProvenance!.IsMeasured).IsFalse();
        await Assert.That(listed["asserted"].PlayersNowProvenance!.Source).IsEqualTo(FieldSource.Mssp);
    }

    [Test]
    public async Task AValueWeDoNotHaveCarriesNoLabelAtAll()
    {
        // A chip over an absent count would attest to a measurement nobody took, which is worse than
        // the bare value it replaced.
        await using var db = await PostgresFixture.MigratedAsync();
        await Seed.GameAsync(db, lastReachableAt: Now);

        var summary = (await QueriesOn(db).ListAsync(new GameFilter())).Single();

        await Assert.That(summary.PlayersNow).IsNull();
        await Assert.That(summary.PlayersNowProvenance).IsNull();
        await Assert.That(summary.Codebase).IsNull();
        await Assert.That(summary.CodebaseProvenance).IsNull();
    }

    [Test]
    public async Task AGameLookedUpByIdIsTheSameSummaryTheListingReturned()
    {
        // The owner surfaces address a game by id (§5.7) and were handed a summary with neither its
        // labels nor its last-reachable moment — so a claimed listing said "never reached" about a
        // game the public listing showed as reachable minutes ago.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, lastReachableAt: Now.AddMinutes(-5));

        await new NpgsqlPresenceStore(db.DataSource)
            .AppendAsync(PresenceSample.Counted(game, Now.AddMinutes(-5), 3, FieldSource.Who));
        await new NpgsqlGameFieldStore(db.DataSource).UpsertAsync(new GameField(
            game, "CODEBASE", FieldSource.Mssp, "Evennia", Now, Now));

        var listed = (await QueriesOn(db).ListAsync(new GameFilter())).Single();
        var byId = await QueriesOn(db).FindByIdAsync(game);

        await Assert.That(byId!.MeasuredProtocols).IsEquivalentTo(listed.MeasuredProtocols);

        // Everything else compared in one go — the protocols swapped in because a record holding a
        // list compares that list by reference, and two equal lists are the answer here.
        await Assert.That(byId with { MeasuredProtocols = listed.MeasuredProtocols }).IsEqualTo(listed);
    }

    [Test]
    public async Task ThePrecedenceLadderPicksTheWinnerAndTheLoserIsStillStored()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var fields = new NpgsqlGameFieldStore(db.DataSource);

        await fields.UpsertAsync(new GameField(
            game, "CODEBASE", FieldSource.Banner, "PennMUSH 1.8.7", Now, Now));
        await fields.UpsertAsync(new GameField(
            game, "CODEBASE", FieldSource.Mssp, "PennMUSH 1.8.8p0", Now, Now));

        var page = await QueriesOn(db).FindAsync("corvid");

        await Assert.That(page!.Summary.Codebase).IsEqualTo("PennMUSH 1.8.8p0");
        await Assert.That(await fields.ForGameAsync(game)).Count().IsEqualTo(2);
    }

    [Test]
    public async Task AProtocolFacetMatchesOnWhatWasMeasuredAndNotOnWhatWasClaimed()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var measured = await Seed.GameAsync(db, "measured", "Measured");
        var claimed = await Seed.GameAsync(db, "claimed", "Claimed");
        var fields = new NpgsqlGameFieldStore(db.DataSource);

        await fields.UpsertAsync(new GameField(
            measured, CapabilityFields.Measured("GMCP"), FieldSource.Handshake, "true", Now, Now));
        await fields.UpsertAsync(new GameField(
            claimed, CapabilityFields.Declared("GMCP"), FieldSource.Mssp, "true", Now, Now));

        var listed = await QueriesOn(db).ListAsync(new GameFilter { MeasuredProtocols = ["GMCP"] });

        await Assert.That(listed.Select(g => g.Slug).ToList()).IsEquivalentTo(new[] { "measured" });
    }

    [Test]
    public async Task AnHourWithNoSampleIsAGapAndAnHourWithNoCountIsHatched()
    {
        // The three renderings of §5.4, read back off the grid the page draws.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var presence = new NpgsqlPresenceStore(db.DataSource);
        var counted = Now.AddDays(-1);
        var hatched = Now.AddDays(-2);

        await presence.AppendAsync(PresenceSample.Counted(game, counted, 9, FieldSource.Who));
        await presence.AppendAsync(
            PresenceSample.Unmeasurable(game, hatched, UnmeasurableReason.WhoUnparseable));

        var page = await QueriesOn(db).FindAsync("corvid");
        var cells = page!.Activity;

        await Assert.That(cells).Count().IsEqualTo(7 * 24);
        await Assert.That(Cell(cells, counted).IsCounted).IsTrue();
        await Assert.That(Cell(cells, hatched).IsUnmeasurable).IsTrue();
        await Assert.That(Cell(cells, Now.AddDays(-3)).IsGap).IsTrue();
    }

    [Test]
    public async Task TheFeedsSayWhatWasFoundWhatWentDarkAndWhatCameBack()
    {
        // §9's three liveness feeds — the differentiator no incumbent can publish, because none of
        // them measured continuously enough to know when a game came back.
        await using var db = await PostgresFixture.MigratedAsync();
        var found = await Seed.GameAsync(db, "eldertale", "Eldertale Online", firstSeenAt: Now.AddHours(-2));
        var dark = await Seed.GameAsync(db, "gaslight-row", "Gaslight Row", firstSeenAt: Now.AddYears(-4));
        var back = await Seed.GameAsync(db, "aardwolf", "Aardwolf MUD", firstSeenAt: Now.AddYears(-4));
        var availability = new NpgsqlAvailabilityStore(db.DataSource);

        await availability.OpenAsync(new AvailabilityInterval
        {
            GameId = dark,
            State = AvailabilityState.Unreachable,
            FromAt = Now.AddDays(-6),
            Cause = FailureCause.Dns,
        });
        await availability.OpenAsync(new AvailabilityInterval
        {
            GameId = back,
            State = AvailabilityState.Unreachable,
            FromAt = Now.AddDays(-800),
            ToAt = Now.AddMinutes(-40),
            Cause = FailureCause.Timeout,
        });
        await availability.OpenAsync(new AvailabilityInterval
        {
            GameId = back,
            State = AvailabilityState.Reachable,
            FromAt = Now.AddMinutes(-40),
        });

        var feeds = await QueriesOn(db).FeedsAsync();

        await Assert.That(feeds.NewlyDiscovered.Select(f => f.Slug).ToList())
            .IsEquivalentTo(new[] { "eldertale" });
        await Assert.That(feeds.WentDark.Select(f => f.Slug).ToList())
            .IsEquivalentTo(new[] { "gaslight-row" });
        await Assert.That(feeds.WentDark[0].Detail).Contains("dns");
        await Assert.That(feeds.CameBack.Select(f => f.Slug).ToList())
            .IsEquivalentTo(new[] { "aardwolf" });

        // Each entry names its game by the identifier that survives a rename (§5.7), from the query
        // that already had the row. Nothing downstream has to read the catalogue to find out which
        // game an event was about.
        await Assert.That(feeds.NewlyDiscovered[0].Id).IsEqualTo(found);
        await Assert.That(feeds.WentDark[0].Id).IsEqualTo(dark);
        await Assert.That(feeds.CameBack[0].Id).IsEqualTo(back);
    }

    [Test]
    public async Task ThePageCarriesTheReachabilityArithmeticAndNeverCallsItUptime()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var availability = new NpgsqlAvailabilityStore(db.DataSource);

        await availability.OpenAsync(new AvailabilityInterval
        {
            GameId = game,
            State = AvailabilityState.Unreachable,
            FromAt = Now.AddDays(-40),
            ToAt = Now.AddDays(-30),
            Cause = FailureCause.Timeout,
        });
        await availability.OpenAsync(new AvailabilityInterval
        {
            GameId = game,
            State = AvailabilityState.Reachable,
            FromAt = Now.AddDays(-30),
        });

        var page = await QueriesOn(db).FindAsync("corvid");

        await Assert.That(page!.ReachableFraction!.Value).IsEqualTo(0.75).Within(0.001);
        await Assert.That(page.LongestOutage!.Value.TotalDays).IsEqualTo(10).Within(0.001);
    }

    [Test]
    public async Task AGameWeHaveNeverProbedIsUnmeasuredRatherThanZeroPercentReachable()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        await Seed.GameAsync(db);

        var page = await QueriesOn(db).FindAsync("corvid");

        await Assert.That(page!.ReachableFraction).IsNull();
        await Assert.That(page.LongestOutage).IsNull();
    }

    [Test]
    public async Task TheChangeFeedIsEventsThatActuallyHappened()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var reconciler = new FieldReconciler(new NpgsqlGameFieldStore(db.DataSource));

        await reconciler.ApplyAsync(
            game, [new FieldObservation("CODEBASE", FieldSource.Mssp, "PennMUSH 1.8.7")], Now.AddDays(-30));
        await reconciler.ApplyAsync(
            game, [new FieldObservation("CODEBASE", FieldSource.Mssp, "PennMUSH 1.8.7")], Now.AddDays(-20));
        await reconciler.ApplyAsync(
            game, [new FieldObservation("CODEBASE", FieldSource.Mssp, "PennMUSH 1.8.8p0")], Now.AddDays(-16));

        var page = await QueriesOn(db).FindAsync("corvid");

        await Assert.That(page!.Changes).Count().IsEqualTo(1);
        await Assert.That(page.Changes[0].Summary).Contains("PennMUSH 1.8.8p0");
        await Assert.That(page.Changes[0].At).IsEqualTo(Now.AddDays(-16));
    }

    [Test]
    public async Task AConnectScreenIsAFieldAndCanBeSuppressed()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var fields = new NpgsqlGameFieldStore(db.DataSource);

        await fields.UpsertAsync(new GameField(
            game, "connect_screen", FieldSource.Banner, "Welcome to Corvid", Now, Now));
        await fields.UpsertAsync(new GameField(
            game, "connect_screen_suppressed", FieldSource.Owner, "true", Now, Now));

        var page = await QueriesOn(db).FindAsync("corvid");

        await Assert.That(page!.ConnectScreen).IsEqualTo("Welcome to Corvid");
        await Assert.That(page.ConnectScreenSuppressed).IsTrue();

        // And neither of ours shows up among the game's own declared fields.
        await Assert.That(page.Declared.Keys).IsEmpty();
    }

    [Test]
    public async Task AnEndpointOnThePageSaysWhetherTlsWasMeasured()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var endpoints = new NpgsqlEndpointStore(db.DataSource);

        await endpoints.UpsertAsync(new GameEndpoint(
            game, "mush.pennmush.org", 4201, EndpointKind.Telnet, Now, Now, EndpointState.Active));
        await endpoints.UpsertAsync(new GameEndpoint(
            game, "mush.pennmush.org", 4202, EndpointKind.Tls, Now, Now, EndpointState.Active));

        var page = await QueriesOn(db).FindAsync("corvid");

        await Assert.That(page!.Endpoints).Count().IsEqualTo(2);
        await Assert.That(page.Endpoints.Single(e => e.Port == 4202).TlsMeasured).IsTrue();
        await Assert.That(page.Endpoints.Single(e => e.Port == 4201).TlsMeasured).IsFalse();
    }

    private static ActivityCell Cell(IReadOnlyList<ActivityCell> cells, DateTimeOffset at) =>
        cells.Single(c => c.DayOfWeek == (int)at.UtcDateTime.DayOfWeek && c.Hour == at.UtcDateTime.Hour);
}
