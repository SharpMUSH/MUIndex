using System.Reflection;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

using MUI.Web.Components;
using MUI.Web.Components.Layout;
using MUI.Web.Data;
using MUI.Web.Localization;

namespace MUI.Web.Tests;

/// <summary>
/// The three controls that change how a page is read, and the footer they came out of.
/// </summary>
/// <remarks>
/// Every page used to end in a footer row duplicating the bar's own nav, with the text mirror and
/// language switcher — the two non-duplicate items — buried at the end of it. Those two now live in
/// the bar beside the theme control: all three are ways to read the page, not places to go.
/// </remarks>
public class ReadingControlsTests
{
    [Test]
    public async Task TheBarOffersTheTextMirrorOnAPageThatHasOne()
    {
        var markup = await HeaderAsync(path: "/games");

        await Assert.That(Render.Words(markup)).Contains(Messages.For(Locales.SourceTag, "a11y.plainText"));
        await Assert.That(markup).Contains("class=\"mirror\" href=\"?plain=1\"");
    }

    /// <summary>
    /// The caret that holds them is a disclosure with a name, and it works with no script.
    /// </summary>
    /// <remarks>
    /// A <c>summary</c> whose whole content is <c>▾</c> has no accessible name — a screen reader
    /// announces "disclosure triangle, collapsed" and nothing else. The word is present for
    /// non-visual readers, with the glyph hidden from them (<c>aria-hidden</c>). <c>details</c>
    /// rather than a button because this site runs no script.
    /// </remarks>
    [Test]
    public async Task TheReadingCaretIsANamedDisclosureThatNeedsNoScript()
    {
        var markup = await HeaderAsync(path: "/games", review: true);

        await Assert.That(markup).Contains("<details class=\"read-menu\"");
        await Assert.That(markup).DoesNotContain("<script");

        var summary = markup[markup.IndexOf("<summary", markup.IndexOf("read-menu", StringComparison.Ordinal), StringComparison.Ordinal)..];
        summary = summary[..summary.IndexOf("</summary>", StringComparison.Ordinal)];

        await Assert.That(summary).Contains(Messages.For(Locales.SourceTag, "nav.reading"));
        await Assert.That(summary).Contains("sr-only");
        await Assert.That(summary).Contains("aria-hidden=\"true\"");
    }

    /// <summary>
    /// The theme control is not behind the caret.
    /// </summary>
    /// <remarks>It's the control a reader reaches for because they can't comfortably read the page — a click in front of that would be the wrong direction.</remarks>
    [Test]
    public async Task TheThemeControlStaysDrawnInTheBar()
    {
        var markup = await HeaderAsync(path: "/games", review: true);

        var panel = markup[markup.IndexOf("read-panel", StringComparison.Ordinal)..];
        panel = panel[..panel.IndexOf("</details>", StringComparison.Ordinal)];

        await Assert.That(panel).DoesNotContain("form class=\"theme\"");
        await Assert.That(markup).Contains("form class=\"theme\"");
    }

    /// <summary>
    /// And says nothing on a page that has none.
    /// </summary>
    /// <remarks>Six routes don't honour <c>?plain=1</c>; offering it there would be a promise the chrome quietly doesn't keep.</remarks>
    [Test]
    [Arguments("/account")]
    [Arguments("/account/sign-in")]
    [Arguments("/g/eldertale/mssp")]
    [Arguments("/g/eldertale/claim")]
    [Arguments("/not-found")]
    public async Task TheBarSaysNothingAboutTextOnAPageWithNoMirror(string path)
    {
        var markup = await HeaderAsync(path: path);

        await Assert.That(markup).DoesNotContain("plain=1");
    }

    /// <summary>
    /// The offer carries the question the page was already answering.
    /// </summary>
    /// <remarks>A bare <c>?plain=1</c> on a narrowed listing would answer with the whole catalogue as text, not the filtered page the reader was looking at.</remarks>
    [Test]
    public async Task TheOfferKeepsTheQueryThePageIsAnswering()
    {
        var markup = await HeaderAsync(path: "/games", query: "?codebase=PennMUSH&sort=players");

        await Assert.That(markup).Contains("href=\"?codebase=PennMUSH&amp;sort=players&amp;plain=1\"");
    }

    /// <summary>A reader already in the mirror is not offered the mirror.</summary>
    /// <remarks>The page they're on is the offer's own destination — a self-link would read as a way out of something.</remarks>
    [Test]
    public async Task ThePlainSurfaceDoesNotOfferItself()
    {
        var markup = await HeaderAsync(path: "/games", query: "?plain=1");

        await Assert.That(markup).DoesNotContain("class=\"mirror\"");
    }

    /// <summary>
    /// The switcher is in the document twice and carries no id either time.
    /// </summary>
    /// <remarks>
    /// The bar renders it above 880px, the nav disclosure below it, and CSS shows only one. Safe
    /// because the label wraps the control — a shared <c>for=</c>/<c>id</c> would duplicate the id
    /// and bind to whichever select the browser reached first.
    /// </remarks>
    [Test]
    public async Task TheLanguageSwitcherIsRenderedTwiceAndNamesItsControlWithoutAnId()
    {
        var markup = await HeaderAsync(path: "/games", review: true);

        await Assert.That(markup.Split("<form class=\"locale\"").Length - 1).IsEqualTo(2);
        await Assert.That(markup).DoesNotContain("locale-select");
        await Assert.That(markup).DoesNotContain("for=\"");
    }

