using MUI.Catalog;

namespace MUI.Catalog.Tests;

/// <summary>
/// The fold that turns a <c>CODEBASE</c> value into the family the dashboard groups on.
/// </summary>
/// <remarks>
/// It has to be tight in both directions. Too loose and a name gets truncated mid-phrase, which puts
/// a codebase on a public page under a name nobody uses; too tight and one codebase's share is spread
/// across every patch level in the wild, which answers no question anybody asked.
/// <summary>
/// The codebase facet's matching rule, which a reference page's headline count is taken over.
/// </summary>
/// <remarks>
/// A facet that absorbs a neighbouring family produces a count that is wrong in the direction nobody
/// checks — larger, plausible, and attached to the wrong page.
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
    public async Task AVersionedCodebaseBelongsToItsFamily()
    {
        // The question a reader asks is never version-shaped, so every patchlevel gathers.
        await Assert.That(CodebaseFamily.Matches("PennMUSH 1.8.8p0", "PennMUSH")).IsTrue();
        await Assert.That(CodebaseFamily.Matches("PennMUSH 1.8.5", "PennMUSH")).IsTrue();
        await Assert.That(CodebaseFamily.Matches("PennMUSH", "PennMUSH")).IsTrue();
    }

    [Test]
    public async Task MatchingIsCaseInsensitive()
    {
        await Assert.That(CodebaseFamily.Matches("pennmush 1.8.8p0", "PennMUSH")).IsTrue();
    }

    [Test]
    public async Task ANeighbouringFamilyIsNotAbsorbed()
    {
        // The two spellings this rule exists for: a family whose name is a prefix of another's, and
        // a family whose name is a suffix of another's.
        await Assert.That(CodebaseFamily.Matches("TinyMUX 2.12", "TinyMUSH")).IsFalse();
        await Assert.That(CodebaseFamily.Matches("LambdaMOO 1.8.1", "MOO")).IsFalse();
    }

    [Test]
    public async Task AMatchEndsAtAWordBoundary()
    {
        // Anchoring at the start is not enough on its own: ROM against ROMulus would pass it.
        await Assert.That(CodebaseFamily.Matches("ROMulus 3", "ROM")).IsFalse();
        await Assert.That(CodebaseFamily.Matches("ROM 2.4", "ROM")).IsTrue();
        await Assert.That(CodebaseFamily.Matches("ROM-2.4", "ROM")).IsTrue();
    }

    [Test]
    public async Task AGameWithNoIdentifiedCodebaseBelongsToNoFamily()
    {
        // Failing to identify a codebase is a measurement, and it is not a measurement of this.
        await Assert.That(CodebaseFamily.Matches(null, "PennMUSH")).IsFalse();
        await Assert.That(CodebaseFamily.Matches("", "PennMUSH")).IsFalse();
    }

    [Test]
    public async Task NoFamilyMeansNoFilterRatherThanNoMatches()
    {
        // GameFilter.Codebase is optional, so an absent family has to leave the listing alone —
        // including for the games whose codebase we could not read.
        await Assert.That(CodebaseFamily.Matches("PennMUSH 1.8.8p0", null)).IsTrue();
        await Assert.That(CodebaseFamily.Matches(null, null)).IsTrue();
    }
}
