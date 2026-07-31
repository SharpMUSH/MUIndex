using MUI.Catalog;

namespace MUI.Import.Tests;

/// <summary>
/// Spec §7.6's two tiers, and §5.1's rule that neither of them outranks anything we measured
/// ourselves. If the precedence ladder is ever reordered, this file is what says so.
/// </summary>
public class ImportTierTests
{
    private static readonly FieldSource[] FirstParty =
    [
        FieldSource.Staff, FieldSource.Handshake, FieldSource.Owner,
        FieldSource.Who, FieldSource.Mssp, FieldSource.Banner,
    ];

    [Test]
    public async Task EachTierMapsToItsOwnFieldSource()
    {
        await Assert.That(ImportTierMap.SourceFor(ImportTier.Measured)).IsEqualTo(FieldSource.ImportedMeasured);
        await Assert.That(ImportTierMap.SourceFor(ImportTier.Asserted)).IsEqualTo(FieldSource.ImportedAsserted);
    }

    [Test]
    public async Task NeitherTierOutranksAnythingWeMeasuredOurselves()
    {
        foreach (var tier in new[] { ImportTier.Measured, ImportTier.Asserted })
        {
            var imported = ImportTierMap.SourceFor(tier);

            foreach (var ours in FirstParty)
            {
                // Lower rank is stronger, so an import must always sit numerically below everything.
                await Assert.That(FieldPrecedence.RankOf(imported))
                    .IsGreaterThan(FieldPrecedence.RankOf(ours));
            }
        }
    }

    [Test]
    public async Task AMeasuredImportBeatsAnAssertedOne()
    {
        await Assert.That(FieldPrecedence.RankOf(FieldSource.ImportedMeasured))
            .IsLessThan(FieldPrecedence.RankOf(FieldSource.ImportedAsserted));
    }

    [Test]
    public async Task AnImportedValueLosesToEveryFirstPartyValueForTheSameField()
    {
        var at = new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);
        var gameId = Guid.NewGuid();

        foreach (var tier in new[] { ImportTier.Measured, ImportTier.Asserted })
        {
            foreach (var ours in FirstParty)
            {
                var rows = new[]
                {
                    // The import is the more recently confirmed of the two, deliberately: age must not
                    // be able to promote an import over a measurement.
                    new GameField(gameId, "GENRE", ImportTierMap.SourceFor(tier), "Imported", at, at),
                    new GameField(gameId, "GENRE", ours, "Ours", at.AddYears(-1), at.AddYears(-1)),
                };

                await Assert.That(FieldPrecedence.Winner(rows)!.Source).IsEqualTo(ours);
            }
        }
    }

    [Test]
    public async Task OnlyTheMeasuredTierMayWriteHistory()
    {
        await Assert.That(ImportTierMap.MayWriteHistory(ImportTier.Measured)).IsTrue();
        await Assert.That(ImportTierMap.MayWriteHistory(ImportTier.Asserted)).IsFalse();
    }

    [Test]
    public async Task AnImportedGameCarriesNoHistoryUntilSomebodyPutsItThere()
    {
        var game = new ImportedGame { SourceName = "MudVerse", SourceKey = "anachronism", Name = "Anachronism" };

        await Assert.That(game.Endpoints).IsEmpty();
        await Assert.That(game.Presence).IsEmpty();
        await Assert.That(game.Availability).IsEmpty();
        await Assert.That(game.Fields).IsEmpty();
    }

    [Test]
    public async Task AnImportedMeasuredValueCountsAsMeasuredAndAnAssertedOneDoesNot()
    {
        var at = new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

        await Assert.That(new Provenance(FieldSource.ImportedMeasured, at, at).IsMeasured).IsTrue();
        await Assert.That(new Provenance(FieldSource.ImportedAsserted, at, at).IsMeasured).IsFalse();
    }
}
