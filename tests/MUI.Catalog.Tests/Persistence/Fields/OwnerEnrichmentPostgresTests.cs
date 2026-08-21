using Dapper;

using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// Spec §8.5 and §11 against a real database: what a verified owner may write, and what happens to
/// the measurements while they do it.
/// </summary>
/// <remarks>
/// The property under test throughout: an owner's answer is a row of its own, keyed
/// <c>(game, field, source)</c>, so a declared value and a measured one can't contend. These assert
/// the second half of that key does its job.
/// </remarks>
public class OwnerEnrichmentPostgresTests
{
    private static readonly DateTimeOffset Now = Seed.Now;

    /// <summary>The fields MSSP has no variable for, and not one more.</summary>
    /// <remarks>
    /// Pinned because the writable set is derived from a flag: marking a field <c>ownerEnrichable</c>
    /// makes it editable with no second review. This is that review. The six addresses join §3.2's
    /// four on the same argument: MSSP has no <c>FORUM</c>, <c>WIKI</c>, <c>MASTODON</c>,
    /// <c>BLUESKY</c>, <c>X</c> or <c>TELEGRAM</c> variable, so the owner is the only possible source.
    /// </remarks>
    [Test]
    public async Task TheWritableSetIsExactlyWhatMsspHasNoRoomFor()
    {
        await Assert.That(FieldRegistry.OwnerEnrichable.Select(d => d.Name))
            .IsEquivalentTo(new[]
            {
                "FANDOM", "APPLICATION PROCESS", "RP ENFORCEMENT", "CONSENT TOOLS",
                "WIKI", "FORUM", "TELEGRAM", "MASTODON", "BLUESKY", "X",
            });
    }

    /// <summary>
    /// A field that holds an address gets one or nothing, and the refusal names the field.
    /// </summary>
    /// <remarks>
    /// Gated on the write, not only the render — a value stored and then quietly declined by the page
    /// looks like success to the owner who typed it.
    /// </remarks>
    [Test]
    [Arguments("WIKI", "javascript:alert(1)")]
    [Arguments("FORUM", "www.example.org")]
    [Arguments("MASTODON", "https://user:pass@evil.example/")]
    [Arguments("WEBSITE", "not a url at all")]
    [Arguments("CONTACT", "msocorcim (at) gmail (dot) com")]
    public async Task AnAddressFieldRefusesAValueThatIsNotOne(string field, string value)
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var world = await World.BuildAsync(db);

        var outcome = await world.Enrichment.ApplyAsync(
            world.Game, world.Owner, [new OwnerEdit("FANDOM", "Exalted"), new OwnerEdit(field, value)]);

        await Assert.That(outcome.Verdict).IsEqualTo(EnrichmentVerdict.NotAnAddress);
        await Assert.That(outcome.Field).IsEqualTo(field);

