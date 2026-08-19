using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Web.Accounts;
using MUI.Web.Components.Pages;
using MUI.Web.Fixtures;
using MUI.Web.Localization;

namespace MUI.Web.Tests;

/// <summary>
/// Sign-in hands the operator back to the page that sent them to it (spec §8.2).
/// </summary>
/// <remarks>
/// An operator reaches sign-in from somewhere — nearly always a claim page, since claiming is what
/// an account here is for — and landing on the dashboard instead left them to find their own way
/// back. The way back a browser offers is Back, which is the navigation that served the signed-out
/// render again (<see cref="PageCacheHeaderTests"/>): between them they made a loop somebody could
/// not get out of on the live site.
/// The return address arrives in a querystring, so it is attacker-supplied by construction, and
/// half of what is asserted here is what it is not allowed to be.
/// </remarks>
public class SignInReturnTests
{
    [Test]
    [Arguments("/g/ashen-court/claim")]
    [Arguments("/games?seen=day&genre=Fantasy")]
    [Arguments("/")]
    public async Task APageOfThisSiteIsSomewhereToComeBackTo(string path)
    {
        await Assert.That(ReturnPath.Of(path)).IsEqualTo(path);
    }

    /// <summary>
    /// Everything that is not a path on this site, including the two spellings of "another host"
    /// that do not look like one.
    /// </summary>
    /// <remarks>
    /// <c>//evil.example</c> is protocol-relative and several browsers read <c>/\evil.example</c>
    /// the same way; a newline is a response-splitting attempt rather than an address. An open
    /// redirect hung off a sign-in page is worth more than the account it would be attached to.
    /// </remarks>
    [Test]
    [Arguments("https://evil.example/take-a-passkey")]
    [Arguments("//evil.example")]
    [Arguments("/\\evil.example")]
    [Arguments("\\\\evil.example")]
    [Arguments("javascript:alert(1)")]
    [Arguments("/games\nLocation: https://evil.example")]
    [Arguments("")]
    [Arguments(null)]
    public async Task AnythingElseIsNotSomewhereToComeBackTo(string? candidate)
    {
        await Assert.That(ReturnPath.Of(candidate)).IsNull();
    }

    /// <summary>A ceiling, so nothing unbounded is echoed back into a page.</summary>
    [Test]
    public async Task AnAbsurdlyLongAddressIsRefused()
    {
        await Assert.That(ReturnPath.Of("/g/" + new string('x', 600))).IsNull();
    }

    [Test]
    public async Task TheSignInLinkCarriesThePageItWasOfferedOn()
    {
        var link = ReturnPath.SignInFrom(At("/g/ashen-court/claim"));

        await Assert.That(link)
            .IsEqualTo($"{Passkeys.SignInPath}?return=%2Fg%2Fashen-court%2Fclaim");
    }

    /// <summary>The question the page was answering rides along, or a narrowed listing comes back whole.</summary>
    [Test]
    public async Task TheQuestionThePageWasAnsweringRidesAlong()
    {
        var link = ReturnPath.SignInFrom(At("/games", "?seen=day"));

        await Assert.That(link).IsEqualTo($"{Passkeys.SignInPath}?return=%2Fgames%3Fseen%3Dday");
    }

    /// <summary>
    /// Signing in is not a language change: the link is localized, and the address inside it is not.
    /// </summary>
    /// <remarks>The locale is in <c>PathBase</c> by the time a page reads its own path, and goes back on through <see cref="LocaleRouting"/> — spelling it into the return value too would put it there twice.</remarks>
    [Test]
    public async Task AGermanReaderIsOfferedGermanSignInAndComesBackToGerman()
    {
        var link = ReturnPath.SignInFrom(At("/g/ashen-court/claim", tag: "de"));

        await Assert.That(link)
            .IsEqualTo($"/de{Passkeys.SignInPath}?return=%2Fg%2Fashen-court%2Fclaim");
    }

