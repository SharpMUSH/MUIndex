using MUI.Web.Components;
using MUI.Web.Components.Pages;

namespace MUI.Web.Tests;

/// <summary>
/// The face a game gets on a listing row and on its own page (spec §11).
/// </summary>
/// <remarks>May not hot-link (§11 — hands every reader's address to a stranger's server for a decoration) and may not distinguish "declared no icon" from "server unreachable", since only the first is a fact about the game.</remarks>
public class GamePlateTests
{
    [Test]
    public async Task AMonogramReadsTheNameTheHobbyActuallyWrites()
    {
        // The star is a separator here, not in .NET's idea of one — M*U*S*H would monogram as a lone M without it.
        await Assert.That(Monogram.Of("M*U*S*H")).IsEqualTo("MU");
        await Assert.That(Monogram.Of("Midnight Sun II")).IsEqualTo("MS");
        await Assert.That(Monogram.Of("Gaslight-Row")).IsEqualTo("GR");
        await Assert.That(Monogram.Of("BatMUD")).IsEqualTo("B");
    }

    [Test]
    public async Task APlateIsNeverEmpty()
    {
        // An empty plate reads as a failed image load, a statement about us, not the game.
        await Assert.That(Monogram.Of(null)).IsEqualTo("?");
        await Assert.That(Monogram.Of("   ")).IsEqualTo("?");
        await Assert.That(Monogram.Of("***")).IsEqualTo("?");
    }

    [Test]
    public async Task EveryRowDrawsAPlateAndNoIconPointsOffThisOrigin()
    {
        // Every row, not most: the plate's job on a listing is a left edge the eye can run down, and
        // an edge with gaps in it is furniture. Asserted per row rather than as a total — a page with
        // one row holding two plates and another holding none has the same total and none of the
        // edge, which is the failure this exists to catch.
        var html = await Render.PageAsync<Games>([]);
        var rows = Rows(html);

        await Assert.That(rows).IsNotEmpty();

        foreach (var row in rows)
        {
            await Assert.That(row.Split("class=\"plate").Length - 1).IsEqualTo(1);

            // The fixture holds bytes for nobody, so every row here is the fallback — which is the
            // half of the plate that PR #89's removal took away from 95% of the catalogue.
            await Assert.That(row).Contains("class=\"plate mono\" aria-hidden=\"true\"");
        }

        // §11: an icon is served from this origin or not at all.
        foreach (var src in html.Split("<img").Skip(1))
        {
            await Assert.That(src[..src.IndexOf('>')]).DoesNotContain("src=\"http");
        }
    }

    /// <summary>The listing's rows, one string each.</summary>
    /// <remarks>
    /// Bounded to the games list before splitting: past its close are other pages' plates and, on a
    /// row-by-row assertion, the tail after the last row would otherwise carry the whole document.
    /// The list holds no nested <c>ul</c>, so its first closing tag is its own.
    /// </remarks>
    private static string[] Rows(string html)
    {
        var open = html.IndexOf("<ul class=\"games\">", StringComparison.Ordinal);

        if (open < 0)
        {
            return [];
        }

        var close = html.IndexOf("</ul>", open, StringComparison.Ordinal);

        return [.. html[open..close].Split("class=\"game-row").Skip(1)];
    }

    [Test]
    public async Task AMissingIconAndAnUnreachableOneAreDrawnIdentically()
    {
        // A placeholder, broken image, or "logo unavailable" would publish our failed fetch as a fact
        // about the game (rule 5).
        var html = await Render.PageAsync<Game>(new() { ["Slug"] = "m-u-s-h" });

        await Assert.That(html).DoesNotContain("no icon");
        await Assert.That(html).DoesNotContain("icon unavailable");

        // No element at all where there's no icon — a generated monogram would invent a brand the
        // game never supplied.
        await Assert.That(html).DoesNotContain("class=\"plate");
    }

    [Test]
    public async Task AnIconIsServedFromThisOriginAndReservesItsSpaceBeforeItArrives()
    {
        // Two requirements: point at our route, not the game's server (§11), and carry the box's
        // dimensions so lazily-loaded images don't reflow the page as the reader scrolls.
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

        // The name is the link beside it, so the picture is decoration, not words announced twice.
        await Assert.That(html).Contains(" alt ");
        await Assert.That(html).DoesNotContain("Aardwolf");
    }

    [Test]
    public async Task TheGamePageInventsNoFaceForAGameThatPublishedNone()
    {
        // The page does not pass Fallback, so a game with no icon gets no element and the title
        // starts at the left edge. One plate at 96px is where a generated monogram would read as a
        // brand the game supplied; five hundred at 36px down a listing is where it reads as an edge,
        // which is why the listing does pass it and this asserts only the page.
        var page = await Render.PageAsync<Game>(new() { ["Slug"] = "m-u-s-h" });

        await Assert.That(page).DoesNotContain("class=\"plate");

        // Still one implementation behind the parameter, so the two surfaces can't diverge.
        var withFallback = await Render.ComponentAsync<GamePlate>(new()
        {
            ["Slug"] = "m-u-s-h",
            ["Name"] = "M*U*S*H",
            ["HasIcon"] = false,
            ["Fallback"] = true,
        });

        await Assert.That(withFallback).Contains("class=\"plate mono\" aria-hidden=\"true\"");
        await Assert.That(Render.Words(withFallback)).Contains(Monogram.Of("M*U*S*H"));
    }
}
