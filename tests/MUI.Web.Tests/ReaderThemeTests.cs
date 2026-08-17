using System.Net;
using System.Security.Claims;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using MUI.Web.Components.Layout;
using MUI.Web.Data;
using MUI.Web.Theme;

namespace MUI.Web.Tests;

/// <summary>
/// The reader's own choice of theme — the cookie, the attribute it writes, and the control that
/// sets it.
/// </summary>
/// <remarks>
/// <para>
/// <c>app.css</c> has carried both themes and an explicit <c>data-theme</c> override since the
/// tokens were written; what was missing was any way for a reader to reach it. These are the tests
/// for the way.
/// </para>
/// <para>
/// <b>Three states, not two</b>, and for the same reason the heatmap has three: "follow my system"
/// is a real answer and not the absence of one. A two-state flip cannot express it, and — because
/// the server never learns what <c>prefers-color-scheme</c> said — cannot even label itself for a
/// reader who has never chosen.
/// </para>
/// <para>
/// Every assertion here is about a document served with no script running. The site loads no
/// <c>blazor.web.js</c> (see <c>App.razor</c>), and a preference stored in <c>localStorage</c> would
/// have been dead for exactly the readers the no-script guarantee is for.
/// </para>
/// </remarks>
public class ReaderThemeTests
{
    /// <summary>
    /// No cookie, no attribute — so the stylesheet's media query decides, which is what makes
    /// "dark by default, light if your system says light" still true for a first-time reader.
    /// </summary>
    /// <remarks>
    /// Pinning <c>data-theme="dark"</c> here would serve dark to a light-preferring reader and call
    /// it a default. <c>App.razor</c> says exactly that, and this is the test that keeps it said.
    /// </remarks>
    [Test]
    public async Task AReaderWhoHasNotChosenIsNotPinnedToATheme()
    {
        var document = await DocumentAsync(choice: null);

        await Assert.That(document).Contains("<html lang=\"en\">");
        await Assert.That(document).DoesNotContain("data-theme");
        await Assert.That(Head.Meta(document, "color-scheme")).IsEqualTo("dark light");
    }

    [Test]
    [Arguments("light")]
    [Arguments("dark")]
    public async Task TheChoiceInTheCookieIsOnTheDocument(string choice)
    {
        var document = await DocumentAsync(choice);

        await Assert.That(document).Contains($"data-theme=\"{choice}\"");
    }

    /// <summary>
    /// A pinned theme takes the browser chrome with it.
    /// </summary>
    /// <remarks>
    /// The head carries a <c>theme-color</c> for each system preference, so that mobile Safari is
    /// never handed one theme as the truth. A reader who has overridden the system is exactly the
    /// case that pair gets wrong: they asked for light, their phone still says dark, and the bar
    /// above a light page is painted #0f1113. When we know the answer we state it once.
    /// </remarks>
    [Test]
    public async Task APinnedThemePaintsTheBrowserChromeToMatchRatherThanTheSystem()
    {
        var document = await DocumentAsync("light");

        await Assert.That(Head.Meta(document, "color-scheme")).IsEqualTo("light");
        await Assert.That(Head.Meta(document, "theme-color")).IsEqualTo("#f7f8f9");
        await Assert.That(document).DoesNotContain("prefers-color-scheme");
    }

    /// <summary>A value we did not write is not a preference, and is not honoured as one.</summary>
    [Test]
    public async Task AChoiceWeDoNotRecogniseIsNoChoiceAtAll()
    {
        var document = await DocumentAsync("chartreuse");

        await Assert.That(document).DoesNotContain("data-theme");
        await Assert.That(Head.Meta(document, "color-scheme")).IsEqualTo("dark light");
    }

