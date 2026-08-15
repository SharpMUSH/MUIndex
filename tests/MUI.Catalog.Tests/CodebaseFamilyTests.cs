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

    [Test]
    public async Task ATrailingTokenThatIsNotAVersionIsLeftAlone()
    {
        // The failure this guards against is a truncation rather than a mis-grouping: folding on
        // "starts with a digit" alone leaves "Rhost 4.0.4 (patchlevel", which is a name no game runs
        // and no reader recognises.
        await Assert.That(CodebaseFamily.Of("Rhost 4.0.4 (patchlevel 1)"))
            .IsEqualTo("Rhost 4.0.4 (patchlevel 1)");
    }

    [Test]
    public async Task AVersionOnItsOwnIsNotFoldedToNothing() =>
        await Assert.That(CodebaseFamily.Of("2.12")).IsEqualTo("2.12");

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
