namespace MUI.Catalog.Tests;

/// <summary>
/// The fold that turns a <c>CODEBASE</c> value into the family the dashboard groups on.
/// </summary>
/// <remarks>
/// It has to be tight in both directions. Too loose and a name gets truncated mid-phrase, which puts
/// a codebase on a public page under a name nobody uses; too tight and one codebase's share is spread
/// across every patch level in the wild, which answers no question anybody asked.
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
}