        // All or nothing, like every other refusal here: the good half of the submission is not
        // applied while the bad half is reported.
        await Assert.That((await world.Fields.ForGameAsync(world.Game)).Count).IsEqualTo(0);
    }

    /// <summary>Clearing an address field is a withdrawal, not a malformed address.</summary>
    [Test]
    public async Task AnEmptyBoxIsNotAnAddressThatFailedToParse()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var world = await World.BuildAsync(db);

        await world.Enrichment.ApplyAsync(
            world.Game, world.Owner, [new OwnerEdit("WIKI", "https://wiki.example.org")]);

        var outcome = await world.Enrichment.ApplyAsync(
            world.Game, world.Owner, [new OwnerEdit("WIKI", "  ")]);

        await Assert.That(outcome.IsApplied).IsTrue();

        // The row goes on existing, emptied. Nothing here is ever deleted (§7.5).
        var stored = await world.Fields.ForGameAsync(world.Game, FieldSource.Owner);

        await Assert.That(stored.Single(f => f.Field == "WIKI").Value).IsEmpty();
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
    /// The form never offers <c>CODEBASE</c>, so this is a hand-assembled post — exactly the case
    /// that must not get half of what it asked for.
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
    /// Asserted over every non-owner row, not just the one field written — the failure this guards
    /// against is a write path that confirmed, restamped or replaced rows it was never asked about.
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

    /// <summary>
    /// The overridable half: what a person types into <c>mush.cnf</c>, and nothing the codebase
    /// fills in about the connection.
    /// </summary>
    /// <remarks>
    /// Pinned for the same reason as the enrichable list, with more at stake: this half has MSSP
    /// counterparts, so a field added here starts outranking a game's own report the moment it's
    /// marked. That is the review.
    /// </remarks>
    [Test]
    public async Task TheOverridableSetIsTheHandTypedHalfOfMssp()
    {
        await Assert.That(FieldRegistry.OwnerOverridable.Select(d => d.Name))
            .IsEquivalentTo(new[]
            {
                "NAME", "CONTACT", "WEBSITE", "DISCORD", "ICON", "CREATED", "LANGUAGE", "LOCATION",
                "MINIMUM AGE", "GENRE", "GAMEPLAY", "GAMESYSTEM", "SUBGENRE", "STATUS", "INTERMUD",
                "DESCRIPTION",
            });
    }

    /// <summary>
    /// An override and the report it overrides are two rows, and both survive.
    /// </summary>
    /// <remarks>
    /// The property the whole widening rests on: <c>GameField</c> is keyed
    /// <c>(game, field, source)</c>, so an owner answering <c>GENRE</c> can't overwrite their game's
    /// report even by accident — §5.1's ladder puts <c>owner</c> above <c>mssp</c>, and both survive
    /// with their own age.
    /// </remarks>
    [Test]
    public async Task AnOverrideSitsBesideTheReportRatherThanOverIt()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var world = await World.BuildAsync(db);

        await world.Fields.UpsertAsync(new GameField(
            world.Game, "GENRE", FieldSource.Mssp, "Adventure", Now.AddDays(-30), Now.AddDays(-1)));

        var outcome = await world.Enrichment.ApplyAsync(
            world.Game, world.Owner, [new OwnerEdit("GENRE", "Fantasy")]);

        await Assert.That(outcome.Verdict).IsEqualTo(EnrichmentVerdict.Applied);

        var rows = (await world.Fields.ForGameAsync(world.Game))
            .Where(f => f.Field == "GENRE")
            .ToList();

        await Assert.That(rows.Count).IsEqualTo(2);
        await Assert.That(rows.Single(r => r.Source is FieldSource.Mssp).Value).IsEqualTo("Adventure");
        await Assert.That(rows.Single(r => r.Source is FieldSource.Owner).Value).IsEqualTo("Fantasy");

        // And the owner's is what a reader is shown first.
        await Assert.That(FieldPrecedence.Winner(rows)!.Value).IsEqualTo("Fantasy");
    }

    /// <summary>
    /// An override is <em>declared</em>, and widening the writable set did not move that line.
    /// </summary>
    /// <remarks>
    /// The plausible failure: having decided an owner may answer <c>GENRE</c>, it would be easy to
    /// start treating that answer as authoritative in the sense reserved for a socket.
    /// <see cref="FieldSources.IsMeasured"/> is the one spelling of that line, and <c>owner</c> sits
    /// on the declared side of it exactly as <c>mssp</c> does.
    /// </remarks>
    [Test]
    public async Task AnOverrideIsDeclaredJustAsTheReportIs()
    {
        await Assert.That(FieldSources.IsMeasured(FieldSource.Owner)).IsFalse();
        await Assert.That(FieldSources.IsMeasured(FieldSource.Mssp)).IsFalse();
    }

    /// <summary>
    /// The connection-describing half is refused out loud, like a measurement.
    /// </summary>
    /// <remarks>
    /// <c>CODEBASE</c> is hand-typeable but still not an owner's to answer — it names the software,
    /// not the game, and the crawler reads it off the login replies. <c>capability.gmcp.declared</c>
    /// is refused for a sharper reason: the matrix exists to show a claim beside a handshake, and this
    /// would edit one half of a comparison about oneself.
    /// </remarks>
    [Test]
    [Arguments("CODEBASE")]
    [Arguments("HOSTNAME")]
    [Arguments("PORT")]
    [Arguments("FAMILY")]
    public async Task TheConnectionDescribingFieldsAreRefusedByName(string field)
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var world = await World.BuildAsync(db);

        var outcome = await world.Enrichment.ApplyAsync(
            world.Game, world.Owner, [new OwnerEdit(field, "something")]);

        await Assert.That(outcome.Verdict).IsEqualTo(EnrichmentVerdict.NotEnrichable);
        await Assert.That(outcome.Field).IsEqualTo(field);
        await Assert.That((await world.Fields.ForGameAsync(world.Game)).Count).IsEqualTo(0);
    }

    /// <summary>
    /// Answering <c>NAME</c> stores a row and asks for the listing and the URL to follow it.
    /// </summary>
    /// <remarks>
    /// The listed name is denormalised and the URL minted from it is a promise to everyone holding
    /// the old one, so this is the one writable field whose write has a second half. Asserts only
    /// that it's asked for, with the name the owner typed.
    /// </remarks>
    [Test]
    public async Task AnsweringTheNameAsksForTheListingAndTheUrlToFollow()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var world = await World.BuildAsync(db);
        var renames = new RecordedRenames();

        var enrichment = new OwnerEnrichment(
            new NpgsqlClaimStore(db.DataSource),
            world.Fields,
            new FieldReconciler(world.Fields),
            FieldRegistry.Instance,
            new FixedClock(Now),
            renames);

        var outcome = await enrichment.ApplyAsync(
            world.Game, world.Owner, [new OwnerEdit("NAME", "Corvid Court")]);

        await Assert.That(outcome.Verdict).IsEqualTo(EnrichmentVerdict.Applied);
        await Assert.That(renames.Applied).IsEquivalentTo(new[] { (world.Game, "Corvid Court") });
    }

    /// <summary>
    /// Withdrawing the name renames nothing, because giving one up is not asking for another.
    /// </summary>
    /// <remarks>
    /// The empty row goes on existing and simply stops outranking the game's own report, so the
    /// crawler's ordinary grace decides the name from the next cycle.
    /// </remarks>
    [Test]
    public async Task WithdrawingTheNameRenamesNothing()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var world = await World.BuildAsync(db);
        var renames = new RecordedRenames();

        var enrichment = new OwnerEnrichment(
            new NpgsqlClaimStore(db.DataSource),
            world.Fields,
            new FieldReconciler(world.Fields),
            FieldRegistry.Instance,
            new FixedClock(Now),
            renames);

        await enrichment.ApplyAsync(world.Game, world.Owner, [new OwnerEdit("NAME", "Corvid Court")]);
        await enrichment.ApplyAsync(world.Game, world.Owner, [new OwnerEdit("NAME", "   ")]);

        await Assert.That(renames.Applied.Count).IsEqualTo(1);

        var row = (await world.Fields.ForGameAsync(world.Game))
            .Single(f => f.Field == "NAME" && f.Source is FieldSource.Owner);

        await Assert.That(row.Value).IsEqualTo(string.Empty);
    }

    /// <summary>
    /// A deployment with no minter behind it stores the name and does less, rather than refusing.
    /// </summary>
    /// <remarks>
    /// The row is the fact; the URL is a convenience built on it. Refusing the write would make a
    /// missing collaborator look like a rule about what an owner may say.
    /// </remarks>
    [Test]
    public async Task WithNoMinterTheNameIsStillStored()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var world = await World.BuildAsync(db);

        var outcome = await world.Enrichment.ApplyAsync(
            world.Game, world.Owner, [new OwnerEdit("NAME", "Corvid Court")]);

        await Assert.That(outcome.Verdict).IsEqualTo(EnrichmentVerdict.Applied);
        await Assert.That((await world.Fields.ForGameAsync(world.Game))
            .Single(f => f.Field == "NAME").Value).IsEqualTo("Corvid Court");
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
    /// The one way an owner could still edit a measurement: <c>owner</c> outranks <c>mssp</c> in
    /// §5.1's ladder, so a cleared owner row — empty value, still the precedence winner — was chosen
    /// as winner and then dropped for being empty, taking the whole field with it. Emptiness is
    /// filtered before the ladder runs, not after.
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
    /// A withdrawn field keeps its row, and re-confirming it would walk <c>last_confirmed_at</c>
    /// forward for nothing declared — a field withdrawn a year ago would read as touched this morning.
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
    /// Reading every row and filtering in memory cost one claimed game a connect screen — thousands
    /// of characters — across the wire per page load, to be discarded.
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
    /// <c>connect_screen_suppressed</c> is machinery — a decision of ours, not the game's — and
    /// <c>InternalFields</c> exists to keep it off the surfaces. The change feed wasn't filtering it,
    /// so reversing suppression published "connect_screen_suppressed changed from true to false
    /// (owner)" in plain text and the API.
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

    /// <summary>How many transitions the ledger holds for one field, read past every surface.</summary>
    /// <remarks>
    /// Raw SQL on purpose — <c>ChangesAsync</c> filters machinery out of the feed, so asking it would
    /// be asking the thing under test.
    /// </remarks>
    private static async Task<int> LedgerCountAsync(TestDatabase db, Guid game, string field)
    {
        await using var connection = await db.DataSource.OpenConnectionAsync();

        return await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM field_change WHERE game_id = @game AND field = @field",
            new { game, field });
    }

    /// <summary>
    /// A minter that records rather than mints — what a real one does to a URL is
    /// <c>SlugMinterTests</c>'s to prove; this suite only asks whether it's called.
    /// </summary>
    private sealed class RecordedRenames : IOwnerRenames
    {
        public List<(Guid Game, string Name)> Applied { get; } = [];

        public Task RenameAsync(Guid gameId, string name, CancellationToken cancellationToken = default)
        {
            Applied.Add((gameId, name));

            return Task.CompletedTask;
        }
    }

    /// <summary>A game, an owner who proved it, and the services that let them write.</summary>
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
