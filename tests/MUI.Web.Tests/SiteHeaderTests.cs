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

    private static Task<string> HeaderAsync(bool signedIn) =>
        Render.ComponentAsync<MainLayout>(new Dictionary<string, object?>(), services =>
        {
            services.AddSingleton(new CatalogueSource(IsMeasured: true));
            services.AddCascadingValue(_ => Context(signedIn));
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
    private static HttpContext Context(bool signedIn)
    {
        var context = new DefaultHttpContext();

        if (signedIn)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString())],
                "Test"));
        }

        return context;
    }
}
