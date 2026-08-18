using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// Spec §5.1: every probe does exactly one of two things to each field. <b>Confirm</b> bumps
/// <c>last_confirmed_at</c> and writes nothing else; only a <b>change</b> reaches the change feed.
/// </summary>
/// <remarks>
/// This is what makes a game whose <c>GENRE</c> never moves cost one row per source for ever rather
/// than one per probe, and it is what makes the change feed a table of events that actually happened
/// rather than a log of the crawler having run.
/// </remarks>
public class FieldReconcilerTests
{
    private static readonly Guid Game = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly DateTimeOffset Noon = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AFirstSightingIsAnAdditionAndNotAChange()
    {
        // "Newly discovered" is §9's own feed. A change entry reading "GENRE changed from nothing to
        // Fantasy" would be an event about us rather than about the game.
        var store = new InMemoryGameFieldStore();
        var reconciler = new FieldReconciler(store);

        var result = await reconciler.ApplyAsync(
            Game, [new FieldObservation("GENRE", FieldSource.Mssp, "Fantasy")], Noon);

        await Assert.That(result).IsEqualTo(new FieldReconciliation(Confirmed: 0, Changed: 0, Added: 1));
        await Assert.That(store.Changes).IsEmpty();
    }

    [Test]
    public async Task ARepeatedValueBumpsTheConfirmationAndWritesNothingElse()
    {
        var store = new InMemoryGameFieldStore();
        var reconciler = new FieldReconciler(store);
        List<FieldObservation> observed = [new("GENRE", FieldSource.Mssp, "Fantasy")];

        await reconciler.ApplyAsync(Game, observed, Noon);
        var again = await reconciler.ApplyAsync(Game, observed, Noon.AddHours(1));

        var stored = (await store.ForGameAsync(Game)).Single();

        await Assert.That(again).IsEqualTo(new FieldReconciliation(Confirmed: 1, Changed: 0, Added: 0));
        await Assert.That(store.Changes).IsEmpty();
        await Assert.That(stored.LastConfirmedAt).IsEqualTo(Noon.AddHours(1));
        await Assert.That(stored.FirstSeenAt).IsEqualTo(Noon);
    }

    [Test]
    public async Task AFieldThatNeverMovesCostsOneRowPerSourceForever()
    {
        // The economics of §5.1, asserted rather than asserted-about: a hundred probes over a field
        // that never moves leave one row and an empty change feed.
        var store = new InMemoryGameFieldStore();
        var reconciler = new FieldReconciler(store);
        List<FieldObservation> observed = [new("GENRE", FieldSource.Mssp, "Fantasy")];

        for (var probe = 0; probe < 100; probe++)
        {
            await reconciler.ApplyAsync(Game, observed, Noon.AddHours(probe));
        }

        await Assert.That(await store.ForGameAsync(Game)).Count().IsEqualTo(1);
        await Assert.That(store.Changes).IsEmpty();
    }

    [Test]
    public async Task OnlyATransitionAppendsToTheChangeFeed()
    {
        var store = new InMemoryGameFieldStore();
        var reconciler = new FieldReconciler(store);

        await reconciler.ApplyAsync(
            Game, [new FieldObservation("CODEBASE", FieldSource.Mssp, "PennMUSH 1.8.7")], Noon);
        var moved = await reconciler.ApplyAsync(
            Game, [new FieldObservation("CODEBASE", FieldSource.Mssp, "PennMUSH 1.8.8p0")], Noon.AddDays(1));

        var change = store.Changes.Single();
        var stored = (await store.ForGameAsync(Game)).Single();

        await Assert.That(moved).IsEqualTo(new FieldReconciliation(Confirmed: 0, Changed: 1, Added: 0));
        await Assert.That(change.OldValue).IsEqualTo("PennMUSH 1.8.7");
        await Assert.That(change.NewValue).IsEqualTo("PennMUSH 1.8.8p0");
        await Assert.That(change.At).IsEqualTo(Noon.AddDays(1));
        await Assert.That(stored.Value).IsEqualTo("PennMUSH 1.8.8p0");

        // first_seen_at is when we first saw this (game, field, source) — not when the current value
        // arrived. The change feed already carries the value's history.
        await Assert.That(stored.FirstSeenAt).IsEqualTo(Noon);
    }

