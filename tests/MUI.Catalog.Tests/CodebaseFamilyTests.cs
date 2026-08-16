using MUI.Catalog;

namespace MUI.Catalog.Tests;

/// <summary>
/// The fold that turns a <c>CODEBASE</c> value into the family the panel counts and the dashboard
/// groups on — and, since the fold <em>is</em> the matching rule, the rule a reference page's
/// headline count is taken over.
/// </summary>
/// <remarks>
/// It has to be tight in both directions. Too loose and a name gets truncated mid-phrase, which puts
/// a codebase on a public page under a name nobody uses, or a neighbouring family is absorbed into a
/// count that is wrong in the direction nobody checks — larger, plausible, and attached to the wrong
/// page. Too tight and one codebase's share is spread across every patchlevel in the wild, which
/// answers no question anybody asked.
/// </remarks>
public class CodebaseFamilyTests
{
    [Test]
    [Arguments("PennMUSH 1.8.8p0", "PennMUSH")]
    [Arguments("PennMUSH 1.8.7", "PennMUSH")]
    [Arguments("TinyMUX 2.12", "TinyMUX")]
    [Arguments("CoffeeMud v5.9", "CoffeeMud")]
    [Arguments("Evennia", "Evennia")]
    public async Task ATrailingVersionIsFoldedAway(string reported, string family) =>
        await Assert.That(CodebaseFamily.Of(reported)).IsEqualTo(family);

    [Test]
    [Arguments("Midnight Sun")]
    [Arguments("Ancient Anguish")]
    public async Task ATwoWordNameKeepsBothWords(string reported) =>
        await Assert.That(CodebaseFamily.Of(reported)).IsEqualTo(reported);

    /// <summary>Everything from the version onwards goes, not merely a trailing version token.</summary>
    /// <remarks>
    /// Every value here is live, off <c>mu-index.com/ecosystem</c>. Folding only a trailing token
    /// left one codebase published as four — <c>MUX</c>, <c>MUX 2.12.0.3 Alpha</c>,
    /// <c>MUX 2.13.0.0-MP MPARK-ST</c> and <c>MUX 2.13.0.0-MP MPARK-BB-ST</c> each had their own bar
    /// on the dashboard — because what a game appends after its version is a build tag and never the
    /// name of a different codebase.
    /// </remarks>
    [Test]
    [Arguments("MUX 2.13.0.0-MP MPARK-ST", "MUX")]
    [Arguments("MUX 2.12.0.3 Alpha", "MUX")]
    [Arguments("RhostMUSH Alpha 4.1.0RL(A).p2", "RhostMUSH")]
    [Arguments("RhostMUSH 4.2.2-3RL(A)", "RhostMUSH")]
    [Arguments("CobraMUSH v0.73p4 [fspace]", "CobraMUSH")]
    [Arguments("LDMud 3.6.6 (3.6.6)", "LDMud")]
    [Arguments("Smaug 1.4a (Heavily Modified)", "Smaug")]
    [Arguments("TMI-2 1.3-pre2(modified)", "TMI-2")]
    [Arguments("EmpireMUD 2.0 beta", "EmpireMUD")]
    [Arguments("FluffOS v2021 Colony", "FluffOS")]
    [Arguments("Discworld lib (current)", "Discworld lib")]
    [Arguments("AresMUSH version", "AresMUSH")]
    [Arguments("PennMUSH 1.7.1 pl3 with Elendor Mods", "PennMUSH")]
    public async Task TheVersionAndEverythingAfterItIsFoldedAway(string reported, string family) =>
        await Assert.That(CodebaseFamily.Of(reported)).IsEqualTo(family);

