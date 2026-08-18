using System.Security.Claims;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using MUI.Web.Components.Layout;
using MUI.Web.Data;

namespace MUI.Web.Tests;

/// <summary>
/// The site chrome, and the one link that was missing from it (spec §8).
/// </summary>
/// <remarks>
/// <para>
/// <b>An operator reached their own dashboard by remembering a URL nothing had ever shown them.</b>
/// The header carried nine catalogue links and no account link at all, so §8.5's entire write
/// surface — the enrichment fields, the connect-screen decision, the opt-out — sat behind a door
/// with no handle. Every one of those features was tested, rendered and reachable only by typing
/// <c>/account</c> from memory.
/// </para>
/// <para>
/// The account slot is deliberately not a tenth item in the catalogue nav: that list names places to
/// browse, and an operator's own page is not one of them.
/// </para>
/// </remarks>
public class SiteHeaderTests
{
    /// <summary>A reader with no account is offered one place to go, and it is not the dashboard.</summary>
    [Test]
    public async Task TheHeaderOffersSignInToAReaderWhoIsNotSignedIn()
    {
        var markup = await HeaderAsync(signedIn: false);

        await Assert.That(markup).Contains("/account/sign-in");
        await Assert.That(Render.Words(markup)).Contains("sign in");
    }

    /// <summary>And somebody signed in is offered their games, not the sign-in page again.</summary>
    [Test]
    public async Task TheHeaderOffersTheDashboardToAnOperatorWhoIsSignedIn()
    {
        var markup = await HeaderAsync(signedIn: true);

        await Assert.That(Render.Words(markup)).Contains("your games");
        await Assert.That(markup).DoesNotContain("/account/sign-in");
    }

    /// <summary>
    /// The slot asks the principal and nothing else.
    /// </summary>
    /// <remarks>
    /// Every page on this site renders this header, so a lookup here is a lookup per page view
    /// forever. The display name would be pleasant and costs a query against the user store on the
    /// listing, the rankings and every game page to render four words — so the link says what it
    /// leads to rather than who is holding it, and the dashboard does the greeting.
    /// </remarks>
    [Test]
    public async Task TheHeaderReadsNothingButWhetherThereIsAPrincipal()
    {
        // No IUserStore, no UserManager, no IGameQueries registered below: a header that needed any
        // of them would throw here rather than quietly costing a query on every page of the site.
        await Assert.That(await HeaderAsync(signedIn: true)).IsNotNull();
    }

    [Test]
    public async Task TheCatalogueLinksAreTwoLabelledGroupsAndTheActionsAreNotInThem()
    {
        // Nine links in one flat row, in no order anybody could name. They are two ideas — places to
        // browse, and things to read about the hobby — and two odd ones out: submit is an action
        // rather than a category, and about is site meta. Both moved to the far end, beside sign in.
        var markup = await HeaderAsync(signedIn: false);

        await Assert.That(markup).Contains("aria-label=\"browse\"");
        await Assert.That(markup).Contains("aria-label=\"learn\"");

        var catalogues = markup[markup.IndexOf("site-nav", StringComparison.Ordinal)..
            markup.IndexOf("nav class=\"account", StringComparison.Ordinal)];

        await Assert.That(catalogues).Contains("/games");
        await Assert.That(catalogues).Contains("/rankings");
        await Assert.That(catalogues).DoesNotContain("/about");

        // Submit is in the nav's markup, and it is not in either group: it lives in `menu-tail`,
        // which the bar shows only at the width where the right-hand cluster has run out of room
        // for it. A destination the bar cannot fit moves; it is never dropped.
        var groups = catalogues[..catalogues.IndexOf("menu-tail", StringComparison.Ordinal)];

        await Assert.That(groups).DoesNotContain("/submit");
        await Assert.That(catalogues).Contains("/submit");
    }