    [Test]
    public async Task AReflowedLineWrapIsNotAChange()
    {
        // Found on beutelland's DESCRIPTION-DE: its MSSP report toggled a mid-sentence line wrap in
        // and out of the value on alternating probes — same words, different whitespace — and that
        // is not an event about the game.
        var store = new InMemoryGameFieldStore();
        var reconciler = new FieldReconciler(store);
        var wrapped = new FieldObservation(
            "DESCRIPTION-DE", FieldSource.Mssp, "NPC erscheinen\n  in jedweder Form.");
        var unwrapped = new FieldObservation(
            "DESCRIPTION-DE", FieldSource.Mssp, "NPC erscheinen in jedweder Form.");

        await reconciler.ApplyAsync(Game, [wrapped], Noon);
        var reflowed = await reconciler.ApplyAsync(Game, [unwrapped], Noon.AddHours(2));
        var flippedBack = await reconciler.ApplyAsync(Game, [wrapped], Noon.AddHours(4));

        await Assert.That(reflowed).IsEqualTo(new FieldReconciliation(Confirmed: 1, Changed: 0, Added: 0));
        await Assert.That(flippedBack).IsEqualTo(new FieldReconciliation(Confirmed: 1, Changed: 0, Added: 0));
        await Assert.That(store.Changes).IsEmpty();
    }

    [Test]
    public async Task ARewordingThatAlsoReflowsIsStillAChange()
    {
        // The whitespace collapse must not swallow a real edit just because the edit also happens to
        // land on a different line.
        var store = new InMemoryGameFieldStore();
        var reconciler = new FieldReconciler(store);

        await reconciler.ApplyAsync(
            Game, [new FieldObservation("DESCRIPTION-DE", FieldSource.Mssp, "Alte Beschreibung.")], Noon);
        var moved = await reconciler.ApplyAsync(
            Game,
            [new FieldObservation("DESCRIPTION-DE", FieldSource.Mssp, "Neue\n  Beschreibung.")],
            Noon.AddHours(2));

        await Assert.That(moved).IsEqualTo(new FieldReconciliation(Confirmed: 0, Changed: 1, Added: 0));
        await Assert.That(store.Changes.Single().NewValue).IsEqualTo("Neue\n  Beschreibung.");
    }

    [Test]
    public async Task MeasuredAndDeclaredDoNotContendForOneRow()
    {
        // The capability matrix is the reason the key carries the source. A handshake that did not
        // offer GMCP and an MSSP that claims it must both survive, each with its own age — the
        // disagreement is the interesting fact and hiding it is the failure §5.1 was rewritten over.
        var store = new InMemoryGameFieldStore();
        var reconciler = new FieldReconciler(store);

        await reconciler.ApplyAsync(
            Game,
            [
                new FieldObservation(CapabilityFields.Measured("GMCP"), FieldSource.Handshake, "false"),
                new FieldObservation(CapabilityFields.Declared("GMCP"), FieldSource.Mssp, "true"),
            ],
            Noon);

        var stored = await store.ForGameAsync(Game);

        await Assert.That(stored).Count().IsEqualTo(2);
        await Assert.That(stored.Single(f => f.Source is FieldSource.Handshake).Value).IsEqualTo("false");
        await Assert.That(stored.Single(f => f.Source is FieldSource.Mssp).Value).IsEqualTo("true");
    }

    [Test]
    public async Task TwoSourcesDisagreeingAboutOneFieldAreBothKept()
    {
        var store = new InMemoryGameFieldStore();
        var reconciler = new FieldReconciler(store);

        await reconciler.ApplyAsync(
            Game,
            [
                new FieldObservation("CODEBASE", FieldSource.Mssp, "PennMUSH 1.8.8p0"),
                new FieldObservation("CODEBASE", FieldSource.Banner, "PennMUSH 1.8.7"),
            ],
            Noon);

        var stored = await store.ForGameAsync(Game);
        var winner = FieldPrecedence.Winner(stored);

        await Assert.That(stored).Count().IsEqualTo(2);
        await Assert.That(winner!.Source).IsEqualTo(FieldSource.Mssp);
        await Assert.That(store.Changes).IsEmpty();
    }

    [Test]
    public async Task TheVolatileMsspVariablesAreNeverStoredAsDescriptiveFields()
    {
        // PLAYERS and UPTIME move between one probe and the next. Reconciling them would write a
        // change row per probe per game — the exact cost §5.1 exists to avoid — and would bury every
        // real event in the feed. PLAYERS is presence (§5.2); UPTIME is a counter.
        var store = new InMemoryGameFieldStore();
        var reconciler = new FieldReconciler(store);

        var result = await reconciler.ApplyAsync(
            Game,
            [
                new FieldObservation("PLAYERS", FieldSource.Mssp, "15"),
                new FieldObservation("UPTIME", FieldSource.Mssp, "1769803200"),
                new FieldObservation("NAME", FieldSource.Mssp, "M*U*S*H"),
            ],
            Noon);

        var stored = await store.ForGameAsync(Game);

        await Assert.That(result.Added).IsEqualTo(1);
        await Assert.That(stored.Select(f => f.Field)).IsEquivalentTo(["NAME"]);
    }

    [Test]
    public async Task AProbeThatObservedNothingWritesNothing()
    {
        var store = new InMemoryGameFieldStore();

        var result = await new FieldReconciler(store).ApplyAsync(Game, [], Noon);

        await Assert.That(result).IsEqualTo(FieldReconciliation.Nothing);
        await Assert.That(await store.ForGameAsync(Game)).IsEmpty();
    }
}
