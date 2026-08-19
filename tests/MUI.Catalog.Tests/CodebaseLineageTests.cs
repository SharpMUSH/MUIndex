using MUI.Catalog;

namespace MUI.Catalog.Tests;

/// <summary>
/// The one classification on this site that is ours — the lineage a codebase descends from.
/// </summary>
/// <remarks>
/// The declared <c>family</c> facet cannot answer "which games are MUSHes" — MSSP's vocabulary has no
/// <c>MUSH</c> in it and most of the MUSH world publishes no MSSP at all — so these tests guard the
/// two ways an asserted-not-observed fact can go wrong: placing a game in a lineage its software is
/// not in, and inventing a parent for a codebase that has none.
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
        await Assert.That(CodebaseLineage.Of(codebase)).IsEqualTo(CodebaseLineage.Mush);
    }

    [Test]
    public async Task APatchlevelNeverNeedsItsOwnEntry()
    {
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
    /// The fold only removes a trailing version token, so <c>CD.06.06</c> and
    /// <c>Epiphany v1.2.15 [development]</c> miss the map by key and fall through to word-matching.
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
        // Diku, Merc and Rom are three names for one lineage, so the string is unanimous.
        await Assert.That(CodebaseLineage.Of("Diku Merc Rom RoT AoD")).IsEqualTo(CodebaseLineage.Diku);
    }

    /// <summary>Two lineages in one string is a question we decline rather than answer.</summary>
    [Test]
    public async Task ACodebaseNamingTwoLineagesIsPlacedInNeither()
    {
        await Assert.That(CodebaseLineage.Of("PennMUSH/Diku bridge")).IsNull();
    }

    [Test]
    public async Task ACodebaseWithNoUncontestedParentIsNotGivenOne()
    {
        // Evennia (Python) and CoffeeMUD (Java) are routinely described as Diku-like but descend from
        // neither. Unclassified is the honest answer.
        await Assert.That(CodebaseLineage.Of("Evennia 1.0")).IsNull();
        await Assert.That(CodebaseLineage.Of("CoffeeMud v5.11.0.4")).IsNull();
        await Assert.That(CodebaseLineage.Of("Custom")).IsNull();

        await Assert.That(CodebaseLineage.Of("Riftforge")).IsNull();
        await Assert.That(CodebaseLineage.Of("Enrym (custom Node.js)")).IsNull();
        await Assert.That(CodebaseLineage.Of("LoFP (Go)")).IsNull();
    }

    [Test]
    public async Task AGameWeCouldNotIdentifyHasNoLineage()
    {
        await Assert.That(CodebaseLineage.Of(null)).IsNull();
        await Assert.That(CodebaseLineage.Of("")).IsNull();
    }

    /// <summary>The mudlibs, each placed by the FAMILY it publishes rather than by resemblance.</summary>
    /// <remarks>
    /// A mudlib and the driver beneath it are different software and one lineage, which is the
    /// question this facet asks.
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
        // Legends of the Jedi is a SMAUG descendant by any account but its own, which publishes
        // FAMILY Custom. A declaration a game made about itself outranks a resemblance we noticed.
        await Assert.That(CodebaseLineage.Of("LotJ 4.3")).IsNull();
        await Assert.That(CodebaseLineage.Of("Materia Magica 5.0.30")).IsNull();
        await Assert.That(CodebaseLineage.Of("Alter Aeon v2.25")).IsNull();
        await Assert.That(CodebaseLineage.Of("TeenyMUSH 0.91")).IsNull();
    }

    [Test]
    public async Task AVersionFusedToTheNameDoesNotCostTheLineage()
    {
        // ROM2.4/Haven splits to "ROM2", which no key matches, so trailing digits come off first.
        await Assert.That(CodebaseLineage.Of("ROM2.4/Haven")).IsEqualTo(CodebaseLineage.Diku);
        await Assert.That(CodebaseLineage.Of("ROM24 b6")).IsEqualTo(CodebaseLineage.Diku);

        // A letter after the marker still disqualifies it — the digit rule must not widen that edge.
        await Assert.That(CodebaseLineage.Of("ROMulus2 3")).IsNull();
    }

    [Test]
    public async Task ANeighbouringNameIsNotSweptIn()
    {
        // "MUX" is in the map; "MUXtreme" is not the same software and must not match on prefix.
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
