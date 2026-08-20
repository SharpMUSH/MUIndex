using Dapper;

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

    /// <summary>The account an unlisting is attributed to, since the schema will not take one without.</summary>
    private static readonly Guid Owner = Guid.Parse("cccccccc-0000-0000-0000-000000000001");

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

    /// <summary>
    /// The archive checkbox lifts the archive, and neither of the other two states that withhold a
    /// game from the listing.
    /// </summary>
    /// <remarks>
    /// Previously read <c>@includeArchived OR state NOT IN ('archived', 'excluded')</c>, so ticking
    /// "show the archive" also revealed every excluded game — asking one question, answered
    /// another. <c>unlisted</c> would have inherited the same bug and been worse: the one state
    /// whose entire purpose is that browsing does not reach it.
    /// </remarks>
    [Test]
    public async Task TheArchiveToggleRevealsTheArchiveAndNothingElse()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        await Seed.GameAsync(db, "corvid", "Corvid");
        await Seed.GameAsync(db, "gaslight-row", "Gaslight Row", LifecycleState.Archived);
        var stock = await Seed.GameAsync(db, "your-mud-name", "Your MUD Name");
        var asked = await Seed.GameAsync(db, "asked-to-come-out", "Asked To Come Out");

        var games = new NpgsqlGameStore(db.DataSource);
        await games.ExcludeAsync(stock, "Stock configuration.", Now);

        await OwnerRowAsync(db);
        await games.UnlistAsync(asked, Owner, Now);

        var queries = QueriesOn(db);
        var withArchive = await queries.ListAsync(new GameFilter { IncludeArchived = true });

        await Assert.That(withArchive.Select(g => g.Slug).ToList())
            .IsEquivalentTo(new[] { "corvid", "gaslight-row" });

        // Both pages still answer. Out of the listing is the whole of what either state does.
        await Assert.That((await queries.FindAsync("your-mud-name"))!.Summary.State)
            .IsEqualTo(LifecycleState.Excluded);
        await Assert.That((await queries.FindAsync("asked-to-come-out"))!.Summary.State)
            .IsEqualTo(LifecycleState.Unlisted);
    }

    /// <summary>
    /// The listing is not the only way in, so the two never-browsable states leave the feeds and the
    /// referral links as well.
    /// </summary>
    /// <remarks>
    /// Both surfaces read <c>Public</c> alone, which says who vouched for a game and nothing about
    /// its lifecycle — so a game taken out of the listing kept appearing as newly discovered and as
    /// a link on its neighbour's page. <c>archived</c> stays in both deliberately: the archive is a
    /// browsable section in its own right.
    /// </remarks>
    [Test]
    public async Task NeitherTheFeedsNorTheNeighboursOfferAGameThatIsNotBrowsable()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        // First seen yesterday, all three, because the discovered feed's window is the only thing
        // that decides whether a game is in it — and the default seed is two years old.
        var seen = Now.AddDays(-1);
        var lister = await Seed.GameAsync(db, "corvid", "Corvid", firstSeenAt: seen);
        var stock = await Seed.GameAsync(db, "your-mud-name", "Your MUD Name", firstSeenAt: seen);
        var asked = await Seed.GameAsync(db, "asked-to-come-out", "Asked To Come Out", firstSeenAt: seen);

        await EndpointAsync(db, stock, "stock.example.org", 4201);
        await EndpointAsync(db, asked, "asked.example.org", 4201);
        await EdgeAsync(db, lister, "stock.example.org", 4201);
        await EdgeAsync(db, lister, "asked.example.org", 4201);

        var games = new NpgsqlGameStore(db.DataSource);
        await games.ExcludeAsync(stock, "Stock configuration.", Now);
        await OwnerRowAsync(db);
        await games.UnlistAsync(asked, Owner, Now);

        var queries = QueriesOn(db);
        var feeds = await queries.FeedsAsync();
        var neighbours = (await queries.FindAsync("corvid"))!.Referrals;

        await Assert.That(feeds.NewlyDiscovered.Select(f => f.Slug).ToList())
            .IsEquivalentTo(new[] { "corvid" });
        await Assert.That(neighbours).IsEmpty();
    }

    [Test]
    public async Task TheNewlyDiscoveredFeedShowsTwelve()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var seen = Now.AddDays(-1);

        for (var i = 0; i < 16; i++)
        {
            await Seed.GameAsync(db, $"discovered-{i}", $"Discovered {i}", firstSeenAt: seen.AddSeconds(-i));
        }

        var feeds = await QueriesOn(db).FeedsAsync();

        await Assert.That(feeds.NewlyDiscovered.Count).IsEqualTo(12);
    }

    /// <summary>The argument behind an exclusion reaches the page that carries the decision.</summary>
    /// <remarks>
    /// An unlisting has no counterpart here on purpose: it is not our argument to print.
    /// </remarks>
    [Test]
    public async Task AnExclusionsReasonIsOnItsPage()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var stock = await Seed.GameAsync(db, "lpcdb", "lpcdb");

        await new NpgsqlGameStore(db.DataSource)
            .ExcludeAsync(stock, "A tool on the network rather than a game.", Now);

        var page = await QueriesOn(db).FindAsync("lpcdb");

        await Assert.That(page!.ExcludedReason).IsEqualTo("A tool on the network rather than a game.");
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
        // Rule 5: an MSSP PLAYERS line is the game's own claim, and labelling it as a reading of
        // ours would be the exact confusion the incumbent directories run on. Same number,
        // different fact.
        //
        // Three sources reach this column and are three different facts. `banner` is on the
        // measured side — we parse that number off the connect screen ourselves, on every probe —
        // and stays distinguishable from `who` by its source rather than by its verdict.
        await using var db = await PostgresFixture.MigratedAsync();
        var measured = await Seed.GameAsync(db, "measured", "Measured");
        var asserted = await Seed.GameAsync(db, "asserted", "Asserted");
        var read = await Seed.GameAsync(db, "read", "Read off the screen");
        var presence = new NpgsqlPresenceStore(db.DataSource);

        await presence.AppendAsync(PresenceSample.Counted(measured, Now.AddMinutes(-4), 12, FieldSource.Who));
        await presence.AppendAsync(PresenceSample.Counted(asserted, Now.AddMinutes(-4), 12, FieldSource.Mssp));
        await presence.AppendAsync(PresenceSample.Counted(read, Now.AddMinutes(-4), 12, FieldSource.Banner));

        var listed = (await QueriesOn(db).ListAsync(new GameFilter())).ToDictionary(g => g.Slug);

        await Assert.That(listed["measured"].PlayersNow).IsEqualTo(listed["asserted"].PlayersNow);
        await Assert.That(listed["measured"].PlayersNowProvenance!.IsMeasured).IsTrue();
        await Assert.That(listed["asserted"].PlayersNowProvenance!.IsMeasured).IsFalse();
        await Assert.That(listed["asserted"].PlayersNowProvenance!.Source).IsEqualTo(FieldSource.Mssp);

        await Assert.That(listed["read"].PlayersNowProvenance!.IsMeasured).IsTrue();
        await Assert.That(listed["read"].PlayersNowProvenance!.Source).IsEqualTo(FieldSource.Banner);
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
    public async Task APageAskedForByIdIsTheSamePageAskedForBySlug()
    {
        // Two keys for one game (§5.7); both routes must read one path so they cannot disagree.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, lastReachableAt: Now);

        await new NpgsqlPresenceStore(db.DataSource)
            .AppendAsync(PresenceSample.Counted(game, Now.AddMinutes(-5), 7, FieldSource.Who));
        await new NpgsqlGameFieldStore(db.DataSource).UpsertAsync(new GameField(
            game, "CODEBASE", FieldSource.Mssp, "PennMUSH 1.8.8p0", Now, Now));

        var bySlug = await QueriesOn(db).FindAsync("corvid");
        var byId = await QueriesOn(db).FindAsync(game);

        await Assert.That(byId!.Summary.Slug).IsEqualTo(bySlug!.Summary.Slug);
        await Assert.That(byId.Summary.PlayersNowProvenance)
            .IsEqualTo(bySlug.Summary.PlayersNowProvenance);
        await Assert.That(byId.Summary.CodebaseProvenance)
            .IsEqualTo(bySlug.Summary.CodebaseProvenance);
        await Assert.That(byId.Declared["codebase"]).IsEqualTo(bySlug.Declared["codebase"]);
        await Assert.That(byId.Activity).Count().IsEqualTo(bySlug.Activity.Count);

        // An id is minted once and never reused, so a miss is a miss and has nowhere else to look.
        await Assert.That(await QueriesOn(db).FindAsync(Guid.NewGuid())).IsNull();
    }

    [Test]
    public async Task AnArchivedGameStillAnswersToItsId()
    {
        // Archiving takes a game out of the default listing and out of nothing else (§7.5); a
        // lookup by id must not quietly drop it.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "gaslight-row", "Gaslight Row", LifecycleState.Archived);

        var page = await QueriesOn(db).FindAsync(game);

        await Assert.That(page).IsNotNull();
        await Assert.That(page!.Summary.State).IsEqualTo(LifecycleState.Archived);
    }

    [Test]
    public async Task AGameLookedUpByIdIsTheSameSummaryTheListingReturned()
    {
        // The owner surfaces address a game by id (§5.7) — a claimed listing must not disagree with
        // the public listing about the same game's reachability.
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

    /// <summary>
    /// §9's referral neighbours, both arrows, off the reverse index that had no reader.
    /// </summary>
    /// <remarks>
    /// Kept apart because the two are different people's claims: "our list names them" is what this
    /// game published, "their list names us" is what somebody else did. Merged into one bag, each
    /// game's list would be attributed to the other.
    /// </remarks>
    [Test]
    public async Task AGamePageNamesWhoItListsAndWhoListsIt()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var corvid = await Seed.GameAsync(db);
        var listed = await Seed.GameAsync(db, "eldertale", "Eldertale Online");
        var lister = await Seed.GameAsync(db, "aardwolf", "Aardwolf MUD");

        var endpoints = new NpgsqlEndpointStore(db.DataSource);

        await endpoints.UpsertAsync(new GameEndpoint(
            corvid, "corvid.example", 4201, EndpointKind.Telnet, Now, Now, EndpointState.Active));
        await endpoints.UpsertAsync(new GameEndpoint(
            listed, "eldertale.example", 4000, EndpointKind.Telnet, Now, Now, EndpointState.Active));

        await EdgeAsync(db, corvid, "eldertale.example", 4000);
        await EdgeAsync(db, lister, "corvid.example", 4201);

        var page = await QueriesOn(db).FindAsync("corvid");
        var referrals = page!.Referrals;

        await Assert.That(referrals).Count().IsEqualTo(2);
        await Assert.That(referrals.Single(n => n.Direction is ReferralDirection.Lists).Slug)
            .IsEqualTo("eldertale");
        await Assert.That(referrals.Single(n => n.Direction is ReferralDirection.ListedBy).Slug)
            .IsEqualTo("aardwolf");
    }

    /// <summary>
    /// Being named by somebody's referral is not a way onto a public surface.
    /// </summary>
    /// <remarks>
    /// The rule that keeps an unclaimed submission off every public page applies to a neighbour
    /// list too — otherwise anyone could put a game they submitted onto a stranger's page by
    /// publishing a REFERRAL to it.
    /// </remarks>
    [Test]
    public async Task AnUnclaimedSubmissionIsNotANeighbour()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var corvid = await Seed.GameAsync(db);
        var submitted = await Seed.GameAsync(db, "hearsay", "Hearsay");

        await using (var connection = await db.DataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync(
                "UPDATE game SET submitted_at = @now, is_claimed = false WHERE id = @submitted",
                new { now = Now, submitted });
        }

        var endpoints = new NpgsqlEndpointStore(db.DataSource);

        await endpoints.UpsertAsync(new GameEndpoint(
            corvid, "corvid.example", 4201, EndpointKind.Telnet, Now, Now, EndpointState.Active));
        await endpoints.UpsertAsync(new GameEndpoint(
            submitted, "hearsay.example", 4000, EndpointKind.Telnet, Now, Now, EndpointState.Active));

        await EdgeAsync(db, corvid, "hearsay.example", 4000);
        await EdgeAsync(db, submitted, "corvid.example", 4201);

        await Assert.That((await QueriesOn(db).FindAsync("corvid"))!.Referrals).IsEmpty();
    }

    /// <summary>An address the game has left is rendered as left, not dropped (spec §7.5).</summary>
    [Test]
    public async Task AnEndpointCarriesTheHistoryTheTableAlwaysKept()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);

        await new NpgsqlEndpointStore(db.DataSource).UpsertAsync(new GameEndpoint(
            game,
            "corvid.example",
            4201,
            EndpointKind.Telnet,
            Now.AddYears(-3),
            Now.AddMonths(-8),
            EndpointState.Gone));

        var endpoint = (await QueriesOn(db).FindAsync("corvid"))!.Endpoints.Single();

        await Assert.That(endpoint.State).IsEqualTo("gone");
        await Assert.That(endpoint.IsCurrent).IsFalse();
        await Assert.That(endpoint.FirstSeenAt).IsEqualTo(Now.AddYears(-3));
        await Assert.That(endpoint.LastSeenAt).IsEqualTo(Now.AddMonths(-8));
    }

    /// <summary>An address a game answers on, so a referral edge has something to land on.</summary>
    private static Task EndpointAsync(TestDatabase db, Guid game, string host, int port) =>
        new NpgsqlEndpointStore(db.DataSource).UpsertAsync(
            new GameEndpoint(game, host, port, EndpointKind.Telnet, Now, Now, EndpointState.Active));

    /// <summary>The account an unlisting is attributed to. The schema will not take one without.</summary>
    private static async Task OwnerRowAsync(TestDatabase db)
    {
        await using var connection = await db.DataSource.OpenConnectionAsync();

        await connection.ExecuteAsync(
            """
            INSERT INTO app_user (
                id, display_name, normalised_name, security_stamp, concurrency_stamp, created_at)
            VALUES (@user, 'admin', 'ADMIN', 'stamp', 'stamp', @at)
            ON CONFLICT (id) DO NOTHING
            """,
            new { user = Owner, at = Now });
    }

    private static async Task EdgeAsync(TestDatabase db, Guid from, string host, int port)
    {
        await using var connection = await db.DataSource.OpenConnectionAsync();

        await connection.ExecuteAsync(
            """
            INSERT INTO referral_edge (from_game_id, to_host, to_port, first_seen_at, last_seen_at)
            VALUES (@from, @host, @port, @now, @now)
            """,
            new { from, host, port, now = Now });
    }

    [Test]
    public async Task TheGridSurvivesTheRawSamplesBeingDroppedBehindTheRollup()
    {
        // §5.2: raw samples may be dropped only after the rollup has consumed them, so a cell whose
        // only surviving evidence is a rolled-up bucket must still render as what it was — a hatched
        // hour re-read as a gap would be §5.4's worst collapse arriving by way of retention.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var presence = new NpgsqlPresenceStore(db.DataSource);
        var counted = Now.AddDays(-30);
        var hatched = Now.AddDays(-31);

        await presence.AppendAsync(PresenceSample.Counted(game, counted, 9, FieldSource.Who));
        await presence.AppendAsync(
            PresenceSample.Unmeasurable(game, hatched, UnmeasurableReason.WhoUnparseable));

        var rollups = new NpgsqlPresenceRollupStore(db.DataSource);
        await rollups.RollUpAsync(PresenceGrain.Hour, Now.AddDays(-56), Now, default);
        await rollups.SetWatermarkAsync(PresenceGrain.Hour, Now, default);

        // What retention does, at the only moment it is allowed to: after the rollup has consumed it.
        await using (var command = db.DataSource.CreateCommand("DELETE FROM presence_sample"))
        {
            await command.ExecuteNonQueryAsync();
        }

        var cells = (await QueriesOn(db).FindAsync("corvid"))!.Activity;

        await Assert.That(Cell(cells, counted).IsCounted).IsTrue();
        await Assert.That(Cell(cells, counted).Count).IsEqualTo(9);
        await Assert.That(Cell(cells, hatched).IsUnmeasurable).IsTrue();
    }

    [Test]
    public async Task AnHourProbedSinceTheLastRollupIsStillOnTheGrid()
    {
        // The rollup only consumes whole elapsed hours, so the newest hours are always ahead of the
        // watermark. Reading the rollup alone would render a probe from ten minutes ago as an hour
        // nobody measured.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var presence = new NpgsqlPresenceStore(db.DataSource);
        var rolled = Now.AddDays(-10);
        var fresh = Now.AddMinutes(-10);

        await presence.AppendAsync(PresenceSample.Counted(game, rolled, 4, FieldSource.Who));
        await presence.AppendAsync(PresenceSample.Counted(game, fresh, 7, FieldSource.Who));

        var rollups = new NpgsqlPresenceRollupStore(db.DataSource);
        var boundary = Now.AddDays(-1);

        await rollups.RollUpAsync(PresenceGrain.Hour, Now.AddDays(-56), boundary, default);
        await rollups.SetWatermarkAsync(PresenceGrain.Hour, boundary, default);

        var cells = (await QueriesOn(db).FindAsync("corvid"))!.Activity;

        await Assert.That(Cell(cells, rolled).Count).IsEqualTo(4);
        await Assert.That(Cell(cells, fresh).Count).IsEqualTo(7);
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

    /// <summary>
    /// The screen's caption says what the probe applied, and never what somebody asked for.
    /// </summary>
    /// <remarks>
    /// <c>WireEncoding.Override</c> ignores an override naming an encoding the runtime does not
    /// have, falling back to reading the bytes the ordinary way. Captioning from the raw
    /// <c>CHARSET</c> row instead would print a caption that is not true of the screen.
    /// <c>charset.read</c> exists only where the encoding was actually determined, so an unusable
    /// override yields no caption at all.
    /// </remarks>
    [Test]
    public async Task AnUnusableOverrideCaptionsNothingRatherThanItself()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var fields = new NpgsqlGameFieldStore(db.DataSource);

        await fields.UpsertAsync(new GameField(
            game, "connect_screen", FieldSource.Banner, "Welcome to Corvid", Now, Now));
        await fields.UpsertAsync(new GameField(
            game, "CHARSET", FieldSource.Staff, "not-an-encoding", Now, Now));

        await Assert.That((await QueriesOn(db).FindAsync("corvid"))!.ConnectScreenCharset).IsNull();

        // The probe's own answer is what the caption comes from, and it says gb2312 because that is
        // what the bytes were read with — not "gbk", which is what somebody typed.
        await fields.UpsertAsync(new GameField(
            game, "charset.read", FieldSource.Staff, "gb2312", Now, Now));

        await Assert.That((await QueriesOn(db).FindAsync("corvid"))!.ConnectScreenCharset)
            .IsEqualTo("gb2312");
    }

    /// <summary>UTF-8 is the ordinary case and earns no caption.</summary>
    /// <remarks>
    /// A caption on every ordinary screen would bury the handful where the encoding is the
    /// interesting fact.
    /// </remarks>
    [Test]
    public async Task AnOrdinaryUtf8ScreenSaysNothingAboutItsEncoding()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var fields = new NpgsqlGameFieldStore(db.DataSource);

        await fields.UpsertAsync(new GameField(
            game, "connect_screen", FieldSource.Banner, "Welcome to Corvid", Now, Now));
        await fields.UpsertAsync(new GameField(
            game, "charset.read", FieldSource.Handshake, "utf-8", Now, Now));

        var page = await QueriesOn(db).FindAsync("corvid");

        await Assert.That(page!.ConnectScreenCharset).IsNull();

        // And it is machinery, so it stays out of "Declared by the game" — it is ours, not theirs.
        await Assert.That(page.Declared.Keys).IsEmpty();
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
