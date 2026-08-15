using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Support;

namespace MUI.Catalog.Tests;

/// <summary>
/// One row per <c>(game, field, source)</c>, and a winner derived on read (spec §5.1).
/// </summary>
public class FieldPrecedenceTests
{
    private static readonly Guid Game = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);

    private static GameField Row(FieldSource source, string value, double ageDays = 0) =>
        new(Game, "GMCP", source, value, Now.AddDays(-400), Now.AddDays(-ageDays));

    [Test]
    public async Task AHandshakeObservationBeatsAnMsspClaim()
    {
        var winner = FieldPrecedence.Winner([Row(FieldSource.Mssp, "1", 2000), Row(FieldSource.Handshake, "0")]);

        await Assert.That(winner!.Source).IsEqualTo(FieldSource.Handshake);
        await Assert.That(winner.Value).IsEqualTo("0");
    }

    [Test]
    public async Task TheLosingSourceSurvivesSoTheDisagreementCanBeShown()
    {
        // This is the whole reason the row is keyed by source. "Declared GMCP, never offered in
        // 214 handshakes" is the single most interesting fact a capability matrix can carry, and a
        // store keyed on (game, field) alone cannot hold both halves of it.
        var store = new InMemoryGameFieldStore();

        await store.UpsertAsync(Row(FieldSource.Mssp, "1", 2000));
        await store.UpsertAsync(Row(FieldSource.Handshake, "0"));

        var rows = await store.ForGameAsync(Game);
        await Assert.That(rows).Count().IsEqualTo(2);
        await Assert.That(rows.Select(r => r.Source)).Contains(FieldSource.Mssp);
        await Assert.That(rows.Select(r => r.Source)).Contains(FieldSource.Handshake);
    }

    [Test]
    public async Task EachSourceKeepsItsOwnAgeSoOneCanBeStaleWhileTheOtherIsFresh()
    {
        var declared = Row(FieldSource.Mssp, "1", 2190);
        var measured = Row(FieldSource.Handshake, "0", 0.003);

        await Assert.That(declared.LastConfirmedAt).IsLessThan(measured.LastConfirmedAt);
    }

    [Test]
    public async Task StaffOverridesEverything()
    {
        var winner = FieldPrecedence.Winner(
        [
            Row(FieldSource.Handshake, "0"),
            Row(FieldSource.Owner, "1"),
            Row(FieldSource.Staff, "corrected"),
        ]);

        await Assert.That(winner!.Source).IsEqualTo(FieldSource.Staff);
    }

    [Test]
    public async Task EverySourceOnTheLadderIsOneThisCrawlerCanProduce()
    {
        // The ladder had two rungs below Banner — ImportedMeasured and ImportedAsserted — for values
        // taken from other directories. Nothing imports values any more (spec §7.6): the backfill
        // contributes addresses, and every field is then measured here. A source no writer can
        // produce is not harmless on an enum; it is an invitation to write one.
        await Assert.That(Enum.GetNames<FieldSource>()).DoesNotContain("ImportedMeasured");
        await Assert.That(Enum.GetNames<FieldSource>()).DoesNotContain("ImportedAsserted");
        await Assert.That(Enum.GetNames<IntervalOrigin>().Length).IsEqualTo(1);
    }

    [Test]
    public async Task NoRowsMeansNoWinnerRatherThanADefault()
    {
        await Assert.That(FieldPrecedence.Winner([])).IsNull();
    }

    [Test]
    public async Task UpsertingTheSameSourceTwiceReplacesRatherThanDuplicates()
    {
        var store = new InMemoryGameFieldStore();

        await store.UpsertAsync(Row(FieldSource.Mssp, "1", 10));
        await store.UpsertAsync(Row(FieldSource.Mssp, "0", 0));

        var rows = await store.ForGameAsync(Game);
        await Assert.That(rows).Count().IsEqualTo(1);
        await Assert.That(rows[0].Value).IsEqualTo("0");
    }

    [Test]
    public async Task AMeasuredSourceIsDistinguishableFromADeclaredOne()
    {
        var handshake = new Provenance(FieldSource.Handshake, Now, Now);
        var mssp = new Provenance(FieldSource.Mssp, Now, Now);

        await Assert.That(handshake.IsMeasured).IsTrue();
        await Assert.That(mssp.IsMeasured).IsFalse();
    }

    [Test]
    public async Task WhatWeReadOffTheWireIsMeasuredAndWhatAGameReportsIsDeclared()
    {
        // The line is who read the value, not who authored it. `banner` is on the measured side
        // because we open a socket and parse that text ourselves on every probe — migration 0003
        // has said so in as many words since the presence table was written: several games publish
        // their count only on the connect screen, "and that is still a measurement of ours". Its
        // freshness is ours even where its arithmetic is theirs.
        //
        // `mssp` stays declared, and that is not a contradiction: a game filling in a structured
        // self-description is reporting, and PLAYERS there may be whatever the codebase last cached.
        // `owner` and `staff` are people typing, one of them us.
        foreach (var source in new[] { FieldSource.Handshake, FieldSource.Who, FieldSource.Banner })
        {
            await Assert.That(FieldSources.IsMeasured(source)).IsTrue();
        }

        foreach (var source in new[] { FieldSource.Mssp, FieldSource.Owner, FieldSource.Staff })
        {
            await Assert.That(FieldSources.IsMeasured(source)).IsFalse();
        }
    }

    [Test]
    public async Task EverySourceGetsTheSameVerdictWhicheverTypeCarriesIt()
    {
        // Two records carry provenance and both once spelled the predicate out for themselves, so a
        // decision about one source had to be remembered in two files. It is one function now, and
        // this is what stops a second copy growing back: the badge on somebody else's site, the chip
        // on the listing and the state in the API all resolve through here.
        foreach (var source in Enum.GetValues<FieldSource>())
        {
            var provenance = new Provenance(source, Now, Now).IsMeasured;
            var chip = new ProvenanceChip("v", source, Now, IsStale: false).IsMeasured;

            await Assert.That(provenance).IsEqualTo(FieldSources.IsMeasured(source));
            await Assert.That(chip).IsEqualTo(FieldSources.IsMeasured(source));
        }
    }
}