    [Test]
    [Arguments("light")]
    [Arguments("dark")]
    public async Task ChoosingTakesTheReaderBackToThePageTheyChoseFrom(string choice)
    {
        await using var site = await SiteHost.StartAsync();

        var response = await PostAsync(site, choice, returnTo: "/games?sort=players");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.SeeOther);
        await Assert.That(response.Headers.Location?.OriginalString).IsEqualTo("/games?sort=players");
        await Assert.That(SetCookie(response)).Contains($"{ReaderTheme.CookieName}={choice}");
    }

    /// <summary>
    /// "auto" clears the cookie rather than writing the system's current answer into it.
    /// </summary>
    /// <remarks>
    /// The two are indistinguishable on the day they are chosen and different for ever afterwards: a
    /// reader who asked to follow their system and was given a frozen copy of it stops following it
    /// the first evening their desktop switches. And the server cannot write the right value anyway —
    /// it never sees <c>prefers-color-scheme</c>.
    /// </remarks>
    [Test]
    public async Task ChoosingAutoClearsTheCookieRatherThanFreezingTodaysSystemAnswer()
    {
        await using var site = await SiteHost.StartAsync();

        var response = await PostAsync(site, "auto", returnTo: "/");
        var cookie = SetCookie(response);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.SeeOther);
        await Assert.That(cookie).Contains(ReaderTheme.CookieName);
        await Assert.That(cookie).Contains("expires=Thu, 01 Jan 1970");
    }

    /// <summary>
    /// The return address is a path on this site or it is the front page.
    /// </summary>
    /// <remarks>
    /// It arrives in a form field, so it is whatever the poster typed. An open redirect on a
    /// preference endpoint is still an open redirect — and <c>//elsewhere.example</c> is the case a
    /// <c>StartsWith('/')</c> check written in a hurry lets straight through, because a
    /// protocol-relative URL is a different host wearing a path's clothes.
    /// </remarks>
    [Test]
    [Arguments("https://elsewhere.example/games")]
    [Arguments("//elsewhere.example/games")]
    [Arguments("/\\elsewhere.example")]
    [Arguments("games")]
    [Arguments("")]
    public async Task AReturnAddressWeDidNotWriteIsRefused(string returnTo)
    {
        await using var site = await SiteHost.StartAsync();

        var response = await PostAsync(site, "dark", returnTo);

        await Assert.That(response.Headers.Location?.OriginalString).IsEqualTo("/");
    }

    /// <summary>
    /// The control posts. It is never a link.
    /// </summary>
    /// <remarks>
    /// A GET that changes state is a state change any prefetcher, link checker or crawler can make
    /// on the reader's behalf — and this one appears in the header of every page on the site, which
    /// is precisely where a prefetcher looks. The cost is a form; the alternative is a reader whose
    /// theme flips because their browser was being helpful.
    /// </remarks>
    [Test]
    public async Task TheControlPostsRatherThanLinking()
    {
        var header = await HeaderAsync(choice: null);

        await Assert.That(header).Contains($"action=\"{ReaderTheme.Path}\"");
        await Assert.That(header).Contains("method=\"post\"");
        await Assert.That(header).DoesNotContain($"href=\"{ReaderTheme.Path}");
    }

    /// <summary>All three are offered, and the one in force says so in the markup.</summary>
    [Test]
    [Arguments("light")]
    [Arguments("dark")]
    public async Task TheHeaderOffersAllThreeAndMarksTheOneInForce(string choice)
    {
        var header = await HeaderAsync(choice);
        var other = choice == "light" ? "dark" : "light";

        await Assert.That(Render.Words(header)).Contains("auto");
        await Assert.That(header).Contains($"value=\"{choice}\" aria-pressed=\"true\"");
        await Assert.That(header).Contains($"value=\"{other}\" aria-pressed=\"false\"");
        await Assert.That(header).Contains("value=\"auto\" aria-pressed=\"false\"");
    }

    /// <summary>With no cookie it is "auto" that is marked, not whichever theme we guess they see.</summary>
    [Test]
    public async Task ARenderWithNoChoiceMarksAuto()
    {
        var header = await HeaderAsync(choice: null);

        await Assert.That(header).Contains("value=\"auto\" aria-pressed=\"true\"");
        await Assert.That(header).Contains("value=\"light\" aria-pressed=\"false\"");
        await Assert.That(header).Contains("value=\"dark\" aria-pressed=\"false\"");
    }

    /// <summary>
    /// The form carries the page it was rendered on, so choosing does not lose the reader's place.
    /// </summary>
    /// <remarks>
    /// The query string travels with it. A reader who has narrowed the listing to three facets and
    /// then changes the theme has not asked to start the listing again.
    /// </remarks>
    [Test]
    public async Task TheControlCarriesThePageItWasRenderedOnQueryAndAll()
    {
        var header = await HeaderAsync(choice: null, path: "/games", query: "?codebase=pennmush");

        await Assert.That(header).Contains("value=\"/games?codebase=pennmush\"");
    }

    /// <summary>
    /// A page whose address needs escaping comes back escaped, and so survives the round trip.
    /// </summary>
    /// <remarks>
    /// <c>Request.Path</c> is decoded. Rendering it raw puts a character in the form field that
    /// <see cref="ReaderTheme.Back"/> refuses when it is posted back — so the one reader this
    /// control loses would be the one on a page with an escape in its URL, dropped on the front page
    /// for the crime of preferring light.
    /// </remarks>
    [Test]
    public async Task APageWhoseAddressNeedsEscapingComesBackEscaped()
    {
        var header = await HeaderAsync(choice: null, path: "/g/café");

        await Assert.That(header).Contains("value=\"/g/caf%C3%A9\"");
        await Assert.That(ReaderTheme.Back("/g/caf%C3%A9")).IsEqualTo("/g/caf%C3%A9");
    }

    private static async Task<string> DocumentAsync(string? choice)
    {
        await using var site = await SiteHost.StartAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        if (choice is not null)
        {
            request.Headers.Add("Cookie", $"{ReaderTheme.CookieName}={choice}");
        }

        var response = await site.Client.SendAsync(request);

        return await response.Content.ReadAsStringAsync();
    }

    private static Task<HttpResponseMessage> PostAsync(SiteHost site, string choice, string returnTo) =>
        site.Client.PostAsync(
            ReaderTheme.Path,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                [ReaderTheme.Field] = choice,
                [ReaderTheme.ReturnField] = returnTo,
            }));

    private static string SetCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? string.Join('\n', values)
            : string.Empty;

    private static Task<string> HeaderAsync(string? choice, string path = "/", string query = "") =>
        Render.ComponentAsync<MainLayout>(new Dictionary<string, object?>(), services =>
        {
            services.AddSingleton(new CatalogueSource(IsMeasured: true));
            services.AddCascadingValue(_ => Context(choice, path, query));
        });

    private static HttpContext Context(string? choice, string path, string query)
    {
        var context = new DefaultHttpContext();

        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        context.User = new ClaimsPrincipal(new ClaimsIdentity());

        if (choice is not null)
        {
            context.Request.Headers.Cookie = $"{ReaderTheme.CookieName}={choice}";
        }

        return context;
    }
}
