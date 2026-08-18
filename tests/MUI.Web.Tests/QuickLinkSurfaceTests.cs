using MUI.Catalog;
using MUI.Web.Components;

namespace MUI.Web.Tests;

/// <summary>
/// The reach row as it renders: what goes in the <c>href</c>, what a screen reader hears, and what
/// the row promises a search engine about a destination nobody here chose.
/// </summary>
public class QuickLinkSurfaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 0, 0, 0, TimeSpan.Zero);

    private static QuickLink Link(LinkKind kind, string field, string href, string shown) =>
        new(kind, field, href, shown, FieldSource.Mssp, Now.AddDays(-2), IsStale: false);

    private static readonly QuickLink[] Three =
    [
        Link(LinkKind.Website, "WEBSITE", "https://www.slothmud.org/", "https://www.slothmud.org/"),
        Link(LinkKind.Discord, "DISCORD", "https://discord.gg/5GtCY52", "https://discord.gg/5GtCY52"),
        Link(LinkKind.Email, "CONTACT", "mailto:kali@example.org", "kali@example.org"),
    ];

    private static Task<string> RowAsync(IReadOnlyList<QuickLink> links) =>
        Render.ComponentAsync<ReachRow>(new() { ["Links"] = links, ["Now"] = Now });

    [Test]
    public async Task EveryLinkCarriesNoopenerNofollowAndUgc()
    {
        // noopener stops the opened page reaching back through window.opener; nofollow says we
        // aren't voting for a destination we didn't choose; ugc says who did choose it.
        var html = await RowAsync(Three);

        await Assert.That(html.Split("<a ").Length - 1).IsEqualTo(3);
        await Assert.That(html.Split("rel=\"noopener nofollow ugc\"").Length - 1).IsEqualTo(3);
    }

    [Test]
    public async Task NoLinkOpensAWindowOnTheReadersBehalf()
    {
        // target=_blank reliably breaks the back button; the reader's own browser decides instead.
        await Assert.That(await RowAsync(Three)).DoesNotContain("target=");
    }

    [Test]
    public async Task AnIconIsHiddenFromAssistiveTechnologyAndTheNameIsNot()
    {
        var html = await RowAsync(Three);

        // The glyph announces nothing; the label announces everything.
        await Assert.That(html).Contains("aria-hidden=\"true\"");
        await Assert.That(html).Contains("Website");
        await Assert.That(html).Contains("This game on Discord");
        await Assert.That(html).Contains("Email");
    }

    [Test]
    public async Task ThePlatformsOwnNameIsNeverTranslated()
    {
        // Machine voice, like a hostname — the brand is an argument, so a locale can't translate it.
        var html = await Render.ComponentAsync<ReachRow>(new()
        {
            ["Links"] = new[] { Link(LinkKind.Bluesky, "BLUESKY", "https://bsky.app/profile/x", "https://bsky.app/profile/x") },
            ["Now"] = Now,
        });

        await Assert.That(html).Contains("Bluesky");
    }

    [Test]
    public async Task TheRowIsNotDrawnAtAllForAGameThatPublishedNoAddress()
    {
        // An empty <ul> with a label on it is a heading announcing nothing.
        await Assert.That(await RowAsync([])).DoesNotContain("<ul");
    }

    [Test]
    public async Task TheTooltipSaysWhoSaidSoAndHowOldItIs()
    {
        // The chips stay below; provenance travels with the link in its title instead.
        var html = await RowAsync(Three);

        await Assert.That(html).Contains("declared");
        await Assert.That(html).Contains("kali@example.org");
    }

    [Test]
    public async Task EveryKindHasAGlyphAndNoneOfThemCarriesATitleElement()
    {
        foreach (var kind in Enum.GetValues<LinkKind>())
        {
            var svg = LinkIcons.Svg(kind);

            await Assert.That(svg).StartsWith("<svg").Because($"{kind} has no glyph");

            // A <title> inside the SVG would be a second, English accessible name beside the translated one.
            await Assert.That(svg).DoesNotContain("<title>");
        }
    }
}
