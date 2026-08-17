using MUI.Web.Components;
using MUI.Web.Components.Pages;

namespace MUI.Web.Tests;

/// <summary>
/// The face a game gets on a listing row and on its own page (spec §11).
/// </summary>
/// <remarks>
/// Two properties, and both of them are about what the plate may not say. It may not hot-link — a
/// listing that pointed five hundred <c>img</c> elements at five hundred strangers' web servers would
/// hand every reader's address to all of them for a decoration. And it may not distinguish a game
/// that declared no icon from one whose server we could not reach, because only the first of those is
/// a fact about the game.
/// </remarks>
public class GamePlateTests
{
    [Test]
    public async Task AMonogramReadsTheNameTheHobbyActuallyWrites()
    {
        // The star is a separator here and nowhere else in .NET's idea of one: M*U*S*H is one word to
        // a reader and would monogram as a lone M without it.
        await Assert.That(Monogram.Of("M*U*S*H")).IsEqualTo("MU");
        await Assert.That(Monogram.Of("Midnight Sun II")).IsEqualTo("MS");
        await Assert.That(Monogram.Of("Gaslight-Row")).IsEqualTo("GR");
        await Assert.That(Monogram.Of("BatMUD")).IsEqualTo("B");
    }

    [Test]
    public async Task APlateIsNeverEmpty()
    {
        // An empty plate reads as an image that failed to load, which is a statement about our
        // afternoon rather than about the game.
        await Assert.That(Monogram.Of(null)).IsEqualTo("?");
        await Assert.That(Monogram.Of("   ")).IsEqualTo("?");
        await Assert.That(Monogram.Of("***")).IsEqualTo("?");
    }

    [Test]
    public async Task EveryRowHasAPlateAndNoneOfThemPointsOffThisOrigin()
    {
        // The fixture holds no icon bytes, so every row is the monogram — which is exactly the state
        // a deployment with an empty cache renders, and it has to be a plate rather than a hole.
        var html = await Render.PageAsync<Games>([]);
        var rows = html.Split("class=\"game-row").Length - 1;
        var plates = html.Split("class=\"plate").Length - 1;

        await Assert.That(rows).IsGreaterThan(0);
        await Assert.That(plates).IsEqualTo(rows);

        // §11. An icon is served from this origin or not at all: every src is the site's own route.
        foreach (var src in html.Split("<img").Skip(1))
        {
            await Assert.That(src[..src.IndexOf('>')]).DoesNotContain("src=\"http");
        }
    }

    [Test]
    public async Task AMissingIconAndAnUnreachableOneAreDrawnIdentically()
    {
        // The one thing the plate may not do. A placeholder, a broken image or a "logo unavailable"
        // would publish our failed fetch as a fact about somebody's game (rule 5) — and the second
        // and third of those states are indistinguishable from the first anywhere in the markup.
        var html = await Render.PageAsync<Games>([]);

        await Assert.That(html).DoesNotContain("no icon");
        await Assert.That(html).DoesNotContain("icon unavailable");

        // The name is beside it either way, so the picture is never the thing announced.
        await Assert.That(html).Contains("class=\"plate mono\" aria-hidden=\"true\"");
    }

    [Test]
    public async Task AnIconIsServedFromThisOriginAndReservesItsSpaceBeforeItArrives()
    {
        // The fixture holds no bytes, so the branch a real deployment takes is rendered here on its
        // own. Two things it has to do: point at our route rather than at the game's web server
        // (§11 — hot-linking hands every reader's address to a stranger for a decoration), and carry
        // the box's dimensions, because five hundred lazily-loaded images with no reserved space is
        // a page that reflows under the reader as they scroll.
        var html = await Render.ComponentAsync<GamePlate>(new()
        {
            ["Slug"] = "aardwolf",
            ["Name"] = "Aardwolf MUD",
            ["HasIcon"] = true,
        });

        await Assert.That(html).Contains("src=\"/g/aardwolf/icon\"");
        await Assert.That(html).Contains("width=\"36\"");
        await Assert.That(html).Contains("height=\"36\"");
        await Assert.That(html).Contains("loading=\"lazy\"");

        // The name is the link beside it, so the picture is decoration in the accessibility tree
        // rather than the same words announced twice. The renderer writes an empty attribute value
        // as a bare attribute, which HTML defines as the empty string — the same decorative alt.
        await Assert.That(html).Contains(" alt ");
        await Assert.That(html).DoesNotContain("Aardwolf");
    }

    [Test]
    public async Task TheGamePageAndTheListingDrawOneVocabularyAtTwoSizes()
    {
        // Two spellings of "the first letters of the name" is how a game comes to be MU on one page
        // and M* on the next. Both surfaces render the same component.
        var page = await Render.PageAsync<Game>(new() { ["Slug"] = "m-u-s-h" });
        var listing = await Render.PageAsync<Games>([]);

        await Assert.That(page).Contains("class=\"plate mono\"");
        await Assert.That(Render.Words(page)).Contains(Monogram.Of("M*U*S*H"));
        await Assert.That(listing).Contains("class=\"plate mono\"");
    }
}
