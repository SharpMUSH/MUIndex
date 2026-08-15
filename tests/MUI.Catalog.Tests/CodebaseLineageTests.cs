using MUI.Catalog;

namespace MUI.Catalog.Tests;

/// <summary>
/// The one classification on this site that is ours — the lineage a codebase descends from.
/// </summary>
/// <remarks>
/// It exists because the declared <c>family</c> facet cannot answer "which games are MUSHes": MSSP's
/// vocabulary has no <c>MUSH</c> in it (PennMUSH answers <c>TinyMUD</c>) and most of the MUSH world
/// publishes no MSSP at all. That makes these tests the only guard on a fact we assert rather than
/// observe, so they are written against the two ways it can go wrong — placing a game in a lineage
/// its software is not in, and quietly inventing a parent for a codebase that has none.
/// </remarks>
public class CodebaseLineageTests
{
    [Test]
    [Arguments("PennMUSH 1.8.8p0")]
    [Arguments("TinyMUSH 3.3")]
    [Arguments("TinyMUX 2.12")]
    [Arguments("RhostMUSH")]
    [Arguments("AresMUSH")]
    [Arguments("CobraMUSH")]
    public async Task TheMushCodebasesAreOneLineage(string codebase)
    {
        // The five the hobby names in one breath, plus Cobra. Every one of them is a separate
        // family and a separate answer to CODEBASE, and no two of them agree about MSSP.
        await Assert.That(CodebaseLineage.Of(codebase)).IsEqualTo(CodebaseLineage.Mush);
    }

    [Test]
    public async Task APatchlevelNeverNeedsItsOwnEntry()
    {
        // Keyed on the family rather than the raw string, so a release we have never seen classifies
        // itself. A map keyed on CODEBASE would silently unclassify a game the week it upgraded.
        await Assert.That(CodebaseLineage.Of("PennMUSH 1.9.0")).IsEqualTo(CodebaseLineage.Mush);
        await Assert.That(CodebaseLineage.Of("PennMUSH")).IsEqualTo(CodebaseLineage.Mush);
    }

    [Test]
    [Arguments("ROM 2.4", "DikuMUD")]
    [Arguments("SMAUG 1.4a", "DikuMUD")]
    [Arguments("CircleMUD 3.5", "DikuMUD")]
    [Arguments("FluffOS 2019", "LPMud")]
    [Arguments("MudOS v22", "LPMud")]
    [Arguments("LambdaMOO 1.8.1", "MOO")]
    [Arguments("TinyMUCK 2.2.3", "MUCK")]
    public async Task TheOtherLineagesGatherTheirOwn(string codebase, string lineage) =>
        await Assert.That(CodebaseLineage.Of(codebase)).IsEqualTo(lineage);

    [Test]
    public async Task ACodebaseWithNoUncontestedParentIsNotGivenOne()
    {
        // Evennia was written from nothing in Python and CoffeeMUD in Java; both are routinely
        // *described* as Diku-like and neither descends from it. Unclassified is the honest answer,
        // and the one a reader can tell apart from a classification we stand behind.
        await Assert.That(CodebaseLineage.Of("Evennia 1.0")).IsNull();
        await Assert.That(CodebaseLineage.Of("CoffeeMud v5.11.0.4")).IsNull();
        await Assert.That(CodebaseLineage.Of("Custom")).IsNull();
    }

    [Test]
    public async Task AGameWeCouldNotIdentifyHasNoLineage()
    {
        // Our own gap in measurement, and not a fact about the game's ancestry.
        await Assert.That(CodebaseLineage.Of(null)).IsNull();
        await Assert.That(CodebaseLineage.Of("")).IsNull();
    }

    [Test]
    public async Task ANeighbouringNameIsNotSweptIn()
    {
        // The fold is the whole of the matching, so a codebase whose name merely starts with a
        // classified one stays out. "MUX" is in the map and "MUXtreme" is not the same software.
        await Assert.That(CodebaseLineage.Of("MUXtreme 1.0")).IsNull();
        await Assert.That(CodebaseLineage.Of("ROMulus 3")).IsNull();
    }

    [Test]
    public async Task EveryLineageOfferedIsOneSomethingCanBeIn()
    {
        // CodebaseLineage.All is the panel's fixed vocabulary. A value in it that nothing could ever
        // match would be a permanent zero on the panel, advertising a question with no answer.
        var reachable = CodebaseLineage.All
            .Where(lineage => new[]
            {
                "PennMUSH", "TinyMUCK", "LambdaMOO", "ROM", "FluffOS", "AberMUD",
            }.Any(codebase => CodebaseLineage.Of(codebase) == lineage));

        await Assert.That(reachable).IsEquivalentTo(CodebaseLineage.All);
    }
}