    /// <summary>Both copies post the reader back to the page they were reading.</summary>
    /// <remarks>Counted inside the two <c>form.locale</c> elements rather than over the document — the theme control beside them carries the same return field.</remarks>
    [Test]
    public async Task BothCopiesOfTheSwitcherComeBackToThisPage()
    {
        var markup = await HeaderAsync(path: "/games", query: "?sort=players", review: true);

        var forms = markup.Split("<form class=\"locale\"").Skip(1).ToList();

        await Assert.That(forms.Count).IsEqualTo(2);

        foreach (var form in forms)
        {
            await Assert.That(form[..form.IndexOf("</form>", StringComparison.Ordinal)])
                .Contains("value=\"/games?sort=players\"");
        }
    }

    /// <summary>
    /// Every route on the site has been asked about, rather than merely not matching.
    /// </summary>
    /// <remarks><see cref="TextMirror"/> answers any path, silently, whether classified or not. This walks the assembly's own route table so a new page fails here until classified.</remarks>
    [Test]
    public async Task EveryRoutablePageIsClassified()
    {
        string[] mirrored =
        [
            "/", "/about", "/archive", "/crawler", "/ecosystem", "/find", "/g/{Slug}", "/games",
            "/rankings", "/reference", "/reference/clients/{Slug}", "/reference/codebases/{Slug}",
            "/reference/protocols/{Slug}", "/reference/{Slug}", "/submit",
        ];

        string[] bare =
        [
            "/account", "/account/sign-in", "/g/{Slug}/claim", "/g/{Slug}/mssp", "/games/random",
            "/not-found",
        ];

        var routes = typeof(TextMirror).Assembly.GetTypes()
            .Where(t => typeof(IComponent).IsAssignableFrom(t))
            .SelectMany(t => t.GetCustomAttributes<RouteAttribute>())
            .Select(r => r.Template)
            .Order(StringComparer.Ordinal)
            .ToList();

        await Assert.That(routes).IsEquivalentTo([.. mirrored, .. bare])
            .Because("a page was added or moved and nothing has said whether it reads as text");

        foreach (var template in mirrored)
        {
            await Assert.That(TextMirror.Offers(Sample(template)))
                .IsTrue()
                .Because($"{template} renders a text mirror and the bar does not offer it");
        }

        foreach (var template in bare)
        {
            await Assert.That(TextMirror.Offers(Sample(template)))
                .IsFalse()
                .Because($"{template} has no text mirror and the bar offers one anyway");
        }

        static string Sample(string template) => template.Replace("{Slug}", "eldertale");
    }

    /// <summary>Not one page still ends in the row this replaced.</summary>
    /// <remarks>Asserted over the rendered site, not the layout — the old footer was eleven separate copies, and a missed one is the failure this catches.</remarks>
    [Test]
    [Arguments("/")]
    [Arguments("/games")]
    [Arguments("/find")]
    [Arguments("/archive")]
    [Arguments("/ecosystem")]
    [Arguments("/rankings")]
    [Arguments("/reference")]
    [Arguments("/about")]
    [Arguments("/submit")]
    [Arguments("/g/eldertale")]
    public async Task NoPageCarriesAFooterAnyMore(string path)
    {
        await using var site = await SiteHost.StartAsync();

        var markup = await site.Client.GetStringAsync(path);

        await Assert.That(markup).DoesNotContain("card-footer");

        // Twice and no more: the bar's copy and the menu's (CSS shows only one). A third would mean
        // the page kept its own.
        await Assert.That(markup.Split("plain=1").Length - 1)
            .IsEqualTo(2)
            .Because($"{path} still offers the text mirror somewhere of its own");

        foreach (var slot in new[] { "class=\"read-panel\"", "class=\"menu-reading\"" })
        {
            var at = markup.IndexOf(slot, StringComparison.Ordinal);

            await Assert.That(at).IsGreaterThan(-1).Because($"{path} has no {slot}");
            await Assert.That(markup[at..(at + 400)]).Contains("plain=1");
        }
    }

    private static Task<string> HeaderAsync(string path, string query = "", bool review = false) =>
        Render.ComponentAsync<MainLayout>(new Dictionary<string, object?>(), services =>
        {
            services.AddSingleton(new CatalogueSource(IsMeasured: true));
            services.AddCascadingValue(_ => Context(path, query, review));
        });

    private static HttpContext Context(string path, string query, bool review)
    {
        var context = new DefaultHttpContext();

        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);

        // English is the only shipped locale, so tests that need the switcher visible ask for the
        // review build (with review locales), same as a developer sees.
        if (review)
        {
            var services = new ServiceCollection();

            services.AddSingleton<IHostEnvironment>(new ReviewEnvironment());
            context.RequestServices = services.BuildServiceProvider();
        }

        return context;
    }

    private sealed class ReviewEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "MUI.Web.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
