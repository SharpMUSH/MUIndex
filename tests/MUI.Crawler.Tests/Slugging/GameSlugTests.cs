namespace MUI.Crawler.Tests;

/// <summary>The URL segment a game is minted with (spec §5.7).</summary>
public class GameSlugTests
{
    [Test]
    public async Task NonAlphanumericsCollapseToSingleHyphens()
    {
        await Assert.That(GameSlug.Mint("Tidewater  Nights!!")).IsEqualTo("tidewater-nights");
        await Assert.That(GameSlug.Mint("M*U*S*H")).IsEqualTo("m-u-s-h");
    }

    [Test]
    public async Task AnAccentIsFoldedRatherThanPunched()
    {
        await Assert.That(GameSlug.Mint("Café Noir")).IsEqualTo("cafe-noir");
    }

    [Test]
    public async Task ANameThatLeavesNothingBehindDoesNotBecomeSomethingInvented()
    {
        await Assert.That(GameSlug.Mint("！？")).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ACollisionTakesANumericSuffix()
    {
        // Two unedited PennMUSHes really do want the same slug, and both are entitled to a URL.
        var taken = new HashSet<string>(StringComparer.Ordinal) { "corvid", "corvid-2" };

        var slug = await GameSlug.UniqueAsync(
            "Corvid", (candidate, _) => Task.FromResult(taken.Contains(candidate)));

        await Assert.That(slug).IsEqualTo("corvid-3");
    }

    [Test]
    public async Task ANamelessGameStillGetsAUrl()
    {
        var slug = await GameSlug.UniqueAsync("...", (_, _) => Task.FromResult(false));

        await Assert.That(slug).IsEqualTo("game");
    }

    [Test]
    public async Task ANameInAScriptTheFoldCannotKeepIsMintedFromTheAddressInstead()
    {
        // Both callers know an address — the one a probe answered at, or the URL the game already
        // has — and the word "game" is a URL only one game can hold, whatever it is called.
        var slug = await GameSlug.UniqueAsync(
            "엘리시안 전기", (_, _) => Task.FromResult(false), "110.10.160.150:4001");

        await Assert.That(slug).IsEqualTo("110-10-160-150-4001");
    }

    [Test]
    public async Task AnAddressThatFoldsToNothingEitherStillGetsAUrl()
    {
        var slug = await GameSlug.UniqueAsync("엘리시안 전기", (_, _) => Task.FromResult(false), "！？");

        await Assert.That(slug).IsEqualTo("game");
    }

    [Test]
    public async Task ASlugIsBounded()
    {
        var slug = GameSlug.Mint(new string('a', 500));

        await Assert.That(slug.Length).IsEqualTo(GameSlug.MaxLength);
    }

    [Test]
    public async Task TheLastResortReallyDoesTerminate()
    {
        // Regression: the fallback used to slice a fixed length off the GUID-appended result, which
        // threw for any stem shorter than 31 characters. A listing dying on ArgumentOutOfRangeException
        // is a worse answer to upstream collisions than an ugly URL.
        var slug = await GameSlug.UniqueAsync("Corvid", (_, _) => Task.FromResult(true));

        await Assert.That(slug).StartsWith("corvid-");
        await Assert.That(slug.Length).IsLessThanOrEqualTo(GameSlug.MaxLength);
    }

    [Test]
    public async Task TheLastResortIsStillBoundedForALongName()
    {
        var slug = await GameSlug.UniqueAsync(new string('a', 500), (_, _) => Task.FromResult(true));

        await Assert.That(slug.Length).IsEqualTo(GameSlug.MaxLength);
    }
}
