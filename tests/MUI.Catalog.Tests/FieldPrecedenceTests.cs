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
}
