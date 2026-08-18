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
/// <para>
/// Every page ended with the same row: <em>all games · archive · submit · plain text</em> and the
/// language switcher, under a bar that already carried the first three. So the last thing a reader
/// met on every page of the site was navigation they had been given at the top, and the two things
/// in that row which were <em>not</em> duplicates — the text mirror and the language switcher —
/// were at the end of it.
/// </para>
/// <para>
/// The duplicates are gone and the two survivors are in the bar beside the theme control, which is
/// the third of the same kind: not a place to go, a way to read the place you are.
/// </para>
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
    /// <para>
    /// A <c>summary</c> whose whole content is <c>▾</c> has no accessible name: a screen reader
    /// announces "disclosure triangle, collapsed" and nothing about what is behind it. The word is
    /// in there for whoever is not looking at the glyph, and the glyph is hidden from them so the
    /// name is not "reading ▾".
    /// </para>
    /// <para>
    /// <c>details</c> rather than a button, because this site runs no script — the same reason the
    /// nav's own menu is one and the reason the switcher is a form with a submit.
    /// </para>
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
    /// <remarks>
    /// It is the one of the three a reader reaches for <em>because</em> they cannot comfortably read
    /// the page, and a click in front of that is the wrong direction. It is also what pays for the
    /// caret: with its own label taken out of the drawing the cluster measures 105px where the theme
    /// alone used to, so the bar never grew.
    /// </remarks>
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
    /// <remarks>
    /// Six routes on this site do not honour the flag. <c>/account/sign-in?plain=1</c> answers with
    /// the sign-in page it always answers with, so an offer there is not a broken link a browser can
    /// report — it is a promise the site quietly does not keep, made in the chrome of every page.
    /// </remarks>
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
    /// <remarks>
    /// Four of the eleven footers this replaces wrote a bare <c>?plain=1</c>. On a listing narrowed
    /// to three facets that is not "this page as text", it is the whole catalogue as text — the
    /// reader's question dropped on the floor by the control that offered to answer it differently.
    /// </remarks>
    [Test]
    public async Task TheOfferKeepsTheQueryThePageIsAnswering()
    {
        var markup = await HeaderAsync(path: "/games", query: "?codebase=PennMUSH&sort=players");

        await Assert.That(markup).Contains("href=\"?codebase=PennMUSH&amp;sort=players&amp;plain=1\"");
    }

    /// <summary>A reader already in the mirror is not offered the mirror.</summary>
    /// <remarks>
    /// The page they are on <em>is</em> the offer's destination, and a self-link in the site's
    /// chrome reads as a way out of something. The footers got this right by rendering inside the
    /// branch that draws the graphical page; the bar renders either way and has to ask.
    /// </remarks>
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
    /// The bar holds it above 880px and the nav disclosure below, and CSS shows one — the same trade
    /// <c>submit</c> already makes one step down the ladder, because nothing moves a box between two
    /// flex containers. That is only safe because the label wraps the control now: <c>for=</c> and
    /// <c>id="locale-select"</c> would have been a duplicate id in one document and a label naming
    /// whichever select the browser reached first.
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
    /// <remarks>
    /// Counted inside the two <c>form.locale</c> elements rather than over the document, because the
    /// theme control beside them carries the same return field and for the same reason.
    /// </remarks>
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
    /// <remarks>
    /// <see cref="TextMirror"/> answers a path, which means a page added later gets an answer
    /// whether or not anybody thought about it — and the wrong answer is silent in both directions.
    /// This walks the assembly's own route table and pins each one, so adding a page fails here
    /// until somebody says which it is.
    /// </remarks>
    [Test]
    public async Task EveryRoutablePageIsClassified()
    {
        string[] mirrored =
        [
            "/", "/about", "/archive", "/ecosystem", "/find", "/g/{Slug}", "/games", "/rankings",
            "/reference", "/reference/clients/{Slug}", "/reference/codebases/{Slug}",
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

        // The route as a reader reaches it. Nothing on this site routes on the slug's shape, so any
        // slug stands for every slug.
        static string Sample(string template) => template.Replace("{Slug}", "eldertale");
    }

    /// <summary>Not one page still ends in the row this replaced.</summary>
    /// <remarks>
    /// Asserted over the rendered site rather than over the layout, because the footer was eleven
    /// separate copies in eleven pages and deleting ten of them is the failure this catches.
    /// </remarks>
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

        // Twice and no more: the bar's copy and the menu's, of which CSS shows one — the same trade
        // `submit` makes at the width below. A third would be a page that kept its own.
        await Assert.That(markup.Split("plain=1").Length - 1)
            .IsEqualTo(2)
            .Because($"{path} still offers the text mirror somewhere of its own");

        // And the two are the two the bar renders, not a survivor at the bottom of the column.
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

        // The switcher draws nothing where there is one language to choose between, and English is
        // the only shipped locale — so the tests that look at it ask for the build that lists the
        // review locales too, which is the same thing a developer sees. Asked of the request's own
        // services, which is where LocaleRouting.IsReviewBuild looks.
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
