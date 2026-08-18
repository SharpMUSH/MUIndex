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
/// Previously the header carried nine catalogue links and no account link, so §8.5's entire write
/// surface sat behind a door with no handle — reachable only by typing <c>/account</c> from memory.
/// The account slot is deliberately not a tenth item in the catalogue nav, since that list names
/// places to browse and an operator's own page isn't one of them.
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
    /// <remarks>Every page renders this header, so a lookup here is a lookup per page view forever — the link says what it leads to, and the dashboard does the greeting.</remarks>
    [Test]
    public async Task TheHeaderReadsNothingButWhetherThereIsAPrincipal()
    {
        // No IUserStore, UserManager, or IGameQueries registered — a header needing any of them would throw.
        await Assert.That(await HeaderAsync(signedIn: true)).IsNotNull();
    }

    [Test]
    public async Task TheCatalogueLinksAreTwoLabelledGroupsAndTheActionsAreNotInThem()
    {
        var markup = await HeaderAsync(signedIn: false);

        await Assert.That(markup).Contains("aria-label=\"browse\"");
        await Assert.That(markup).Contains("aria-label=\"learn\"");

        var catalogues = markup[markup.IndexOf("site-nav", StringComparison.Ordinal)..
            markup.IndexOf("nav class=\"account", StringComparison.Ordinal)];

        await Assert.That(catalogues).Contains("/games");
        await Assert.That(catalogues).Contains("/rankings");
        await Assert.That(catalogues).DoesNotContain("/about");

        // Submit lives in `menu-tail`, shown only when the right-hand cluster has room.
        var groups = catalogues[..catalogues.IndexOf("menu-tail", StringComparison.Ordinal)];

        await Assert.That(groups).DoesNotContain("/submit");
        await Assert.That(catalogues).Contains("/submit");
    }

    [Test]
    public async Task EveryCatalogueLinkIsInTheDocumentExactlyOnce()
    {
        // A second copy of the links inside the collapsed menu would put two `aria-current="page"`
        // markers in one document.
        var markup = await HeaderAsync(signedIn: false, path: "/games");

        foreach (var href in new[] { "/games", "/find", "/games/random", "/archive", "/reference", "/ecosystem", "/rankings" })
        {
            await Assert.That(markup.Split($"href=\"{href}\"").Length - 1).IsEqualTo(1);
        }
    }

    [Test]
    public async Task TheCurrentPageIsMarkedAndTheMarkerIsDrawnInsideTheItem()
    {
        var markup = await HeaderAsync(signedIn: false, path: "/reference/protocols/mssp");

        await Assert.That(markup).Contains("href=\"/reference\" class=\"on\" aria-current=\"page\"");
        await Assert.That(markup.Split("aria-current").Length - 1).IsEqualTo(1);
    }

    [Test]
    public async Task TheHomePageDoesNotMarkEverySectionAsCurrent()
    {
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
    /// <remarks>Cascaded rather than authenticated through a scheme — §8.2's passkey-only sign-in means a test host can't produce a real session without an authenticator.</remarks>
    private static HttpContext Context(bool signedIn, string path = "/games")
    {
        var context = new DefaultHttpContext();

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
    /// <remarks><c>/games/random</c> is its own destination in the same bar as <c>/games</c>; a naive match would mark both. The most specific match wins.</remarks>
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