    /// <summary>Coming back to sign-in having signed in is a loop; the dashboard is where a returnless sign-in already goes.</summary>
    [Test]
    [Arguments("/account")]
    [Arguments("/account/sign-in")]
    public async Task TheAccountPagesAreNotSomewhereToBeSentBackTo(string path)
    {
        await Assert.That(ReturnPath.SignInFrom(At(path))).IsEqualTo(Passkeys.SignInPath);
    }

    // ── what the pages actually render ────────────────────────────────────────

    /// <summary>The reported flow: a claim page read by somebody with no account.</summary>
    [Test]
    public async Task TheClaimPageOffersSignInThatComesBackToTheClaimPage()
    {
        var page = await SignedOutClaimPageAsync();

        await Assert.That(page).Contains("/account/sign-in?return=%2Fg%2Fashen-court%2Fclaim");
    }

    [Test]
    public async Task TheSignInPageActsOnAnAddressOnThisSite()
    {
        var page = await SignInAsync("/g/ashen-court/claim");

        await Assert.That(page).Contains("data-passkey-return=\"/g/ashen-court/claim\"");
    }

    /// <summary>The other half: a hostile address reaches the markup as nothing at all.</summary>
    [Test]
    public async Task TheSignInPageActsOnNothingElse()
    {
        var page = await SignInAsync("https://evil.example/take-a-passkey");

        await Assert.That(page).DoesNotContain("evil.example");
        await Assert.That(page).Contains("data-passkey-return=\"\"");
    }

    /// <summary>A request for one path, in one locale, and nothing else a page might read.</summary>
    private static HttpContext At(string path, string query = "", string tag = Locales.SourceTag)
    {
        var context = new DefaultHttpContext();

        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        context.Items[LocaleRouting.ItemKey] =
            new LocaleContext(Locales.Find(tag)!, FromPath: tag != Locales.SourceTag);

        return context;
    }

    /// <summary>
    /// The sign-in page, reached by a link carrying <paramref name="candidate"/>.
    /// </summary>
    /// <remarks>Composed with identity behind it, the condition the page branches on — over the demo fixture it renders "nothing to sign in to" and has no forms to carry anything.</remarks>
    private static Task<string> SignInAsync(string candidate) =>
        Render.ComponentAsync<SignIn>([], services =>
        {
            services.AddLogging();
            services.AddSingleton(new MUI.Web.Data.CatalogueSource(IsMeasured: true));
            services.AddHttpContextAccessor();
            services.AddAuthentication();
            services.AddSingleton<IUserStore<MuiUser>>(new Accounts([], FixtureGameQueries.Now));
            services.AddIdentityCore<MuiUser>().AddSignInManager();

            services.AddCascadingValue(_ => At(
                Passkeys.SignInPath,
                $"?{ReturnPath.Parameter}={Uri.EscapeDataString(candidate)}"));
        });

    /// <summary>
    /// A real game's claim page, read by somebody with no account — the state that offers sign-in.
    /// </summary>
    /// <remarks>
    /// Rendered at the page's own address, because the link is built from the request rather than
    /// from the route parameter — a page that rebuilt its own address would be a second spelling of
    /// every route. No <c>UserManager</c> is registered, which is how a reader with no account
    /// reaches this branch: the page asks for one softly and stops at a null user.
    /// </remarks>
    private static Task<string> SignedOutClaimPageAsync() =>
        Render.PageAsync<MUI.Web.Components.Pages.Claim>(
            new Dictionary<string, object?> { ["Slug"] = Slug },
            query: string.Empty,
            measured: true,
            games: [new GameRecord(
                GameId, Slug, "Ashen Court", null, LifecycleState.Active, false, FixtureGameQueries.Now)],
            claimService: new ClaimService(new NullClaimStore(), new NullGameStore(), TimeProvider.System),
            http: At($"/g/{Slug}/claim"));

    private const string Slug = "ashen-court";

    private static readonly Guid GameId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000042");
}