    [Test]
    public async Task EveryCatalogueLinkIsInTheDocumentExactlyOnce()
    {
        // The bar collapses into a disclosure on a narrow window, and the obvious way to build that
        // is a second copy of the links inside the menu. It is also wrong: two copies put two
        // `aria-current="page"` markers in one document and hand a screen reader the catalogue
        // twice. The disclosure wraps the same two groups instead, and CSS decides whether it is a
        // menu or a row.
        var markup = await HeaderAsync(signedIn: false, path: "/games");

        foreach (var href in new[] { "/games", "/find", "/games/random", "/archive", "/reference", "/ecosystem", "/rankings" })
        {
            await Assert.That(markup.Split($"href=\"{href}\"").Length - 1).IsEqualTo(1);
        }
    }

    [Test]
    public async Task TheCurrentPageIsMarkedAndTheMarkerIsDrawnInsideTheItem()
    {
        // aria-current is the fact. The drawing of it is an inset box-shadow in the stylesheet, not
        // a border or a padding change: either of those adds a pixel to the current item's box and
        // shifts every link beside it on arrival, which is the reported defect.
        var markup = await HeaderAsync(signedIn: false, path: "/reference/protocols/mssp");

        await Assert.That(markup).Contains("href=\"/reference\" class=\"on\" aria-current=\"page\"");
        await Assert.That(markup.Split("aria-current").Length - 1).IsEqualTo(1);
    }

    [Test]
    public async Task TheHomePageDoesNotMarkEverySectionAsCurrent()
    {
        // "/" is a prefix of every path on the site, so a section match that reached it would mark
        // the whole bar on the one page that has no item in it.
        var markup = await HeaderAsync(signedIn: false, path: "/");

        await Assert.That(markup).DoesNotContain("aria-current");
    }

    private static Task<string> HeaderAsync(bool signedIn, string path = "/games") =>
        Render.ComponentAsync<MainLayout>(new Dictionary<string, object?>(), services =>
        {
            services.AddSingleton(new CatalogueSource(IsMeasured: true));
            services.AddCascadingValue(_ => Context(signedIn, path));
        });

    /// <summary>
    /// The object the framework cascades to a page, in the two states a visitor can be in.
    /// </summary>
    /// <remarks>
    /// Cascaded rather than authenticated through a scheme, for the reason
    /// <see cref="AccountSurfaceTests"/> gives: §8.2 makes passkeys the only way in, so a test host
    /// has no way to produce a real session without an authenticator. The guard under test is the
    /// layout's own read of <c>Identity.IsAuthenticated</c>, and that runs either way.
    /// </remarks>
    private static HttpContext Context(bool signedIn, string path = "/games")
    {
        var context = new DefaultHttpContext();

        // The header asks it one other thing now: which page this is, so the item for it can be
        // marked. A path and nothing else — no route data, no endpoint.
        context.Request.Path = path;

        if (signedIn)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString())],
                "Test"));
        }

        return context;
    }

    /// <summary>
    /// One page is current, so one item is marked — even where two destinations nest.
    /// </summary>
    /// <remarks>
    /// A section matches its own root and everything under it, which is what makes a reference
    /// article mark "reference". But <c>/games/random</c> is its own destination in the same bar, so
    /// standing on it matched <c>/games</c> as well and a screen reader was handed two
    /// <c>aria-current="page"</c> markers in one document. The most specific match wins now.
    /// </remarks>
    [Test]
    [Arguments("/games/random", "/games/random")]
    [Arguments("/games", "/games")]
    [Arguments("/reference/protocols/mssp", "/reference")]
    [Arguments("/archive", "/archive")]
    public async Task ExactlyOneNavigationItemIsMarkedCurrent(string path, string expected)
    {
        var markup = await HeaderAsync(signedIn: false, path: path);

        var marked = System.Text.RegularExpressions.Regex
            .Matches(markup, "<a href=\"([^\"]+)\"[^>]*aria-current=\"page\"")
            .Select(m => m.Groups[1].Value)
            .ToList();

        await Assert.That(marked).IsEquivalentTo(new[] { expected });
    }
}