    /// <summary>A real two-word name is not a name plus a qualifier, and survives whole.</summary>
    /// <remarks>
    /// The cost of the wider fold, paid for by a closed list rather than a heuristic. Every one of
    /// these is a live catalogue value, and a rule clever enough to drop <c>Alpha</c> by its shape
    /// would take <c>Aeon</c> or <c>Magica</c> with it.
    /// </remarks>
    [Test]
    [Arguments("Alter Aeon")]
    [Arguments("Dead Souls")]
    [Arguments("Materia Magica")]
    [Arguments("Moral Decay")]
    [Arguments("Galaxy Engine")]
    [Arguments("Midnight Sun")]
    [Arguments("Diku Merc Rom RoT AoD")]
    [Arguments("3Scapes mudlib")]
    [Arguments("Heavily modified SMAUG")]
    [Arguments("Original / Loosely Diku")]
    public async Task ARealMultiWordNameKeepsEveryWord(string reported) =>
        await Assert.That(CodebaseFamily.Of(reported)).IsEqualTo(reported);

    [Test]
    public async Task AFoldNeverTruncatesMidPhrase()
    {
        // The failure the fold has to avoid on the other side: cutting inside a parenthesis leaves
        // "Rhost 4.0.4 (patchlevel", which is a name no game runs and no reader recognises. Cutting
        // at the version is not that cut — it stops before the version begins.
        await Assert.That(CodebaseFamily.Of("Rhost 4.0.4 (patchlevel 1)")).IsEqualTo("Rhost");
    }

    [Test]
    public async Task AVersionOnItsOwnIsNotFoldedToNothing()
    {
        // A game whose whole CODEBASE is a version has published no name, and folding it away would
        // put an empty bar on the dashboard. The cut is never taken from the first word.
        await Assert.That(CodebaseFamily.Of("2.12")).IsEqualTo("2.12");

        // The qualifier strip stops at the first word for the same reason, so this keeps a version
        // where a name should be rather than keeping nothing at all.
        await Assert.That(CodebaseFamily.Of("2.12 beta")).IsEqualTo("2.12");
    }

    [Test]
    public async Task SurroundingWhitespaceIsNotPartOfTheName()
    {
        // MSSP values are hand-typed into a config file and arrive as the game spelled them, so this
        // is the ordinary case rather than the pathological one.
        await Assert.That(CodebaseFamily.Of("  PennMUSH 1.8.8p0  ")).IsEqualTo("PennMUSH");
    }

    [Test]
    public async Task EveryPatchlevelFoldsToOneFamily()
    {
        // The question a reader asks is never version-shaped, so every patchlevel gathers — and it
        // gathers by folding to one string rather than by any looser test, because the panel counts
        // what the fold returns and a count is a promise about what clicking it returns.
        await Assert.That(CodebaseFamily.For("PennMUSH 1.8.8p0")).IsEqualTo("PennMUSH");
        await Assert.That(CodebaseFamily.For("PennMUSH 1.8.5")).IsEqualTo("PennMUSH");
        await Assert.That(CodebaseFamily.For("PennMUSH")).IsEqualTo("PennMUSH");
    }

    [Test]
    public async Task ANeighbouringFamilyIsNotAbsorbed()
    {
        // The three spellings this rule exists for: a family whose name is a prefix of another's, a
        // family whose name is a suffix of another's, and the one a prefix test gets wrong outright.
        await Assert.That(CodebaseFamily.For("TinyMUX 2.12")).IsNotEqualTo("TinyMUSH");
        await Assert.That(CodebaseFamily.For("LambdaMOO 1.8.1")).IsNotEqualTo("MOO");
        await Assert.That(CodebaseFamily.For("ROMulus 3")).IsNotEqualTo("ROM");
        await Assert.That(CodebaseFamily.For("ROM 2.4")).IsEqualTo("ROM");
    }

    [Test]
    public async Task AGameWithNoIdentifiedCodebaseBelongsToNoFamily()
    {
        // Failing to identify a codebase is a measurement, and it is not a measurement of this. Null
        // rather than an empty string, because the facets spell that absence as its own value.
        await Assert.That(CodebaseFamily.For(null)).IsNull();
        await Assert.That(CodebaseFamily.For("")).IsNull();
        await Assert.That(CodebaseFamily.For("   ")).IsNull();
    }
}
