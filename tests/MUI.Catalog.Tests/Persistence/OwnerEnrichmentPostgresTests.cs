using Dapper;

using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// Spec §8.5 and §11 against a real database: what a verified owner may write, and what happens to
/// the measurements while they do it.
/// </summary>
/// <remarks>
/// The property under test throughout is that an owner's answer is a <em>row of its own</em>. It is
/// keyed <c>(game, field, source)</c>, so a declared value and a measured one cannot contend; these
/// assert that the second half of that key is doing its job rather than that somebody remembered to
/// pass <c>FieldSource.Owner</c>.
/// </remarks>
public class OwnerEnrichmentPostgresTests
{
    private static readonly DateTimeOffset Now = Seed.Now;

    /// <summary>The four fields §3.2 names as genuinely absent from MSSP, and not one more.</summary>
    /// <remarks>
    /// Pinned as a list because the writable set is derived from a flag: a field marked
    /// <c>ownerEnrichable</c> becomes editable on the dashboard and writable through the endpoint in
    /// one edit, with no second review anywhere. This is that review.
    /// </remarks>
    [Test]
    public async Task TheWritableSetIsExactlyWhatMsspHasNoRoomFor()
    {
        await Assert.That(FieldRegistry.OwnerEnrichable.Select(d => d.Name))
            .IsEquivalentTo(new[]
            {
                "FANDOM", "APPLICATION PROCESS", "RP ENFORCEMENT", "CONSENT TOOLS",
            });
    }

    /// <summary>
    /// The thing a claim is for: an owner adds what no probe can ask for, and it lands as declared.
    /// </summary>
    [Test]
    public async Task AVerifiedOwnerAddsWhatMsspHasNoFieldForAndItIsStoredAsDeclared()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var world = await World.BuildAsync(db);

        var outcome = await world.Enrichment.ApplyAsync(
            world.Game,
            world.Owner,
            [new OwnerEdit("FANDOM", "Exalted"), new OwnerEdit("RP ENFORCEMENT", "Staff-adjudicated")]);

        await Assert.That(outcome.IsApplied).IsTrue();

        var page = await world.Queries.FindAsync("corvid");

        await Assert.That(page!.Declared["fandom"].Value).IsEqualTo("Exalted");
        await Assert.That(page.Declared["fandom"].Source).IsEqualTo(FieldSource.Owner);

