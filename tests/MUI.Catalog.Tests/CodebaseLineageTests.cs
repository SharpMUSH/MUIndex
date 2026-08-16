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

    /// <summary>A codebase that spells its version oddly is still placed, by the words it wrote.</summary>
    /// <remarks>
    /// Both of these are live values, re-probed 2026-08-15, and both publish an ancestry the map
    /// knows: <c>CD</c> answers <c>FAMILY LPMud</c> and <c>Epiphany</c> answers <c>FAMILY LPMud</c>
    /// with "LPmud version : FluffOS v2.26" on its connect screen. The fold only removes a trailing
    /// version token, so neither reaches the map by its key.
    /// </remarks>
    [Test]
    public async Task AVersionTheFoldCannotRemoveDoesNotCostTheLineage()
    {
        await Assert.That(CodebaseLineage.Of("CD.06.06")).IsEqualTo(CodebaseLineage.Lp);
        await Assert.That(CodebaseLineage.Of("Epiphany v1.2.15 [development]"))
            .IsEqualTo(CodebaseLineage.Lp);
        await Assert.That(CodebaseLineage.Of("Rhost 4.0.4 (patchlevel 1)"))
            .IsEqualTo(CodebaseLineage.Mush);
    }

    /// <summary>A string that recites its own descent is read as the recital it is.</summary>
    [Test]
    public async Task ACodebaseThatNamesItsAncestryIsPlacedByIt()
    {
        // A real catalogue value. Diku, Merc and Rom are three names for one lineage, so the string
        // is unanimous and there is nothing to choose between.
        await Assert.That(CodebaseLineage.Of("Diku Merc Rom RoT AoD")).IsEqualTo(CodebaseLineage.Diku);
    }

    /// <summary>Two lineages in one string is a question we decline rather than answer.</summary>
    [Test]
    public async Task ACodebaseNamingTwoLineagesIsPlacedInNeither()
    {
        // Picking one would be choosing on the reader's behalf and then recording the choice as a
        // fact about somebody's game.
        await Assert.That(CodebaseLineage.Of("PennMUSH/Diku bridge")).IsNull();
    }

    [Test]
    public async Task ACodebaseWithNoUncontestedParentIsNotGivenOne()
    {
        // Evennia was written from nothing in Python and CoffeeMUD in Java; both are routinely
        // *described* as Diku-like and neither descends from it. Unclassified is the honest answer,
        // and the one a reader can tell apart from a classification we stand behind.
        await Assert.That(CodebaseLineage.Of("Evennia 1.0")).IsNull();
        await Assert.That(CodebaseLineage.Of("CoffeeMud v5.11.0.4")).IsNull();
        await Assert.That(CodebaseLineage.Of("Custom")).IsNull();

        // Re-probed 2026-08-15, and these two say so themselves: Evennia and Riftforge both publish
        // FAMILY Custom, CoffeeMUD publishes FAMILY CoffeeMUD. The abstention is corroborated.
        await Assert.That(CodebaseLineage.Of("Riftforge")).IsNull();
        await Assert.That(CodebaseLineage.Of("Enrym (custom Node.js)")).IsNull();
        await Assert.That(CodebaseLineage.Of("LoFP (Go)")).IsNull();
    }

    [Test]
    public async Task AGameWeCouldNotIdentifyHasNoLineage()
    {
        // Our own gap in measurement, and not a fact about the game's ancestry.
        await Assert.That(CodebaseLineage.Of(null)).IsNull();
        await Assert.That(CodebaseLineage.Of("")).IsNull();
    }

    /// <summary>The mudlibs, each placed by the FAMILY it publishes rather than by resemblance.</summary>
    /// <remarks>
    /// A mudlib and the driver beneath it are different software and one lineage, which is the
    /// question this facet asks. Every value here is live and every placement is the game's own
    /// answer, read 2026-08-16 from the <c>FAMILY</c> already in the store.
    /// </remarks>
    [Test]
    [Arguments("TMI-2 1.5.1")]
    [Arguments("Dead Souls 3.7a7")]
    [Arguments("Discworld lib (current)")]
    [Arguments("UNIlib")]
    [Arguments("3Scapes mudlib")]
    [Arguments("TD-MUDLIB 2.0")]
    [Arguments("MorgenGrauen-3.3.5")]
    [Arguments("Aldebaran")]
    [Arguments("RoleMUD 2.2")]
    [Arguments("Moral Decay v9.0")]
    [Arguments("PD/NM III")]
    public async Task AMudlibIsInTheLineageItPublishes(string codebase) =>
        await Assert.That(CodebaseLineage.Of(codebase)).IsEqualTo(CodebaseLineage.Lp);

    [Test]
    [Arguments("PizzaMUD")]
    [Arguments("Galaxy Engine 2.2")]
    [Arguments("EmpireMUD 2.0 beta 5.213")]
    [Arguments("JediMUD")]
    [Arguments("MUME IX ad3e7206")]
    public async Task ADikuDescendantThatSaysSoIsPlacedBySayingIt(string codebase) =>
        await Assert.That(CodebaseLineage.Of(codebase)).IsEqualTo(CodebaseLineage.Diku);

    [Test]
    public async Task AGameSayingCustomIsNotOverruledByWhatWeCouldGuess()
    {
        // Every one of these publishes FAMILY Custom, and several have an ancestry anybody could
        // name from the outside — Legends of the Jedi is a SMAUG descendant by any account but its
        // own. A declaration a game made about itself outranks a resemblance we noticed, or this
        // stops being a map of what games say and becomes a map of what we assumed.
        await Assert.That(CodebaseLineage.Of("LotJ 4.3")).IsNull();
        await Assert.That(CodebaseLineage.Of("Materia Magica 5.0.30")).IsNull();
        await Assert.That(CodebaseLineage.Of("Alter Aeon v2.25")).IsNull();
        await Assert.That(CodebaseLineage.Of("TeenyMUSH 0.91")).IsNull();
    }

    [Test]
    public async Task AVersionFusedToTheNameDoesNotCostTheLineage()
    {
        // ROM2.4/Haven splits to "ROM2", which no key matches, so a string plainly reciting ROM was
        // unplaced on a space its author did not type. The trailing digits come off, which is the
        // boundary LoginCommandReading.NamesFamily already applies to a higher-stakes decision.
        await Assert.That(CodebaseLineage.Of("ROM2.4/Haven")).IsEqualTo(CodebaseLineage.Diku);
        await Assert.That(CodebaseLineage.Of("ROM24 b6")).IsEqualTo(CodebaseLineage.Diku);

        // And a letter after the marker still disqualifies it, which is the edge the digit rule
        // must not have widened.
        await Assert.That(CodebaseLineage.Of("ROMulus2 3")).IsNull();
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