        // Declared, never measured — the chip, the plain surface and the API all read this one flag.
        await Assert.That(page.Declared["fandom"].IsMeasured).IsFalse();
        await Assert.That(page.Declared["rp enforcement"].Value).IsEqualTo("Staff-adjudicated");
    }

    /// <summary>
    /// §8.5's line. A write to a measured field is refused out loud, and takes the submission with it.
    /// </summary>
    /// <remarks>
    /// The form never offers <c>CODEBASE</c>, so this is a hand-assembled post — which is exactly the
    /// case that must not get half of what it asked for. A silent drop would teach an owner the site
    /// is broken; a success would make the whole site a self-report with extra steps.
    /// </remarks>
    [Test]
    public async Task AnOwnerMayNotEditAMeasurementAndIsToldWhichFieldItWas()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var world = await World.BuildAsync(db);

        await world.Fields.UpsertAsync(new GameField(
            world.Game, "CODEBASE", FieldSource.Mssp, "PennMUSH 1.8.8p0", Now, Now));

        var outcome = await world.Enrichment.ApplyAsync(
            world.Game,
            world.Owner,
            [new OwnerEdit("FANDOM", "Exalted"), new OwnerEdit("CODEBASE", "PennMUSH 9.9.9")]);

        await Assert.That(outcome.Verdict).IsEqualTo(EnrichmentVerdict.NotEnrichable);
        await Assert.That(outcome.Field).IsEqualTo("CODEBASE");

        var stored = await world.Fields.ForGameAsync(world.Game);

        // Neither half of the submission landed, and the measurement is untouched.
        await Assert.That(stored.Any(f => f.Source is FieldSource.Owner)).IsFalse();
        await Assert.That(stored.Single(f => f.Field == "CODEBASE").Value)
            .IsEqualTo("PennMUSH 1.8.8p0");
    }

    /// <summary>A capability is a measurement whatever the naming convention makes it look like.</summary>
    [Test]
    public async Task ACapabilityIsNotEnrichableEither()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var world = await World.BuildAsync(db);

        foreach (var field in new[] { CapabilityFields.Measured("GMCP"), CapabilityFields.Declared("GMCP"), "PLAYERS" })
        {
            var outcome = await world.Enrichment.ApplyAsync(
                world.Game, world.Owner, [new OwnerEdit(field, "true")]);

            await Assert.That(outcome.Verdict).IsEqualTo(EnrichmentVerdict.NotEnrichable);
        }

        await Assert.That((await world.Fields.ForGameAsync(world.Game)).Count).IsEqualTo(0);
    }

    /// <summary>
    /// An owner's value sits beside the measurements rather than over them.
    /// </summary>
    /// <remarks>
    /// Asserted over every non-owner row rather than over the one field the test wrote, because the
    /// failure this guards against is not "the wrong field moved" — it is a write path that
    /// confirmed, restamped or replaced rows it was never asked about.
    /// </remarks>
    [Test]
    public async Task AnOwnersWriteLeavesEveryMeasuredRowExactlyWhereItWas()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var world = await World.BuildAsync(db);

        await world.Fields.UpsertAsync(new GameField(
            world.Game, "GENRE", FieldSource.Mssp, "Fantasy", Now.AddYears(-1), Now.AddYears(-1)));
        await world.Fields.UpsertAsync(new GameField(
            world.Game, CapabilityFields.Measured("GMCP"), FieldSource.Handshake, "true", Now, Now));

        var before = (await world.Fields.ForGameAsync(world.Game)).ToList();

        await world.Enrichment.ApplyAsync(world.Game, world.Owner, [new OwnerEdit("FANDOM", "Exalted")]);

        var after = (await world.Fields.ForGameAsync(world.Game))
            .Where(f => f.Source is not FieldSource.Owner)
            .ToList();

        await Assert.That(after).IsEquivalentTo(before);
    }

    /// <summary>A pending claim is an account that asked; asking is not proving (§8.1).</summary>
    [Test]
    public async Task APendingClaimGrantsNothing()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var world = await World.BuildAsync(db);
        var stranger = await world.UserAsync("stranger");

        await world.Claims.IssueAsync(world.Game, stranger);

        var outcome = await world.Enrichment.ApplyAsync(
            world.Game, stranger, [new OwnerEdit("FANDOM", "Exalted")]);

        await Assert.That(outcome.Verdict).IsEqualTo(EnrichmentVerdict.NotAnOwner);
        await Assert.That((await world.Fields.ForGameAsync(world.Game)).Count).IsEqualTo(0);
    }

    /// <summary>A verified claim reaches the game it was verified against, and no other.</summary>
    [Test]
    public async Task AClaimOnOneGameDoesNotReachAnother()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var world = await World.BuildAsync(db);
        var other = await Seed.GameAsync(db, slug: "rookery", name: "Rookery");

        var outcome = await world.Enrichment.ApplyAsync(
            other, world.Owner, [new OwnerEdit("FANDOM", "Exalted")]);

        await Assert.That(outcome.Verdict).IsEqualTo(EnrichmentVerdict.NotAnOwner);
        await Assert.That((await world.Fields.ForGameAsync(other)).Count).IsEqualTo(0);
    }

    /// <summary>
    /// Clearing is a new value, not an erasure — and the record of what it said survives.
    /// </summary>
    [Test]
    public async Task ClearingAFieldKeepsTheRowAndPutsTheWithdrawalInTheChangeFeed()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var world = await World.BuildAsync(db);

        await world.Enrichment.ApplyAsync(world.Game, world.Owner, [new OwnerEdit("FANDOM", "Exalted")]);
        await world.Enrichment.ApplyAsync(world.Game, world.Owner, [new OwnerEdit("FANDOM", "  ")]);

        var row = (await world.Fields.ForGameAsync(world.Game))
            .Single(f => f.Field == "FANDOM" && f.Source is FieldSource.Owner);

        await Assert.That(row.Value).IsEqualTo(string.Empty);

        var changes = await world.Fields.ChangesAsync(world.Game, 20);

        await Assert.That(changes.Count).IsEqualTo(1);
        await Assert.That(changes[0].OldValue).IsEqualTo("Exalted");
        await Assert.That(changes[0].NewValue).IsEqualTo(string.Empty);

        // Nothing to show, so nothing is shown — and the feed says what happened rather than
        // trailing off after "changed from Exalted to".
        var page = await world.Queries.FindAsync("corvid");

        await Assert.That(page!.Declared.ContainsKey("fandom")).IsFalse();
        await Assert.That(page.Changes.Single().Summary).Contains("from Exalted to nothing");
    }

    /// <summary>
    /// Clearing an owner value reveals what is under it, and never silences it.
    /// </summary>
    /// <remarks>
    /// The one way an owner could still edit a measurement, and it took a form nobody would think to
    /// look at. <c>owner</c> outranks <c>mssp</c> in §5.1's ladder for enrichment fields, so a
    /// <em>cleared</em> owner row — empty value, still the precedence winner — was chosen as the
    /// winner and then dropped for being empty, taking the whole field with it. A game that publishes
    /// an unofficial <c>FANDOM</c> in its own MSSP could have had it removed from the page, the plain
    /// surface and the API by the owner typing a space into a box.
    ///
    /// Emptiness is absence, so it is filtered before the ladder runs rather than after.
    /// </remarks>
    [Test]
    public async Task ClearingAnOwnerValueUncoversTheMeasurementRatherThanHidingIt()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var world = await World.BuildAsync(db);

        // A game whose own MSSP report carries the same unofficial variable the owner may enrich.
        await world.Fields.UpsertAsync(new GameField(
            world.Game, "FANDOM", FieldSource.Mssp, "Ars Magica", Now.AddYears(-1), Now));

        await world.Enrichment.ApplyAsync(world.Game, world.Owner, [new OwnerEdit("FANDOM", "Exalted")]);

        await Assert.That((await world.Queries.FindAsync("corvid"))!.Declared["fandom"].Value)
            .IsEqualTo("Exalted");

        await world.Enrichment.ApplyAsync(world.Game, world.Owner, [new OwnerEdit("FANDOM", "")]);

        var page = await world.Queries.FindAsync("corvid");

        // The game's own declaration is back, not gone.
        await Assert.That(page!.Declared["fandom"].Value).IsEqualTo("Ars Magica");
        await Assert.That(page.Declared["fandom"].Source).IsEqualTo(FieldSource.Mssp);

        // And the owner's withdrawal is still on the record.
        await Assert.That((await world.Fields.ForGameAsync(world.Game))
            .Single(f => f.Field == "FANDOM" && f.Source is FieldSource.Owner).Value)
            .IsEqualTo(string.Empty);
    }

    /// <summary>An empty box for a field nobody ever set is not a withdrawal of anything.</summary>
    [Test]
    public async Task AnEmptyBoxForAFieldThatWasNeverSetWritesNothing()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var world = await World.BuildAsync(db);

        var outcome = await world.Enrichment.ApplyAsync(
            world.Game,
            world.Owner,
            [.. FieldRegistry.OwnerEnrichable.Select(d => new OwnerEdit(d.Name, string.Empty))]);

        await Assert.That(outcome.IsApplied).IsTrue();
        await Assert.That((await world.Fields.ForGameAsync(world.Game)).Count).IsEqualTo(0);
    }

    /// <summary>
    /// Re-saving a form whose box is already empty touches nothing at all.
    /// </summary>
    /// <remarks>
    /// A withdrawn field keeps its row, and every subsequent save was reconciling that row and
    /// confirming it — walking <c>last_confirmed_at</c> forward, so a field withdrawn a year ago
    /// read on the dashboard as touched this morning. There is nothing to confirm: nobody declared
    /// anything.
    /// </remarks>
    [Test]
    public async Task ResavingAnAlreadyWithdrawnFieldDoesNotWalkItsAgeForward()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var world = await World.BuildAsync(db);

        await world.Enrichment.ApplyAsync(world.Game, world.Owner, [new OwnerEdit("FANDOM", "Exalted")]);
        await world.Enrichment.ApplyAsync(world.Game, world.Owner, [new OwnerEdit("FANDOM", "")]);

        var withdrawn = (await world.Fields.ForGameAsync(world.Game, FieldSource.Owner))
            .Single(f => f.Field == "FANDOM");

        var outcome = await world.Enrichment.ApplyAsync(
            world.Game, world.Owner, [new OwnerEdit("FANDOM", "  ")]);

        await Assert.That(outcome.Applied).IsEqualTo(FieldReconciliation.Nothing);

        var after = (await world.Fields.ForGameAsync(world.Game, FieldSource.Owner))
            .Single(f => f.Field == "FANDOM");

        await Assert.That(after.LastConfirmedAt).IsEqualTo(withdrawn.LastConfirmedAt);
    }

    /// <summary>
    /// The dashboard's read asks for owner rows, and does not drag a connect screen back to get them.
    /// </summary>
    /// <remarks>
    /// It read every row of the game and filtered in memory, so one claimed game cost a connect
    /// screen — thousands of characters, 9,376 at the longest here — across the wire per page load,
    /// to be discarded.
    /// </remarks>
    [Test]
    public async Task TheDashboardReadsOnlyWhatItEdits()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var world = await World.BuildAsync(db);

        await world.Fields.UpsertAsync(new GameField(
            world.Game,
            InternalFields.ConnectScreen,
            FieldSource.Banner,
            new string('x', 9376),
            Now,
            Now));
        await world.Enrichment.ApplyAsync(world.Game, world.Owner, [new OwnerEdit("FANDOM", "Exalted")]);

        var declared = await world.Enrichment.DeclaredAsync(world.Game);

        await Assert.That(declared.Keys).IsEquivalentTo(new[] { "FANDOM" });
        await Assert.That(declared.Values.Sum(f => f.Value.Length)).IsLessThan(100);
    }

    /// <summary>
    /// Re-saving an unchanged form confirms rather than manufacturing an event (§5.1).
    /// </summary>
    [Test]
    public async Task SavingTheSameAnswerAgainConfirmsItAndWritesNoChange()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var world = await World.BuildAsync(db);

        var first = await world.Enrichment.ApplyAsync(
            world.Game, world.Owner, [new OwnerEdit("FANDOM", "Exalted")]);
        var second = await world.Enrichment.ApplyAsync(
            world.Game, world.Owner, [new OwnerEdit("FANDOM", "Exalted")]);
        var third = await world.Enrichment.ApplyAsync(
            world.Game, world.Owner, [new OwnerEdit("FANDOM", "Ars Magica")]);

        await Assert.That(first.Applied.Added).IsEqualTo(1);
        await Assert.That(second.Applied.Confirmed).IsEqualTo(1);
        await Assert.That(third.Applied.Changed).IsEqualTo(1);

        // A first sighting is an addition and not an event; only the move is in the feed.
        await Assert.That((await world.Fields.ChangesAsync(world.Game, 20)).Count).IsEqualTo(1);
    }

    /// <summary>§11 — suppressed on owner request, and reversible by the same owner.</summary>
    [Test]
    public async Task AnOwnerSuppressesTheConnectScreenAndTheSurfacesStop()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var world = await World.BuildAsync(db);

        await world.Fields.UpsertAsync(new GameField(
            world.Game, InternalFields.ConnectScreen, FieldSource.Banner, "Welcome to Corvid", Now, Now));

        await Assert.That((await world.Queries.FindAsync("corvid"))!.ConnectScreenSuppressed).IsFalse();

        await world.Enrichment.SetConnectScreenSuppressedAsync(world.Game, world.Owner, suppressed: true);

        var suppressed = await world.Queries.FindAsync("corvid");

        await Assert.That(suppressed!.ConnectScreenSuppressed).IsTrue();

        // The screen is still held: the crawler reads it as §7.3's identity signal, and suppression
        // is a decision about republishing rather than about capturing.
        await Assert.That(suppressed.ConnectScreen).IsEqualTo("Welcome to Corvid");

        await world.Enrichment.SetConnectScreenSuppressedAsync(world.Game, world.Owner, suppressed: false);

        await Assert.That((await world.Queries.FindAsync("corvid"))!.ConnectScreenSuppressed).IsFalse();

        // Both decisions are recorded — and neither is a public event about the game, so the page's
        // feed stays empty while the ledger holds the reversal.
        await Assert.That((await world.Queries.FindAsync("corvid"))!.Changes).IsEmpty();
        await Assert.That(await LedgerCountAsync(db, world.Game, InternalFields.ConnectScreenSuppressed))
            .IsEqualTo(1);
    }

    /// <summary>
    /// A privacy decision is not an event on the game's public page.
    /// </summary>
    /// <remarks>
    /// <c>connect_screen_suppressed</c> is machinery — a decision of ours about what to display —
    /// and <c>InternalFields</c> exists to keep exactly these off the surfaces. The declared panel
    /// already filtered them; the change feed filtered nothing, so reversing suppression published
    /// "connect_screen_suppressed changed from true to false (owner)" on the game page, in plain
    /// text and in the API. It would have been the first internal field ever to reach those
    /// surfaces, and what it announces is a choice §11 grants with no questions asked.
    /// </remarks>
    [Test]
    public async Task TogglingSuppressionIsNotPublishedInTheChangeFeed()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var world = await World.BuildAsync(db);

        await world.Enrichment.SetConnectScreenSuppressedAsync(world.Game, world.Owner, suppressed: true);
        await world.Enrichment.SetConnectScreenSuppressedAsync(world.Game, world.Owner, suppressed: false);
        await world.Enrichment.ApplyAsync(world.Game, world.Owner, [new OwnerEdit("FANDOM", "Exalted")]);
        await world.Enrichment.ApplyAsync(world.Game, world.Owner, [new OwnerEdit("FANDOM", "Ars Magica")]);

        var page = await world.Queries.FindAsync("corvid");

        // The enrichment change is an event about the game and belongs there; the suppression is not.
        await Assert.That(page!.Changes.Count).IsEqualTo(1);
        await Assert.That(page.Changes.Single().Summary).Contains("FANDOM");

        foreach (var change in page.Changes)
        {
            await Assert.That(change.Summary).DoesNotContain("connect_screen");
        }

        // Nothing is deleted: the row is still in field_change, it is simply not a public event.
        // Read straight from the table, because ChangesAsync is the surface being filtered.
        await Assert.That(await LedgerCountAsync(db, world.Game, InternalFields.ConnectScreenSuppressed))
            .IsEqualTo(1);
    }

    /// <summary>Nobody else may speak for a game's connect screen.</summary>
    [Test]
    public async Task AStrangerCannotSuppressSomebodyElsesConnectScreen()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var world = await World.BuildAsync(db);
        var stranger = await world.UserAsync("stranger");

        var outcome = await world.Enrichment.SetConnectScreenSuppressedAsync(
            world.Game, stranger, suppressed: true);

        await Assert.That(outcome.Verdict).IsEqualTo(EnrichmentVerdict.NotAnOwner);
        await Assert.That((await world.Queries.FindAsync("corvid"))!.ConnectScreenSuppressed).IsFalse();
    }

    /// <summary>Over the bound it is refused, and emphatically not truncated.</summary>
    [Test]
    public async Task AnOversizedAnswerIsRefusedRatherThanQuietlyShortened()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var world = await World.BuildAsync(db);

        var outcome = await world.Enrichment.ApplyAsync(
            world.Game,
            world.Owner,
            [new OwnerEdit("FANDOM", new string('x', OwnerEnrichment.MaxValueLength + 1))]);

        await Assert.That(outcome.Verdict).IsEqualTo(EnrichmentVerdict.TooLong);
        await Assert.That(outcome.Field).IsEqualTo("FANDOM");
        await Assert.That((await world.Fields.ForGameAsync(world.Game)).Count).IsEqualTo(0);
    }

    /// <summary>A pasted newline is one line by the time it reaches a definition list.</summary>
    [Test]
    public async Task APastedAnswerBecomesOneLine()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var world = await World.BuildAsync(db);

        await world.Enrichment.ApplyAsync(
            world.Game, world.Owner, [new OwnerEdit("APPLICATION PROCESS", "  Write to us.\n\nWe read weekly. ")]);

        var page = await world.Queries.FindAsync("corvid");

        await Assert.That(page!.Declared["application process"].Value)
            .IsEqualTo("Write to us. We read weekly.");
    }

    /// <summary>A game, an owner who proved it, and the services that let them write.</summary>
    /// <summary>
    /// How many transitions the ledger holds for one field, read past every surface.
    /// </summary>
    /// <remarks>
    /// Raw SQL on purpose: <c>ChangesAsync</c> is the game page's feed and filters machinery out of
    /// it, so asking it whether a row survives would be asking the thing under test.
    /// </remarks>
    private static async Task<int> LedgerCountAsync(TestDatabase db, Guid game, string field)
    {
        await using var connection = await db.DataSource.OpenConnectionAsync();

        return await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM field_change WHERE game_id = @game AND field = @field",
            new { game, field });
    }

    private sealed record World(
        TestDatabase Db,
        Guid Game,
        Guid Owner,
        ClaimService Claims,
        OwnerEnrichment Enrichment,
        NpgsqlGameFieldStore Fields,
        NpgsqlGameQueries Queries)
    {
        public static async Task<World> BuildAsync(TestDatabase db)
        {
            var clock = new FixedClock(Now);
            var game = await Seed.GameAsync(db);
            var fields = new NpgsqlGameFieldStore(db.DataSource);
            var claimStore = new NpgsqlClaimStore(db.DataSource);
            var claims = new ClaimService(claimStore, new NpgsqlGameStore(db.DataSource), clock);

            var world = new World(
                db,
                game,
                Guid.Empty,
                claims,
                new OwnerEnrichment(
                    claimStore, fields, new FieldReconciler(fields), FieldRegistry.Instance, clock),
                fields,
                new NpgsqlGameQueries(db.DataSource) { Clock = () => Now });

            var owner = await world.UserAsync("owner");
            var claim = await claims.IssueAsync(game, owner);
            await claims.OfferBeaconAsync(game, claim.Token, ClaimChannel.Mssp);

            return world with { Owner = owner };
        }

        public async Task<Guid> UserAsync(string name)
        {
            var id = Guid.CreateVersion7();

            await using var connection = await Db.DataSource.OpenConnectionAsync();

            await connection.ExecuteAsync(
                """
                INSERT INTO app_user (id, display_name, normalised_name, security_stamp,
                                      concurrency_stamp, created_at)
                VALUES (@id, @name, @normalised, @stamp, @stamp, @now)
                """,
                new
                {
                    id,
                    name,
                    normalised = name.ToUpperInvariant(),
                    stamp = Guid.NewGuid().ToString(),
                    now = Now,
                });

            return id;
        }
    }

    private sealed class FixedClock(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }
}
