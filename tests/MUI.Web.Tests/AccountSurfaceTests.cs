using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Web;
using MUI.Web.Accounts;
using MUI.Web.Components;
using MUI.Web.Components.Pages;
using MUI.Web.Fixtures;
using MUI.Web.Localization;

namespace MUI.Web.Tests;

/// <summary>
/// The owner dashboard, rendered in each state an operator can actually be in.
/// </summary>
/// <remarks>
/// <para>
/// Five features' worth of markup landed in this one page across a merge chain — the owner panel,
/// the MSSP scorecard link, the badge snippet, the co-owner line, the history block, the resign
/// form and the status banner — and nothing rendered it. A component that only ever compiled is a
/// component nobody has looked at.
/// </para>
/// <para>
/// <b>On authentication.</b> The signed-out and no-database states go through <see cref="SiteHost"/>
/// end to end, because nothing stands between a visitor and those. The signed-in states cannot:
/// §8.2 makes passkeys the only way in, so a loopback host has no way to produce an authenticated
/// session without an authenticator. They are rendered at component level with an
/// <see cref="HttpContext"/> cascaded in, which is what the framework would have supplied — the
/// page's own guard on <c>Identity.IsAuthenticated</c> still runs, and the claims it filters are
/// real records in a real <see cref="ClaimService"/>. What is stood in for is the user <em>store</em>
/// and the credential ceremony, neither of which is the authorisation under test.
/// </para>
/// </remarks>
public class AccountSurfaceTests
{
    private static readonly DateTimeOffset Now = FixtureGameQueries.Now;

    /// <summary>A game from the fixture, so the page's own lookup by id has something to find.</summary>
    private static readonly Guid Ashen = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000007");

    private static readonly Guid Mush = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    // ── the states an operator is in ──────────────────────────────────────────

    /// <summary>A site with no database says so, rather than offering half a claim flow.</summary>
    /// <remarks>
    /// Through the real host: this is the state a reader of the demo site is in, and it is reached
    /// by the ordinary pipeline rather than by a component rendered out of context.
    /// </remarks>
    [Test]
    public async Task WithNoDatabaseThePageSaysAccountsNeedOne()
    {
        await using var host = await SiteHost.StartAsync();

        var body = Render.Words(await host.Client.GetStringAsync("/account"));

        // The fact, through the bundle, rather than a pasted sentence: the claim is that this page
        // says the one thing and offers neither the way in nor a write surface, and it goes on
        // being that claim when somebody rewords the copy.
        await Assert.That(body).Contains(En("account.noDatabase"));
        await Assert.That(body).DoesNotContain(En("account.signInButton"));
        await Assert.That(body).DoesNotContain(En("account.resign.summary"));
    }

    /// <summary>One message in the source language, as it reads once rendered.</summary>
    private static string En(string id) => Render.Words(Messages.For(Locales.SourceTag, id));

    /// <summary>
    /// A placed sentence in the source language, with its slots filled as the page fills them.
    /// </summary>
    /// <remarks>
    /// <see cref="Sentence.Place"/> hands back runs and markers so the markup can choose an element
    /// per slot; a test wants the sentence a reader ends up with. Reassembling it here means these
    /// assertions read the same bundle the page does — the alternative is a pasted English string,
    /// which is exactly what this pass exists to remove.
    /// </remarks>
    private static string EnPlaced(
        string id,
        IReadOnlyDictionary<string, string> fills,
        params (string Key, object? Value)[] args) =>
        Render.Words(string.Concat(Sentence
            .Place(Locales.SourceTag, id, [.. fills.Keys], args)
            .Select(part => part.Slot is null ? part.Text : fills[part.Slot])));

    /// <summary>
    /// Every word of the sign-in page comes out of the message bundle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both branches, because they are two different pages: over the demo fixture it is one sentence
    /// saying there is nothing to sign in to, and behind a database it is the whole passkey
    /// explanation — and the second was never rendered by any test at all, so it could have drifted
    /// into English without anything noticing.
    /// </para>
    /// <para>
    /// The pseudolocale is the instrument: it accents and brackets anything that reached a reader
    /// through <see cref="Messages"/>, so an English sentence surviving here is one typed into the
    /// markup. Asserted against the English render rather than against pasted strings, so this keeps
    /// holding when the copy is edited.
    /// </para>
    /// </remarks>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task EveryWordOfSigningInComesFromTheBundle(bool withDatabase)
    {
        var english = Render.Words(await SignInAsync(Locales.SourceTag, withDatabase));
        var pseudo = Render.Words(await SignInAsync("qps-ploc", withDatabase));

        await Assert.That(pseudo).Contains("⟦");

        // Which page this is, asserted rather than assumed: a harness that failed to register
        // identity would render the one-sentence branch twice and the loop below would pass on a
        // page nobody had looked at.
        await Assert.That(english.Contains(
                Messages.For(Locales.SourceTag, "account.store.heading"), StringComparison.Ordinal))
            .IsEqualTo(withDatabase);

        await Assert.That(english.Contains(
                Messages.For(Locales.SourceTag, "account.signIn.noDatabase"), StringComparison.Ordinal))
            .IsEqualTo(!withDatabase);

        // Every sentence of the English page, absent from the same page in another language.
        //
        // Argument-free ids only, and that costs this sweep nothing: the sign-in page says no
        // message that takes one. The `account.` prefix now also covers the owner dashboard, whose
        // sentences name a game, a date or a count — and a pattern cannot be rendered at all
        // without its arguments, so including them here would throw rather than assert.
        foreach (var id in Sayable("account."))
        {
            var sentence = Render.Words(Messages.For(Locales.SourceTag, id));

            if (!english.Contains(sentence, StringComparison.Ordinal))
            {
                continue;
            }

            await Assert.That(pseudo)
                .DoesNotContain(sentence)
                .Because($"{id} is rendered as English whatever language the page was asked for");
        }
    }

    /// <summary>
    /// The sign-in page, in one locale, with or without a database behind it.
    /// </summary>
    /// <remarks>
    /// The locale arrives the way the middleware leaves it — in <c>HttpContext.Items</c> — because
    /// that is what the page reads. Identity is registered only for the second case, which is
    /// exactly the condition the page itself branches on.
    /// </remarks>
    private static Task<string> SignInAsync(string tag, bool withDatabase) =>
        Render.ComponentAsync<MUI.Web.Components.Pages.SignIn>([], services =>
        {
            services.AddLogging();
            services.AddSingleton(new MUI.Web.Data.CatalogueSource(IsMeasured: withDatabase));

            var context = new DefaultHttpContext();
            context.Items[LocaleRouting.ItemKey] =
                new LocaleContext(Locales.Find(tag)!, FromPath: tag != Locales.SourceTag);
            services.AddCascadingValue(_ => context);

            if (!withDatabase)
            {
                return;
            }

            services.AddHttpContextAccessor();
            services.AddAuthentication();
            services.AddSingleton<IUserStore<MuiUser>>(new Accounts([], FixtureGameQueries.Now));
            services.AddIdentityCore<MuiUser>().AddSignInManager();
        });

    /// <summary>A signed-out visitor is offered the way in and nothing else.</summary>
    [Test]
    public async Task ASignedOutVisitorIsOfferedSignInAndNoWriteSurface()
    {
        var markup = await World.New().Anonymous().RenderAsync();

        await Assert.That(markup).Contains("/account/sign-in");
        await Assert.That(markup).DoesNotContain("<form");
        await Assert.That(Render.Words(markup)).DoesNotContain(En("account.claimed.heading"));
    }

    /// <summary>An account with nothing claimed is told where to start.</summary>
    [Test]
    public async Task AnAccountWithNoClaimsIsPointedAtTheListing()
    {
        var markup = await World.New().SignedIn().RenderAsync();
        var words = Render.Words(markup);

        // The whole sentence, reassembled from the bundle including the two runs the markup turns
        // into a link and an emphasis — so this holds on the words a reader gets rather than on
        // the fragments the markup happens to be split into. Read off the visible text, because a
        // placed sentence has elements inside it.
        await Assert.That(Render.Text(markup)).Contains(EnPlaced(
            "account.empty.body",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["listing"] = En("account.empty.listing"),
                ["claimControl"] = En("account.empty.claimControl"),
            }));

        await Assert.That(words).Contains("/games");
        await Assert.That(words).DoesNotContain(En("account.pending.heading"));
        await Assert.That(markup).DoesNotContain("resign");
    }

    /// <summary>A pending claim is a link back to the page with the token on it.</summary>
    [Test]
    public async Task APendingClaimLinksToItsToken()
    {
        var markup = await World.New().SignedIn().Pending(Mush).RenderAsync();
        var words = Render.Words(markup);

        await Assert.That(words).Contains(En("account.pending.heading"));
        await Assert.That(markup).Contains("/g/m-u-s-h/claim");

        // Pending is not owning: none of the owner surfaces appear for it.
        await Assert.That(words).DoesNotContain(En("account.claimed.heading"));
        await Assert.That(markup).DoesNotContain("badge.svg");
    }

    /// <summary>
    /// A verified claim brings every surface a claim grants, in one block.
    /// </summary>
    /// <remarks>
    /// The whole point of the merge chain, asserted in one place: §8.5's enrichment fields, its MSSP
    /// scorecard, its owner-published badge, the audit log and §8.4's explicit resignation. Each
    /// arrived on a different branch and none of them was ever rendered beside the others.
    /// </remarks>
    [Test]
    public async Task AVerifiedClaimShowsEverythingAClaimGrants()
    {
        var markup = await World.New().SignedIn().Verified(Ashen).RenderAsync();
        var words = Render.Words(markup);

        await Assert.That(words).Contains(En("account.claimed.heading"));
        await Assert.That(markup).Contains("/g/ashen-court");

        // §8.5's enrichment, through OwnerPanel.
        await Assert.That(markup).Contains($"{OwnerWrites.FieldPrefix}FANDOM");
        await Assert.That(markup).Contains($"/account/games/{Ashen}/enrichment");

        // §11's suppression.
        await Assert.That(markup).Contains($"/account/games/{Ashen}/connect-screen");

        // §8.5's scorecard, its badge, its audit log, and §8.4's resignation.
        await Assert.That(markup).Contains("/g/ashen-court/mssp");
        await Assert.That(markup).Contains("/g/ashen-court/badge.svg");
        await Assert.That(markup).Contains("/g/ashen-court/badge.json");
        await Assert.That(words).Contains(En("account.history.summary"));
        await Assert.That(markup).Contains("/resign");
    }

    /// <summary>
    /// The badge snippet is the exact line to paste, and it names this game.
    /// </summary>
    /// <remarks>
    /// It is markup inside markup, so the thing to check is that it survived escaping as something a
    /// person can copy rather than as rendered HTML.
    /// </remarks>
    [Test]
    public async Task TheBadgeSnippetIsCopyableRatherThanRendered()
    {
        var markup = await World.New().SignedIn().Verified(Ashen).RenderAsync();

        // Entity-encoded, quotes included. Read off the rendered bytes rather than off an
        // assertion message: a failure report that HTML-decodes what it shows makes a correctly
        // escaped snippet look like markup that got away.
        await Assert.That(markup).Contains("&lt;img src=&quot;/g/ashen-court/badge.svg&quot;");
        await Assert.That(markup).DoesNotContain("<img src=\"/g/ashen-court/badge.svg\"");
    }

    /// <summary>Several games each get their own block, and none is opened by default.</summary>
    /// <remarks>
    /// An operator holds a handful of games, and the page collapses them for that reason — a page of
    /// five open forms is a page nobody reads. One game is the exception and stays open.
    /// </remarks>
    [Test]
    public async Task SeveralGamesAreEachTheirOwnCollapsedBlock()
    {
        var one = await World.New().SignedIn().Verified(Ashen).RenderAsync();
        var two = await World.New().SignedIn().Verified(Ashen).Verified(Mush).RenderAsync();

        await Assert.That(one).Contains("<details class=\"claim\" open");

        await Assert.That(two).Contains("/g/ashen-court");
        await Assert.That(two).Contains("/g/m-u-s-h");
        await Assert.That(Matches(two, "<details class=\"claim\"")).IsEqualTo(2);
        await Assert.That(two).DoesNotContain("<details class=\"claim\" open");
    }

    /// <summary>§8.5 — a game may have several owners, and each of them should know.</summary>
    [Test]
    public async Task ACoOwnedGameNamesTheOtherOwners()
    {
        var markup = await World.New().SignedIn().Verified(Ashen).CoOwnedBy("thistle").RenderAsync();
        var words = Render.Words(markup);

        // One co-owner, so the message's `one` branch — which is a different sentence from the
        // plural in English and in most languages, and naming the branch is the point.
        await Assert.That(words).Contains(
            Render.Words(Messages.Say(
                Locales.SourceTag, "account.claim.coOwners", ("count", 1), ("names", "thistle"))));

        // The resign copy is the one that differs when somebody else holds the game too.
        await Assert.That(Render.Text(markup)).Contains(EnPlaced(
            "account.resign.confirm",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["word"] = OwnershipWrites.ResignConfirmation,
            }));
    }

    /// <summary>A sole owner sees no co-owner line at all.</summary>
    [Test]
    public async Task ASoleOwnerIsNotToldAboutOwnersWhoDoNotExist()
    {
        var words = Render.Words(await World.New().SignedIn().Verified(Ashen).RenderAsync());

        // The whole rendered line for one co-owner, which cannot appear when there are none.
        await Assert.That(words).DoesNotContain(
            Render.Words(Messages.Say(
                Locales.SourceTag, "account.claim.coOwners", ("count", 1), ("names", "thistle"))));
    }

    // ── the status banner ─────────────────────────────────────────────────────

    /// <summary>
    /// Each outcome reports itself, and an else-if chain reports exactly one.
    /// </summary>
    /// <remarks>
    /// This page has already told an operator the wrong thing once: hiding a connect screen came
    /// back saying the page "now shows it as owner-declared", which is the enrichment sentence. The
    /// chain grew a third arm afterwards, and a chain is the shape that silently reports the wrong
    /// branch — so every arm is driven here, by the querystring the redirect actually carries.
    /// </remarks>
    [Test]
    [Arguments("?saved={game}&did=fields", "account.saved.fields")]
    [Arguments("?saved={game}&did=screen-hidden", "account.saved.screenHidden")]
    [Arguments("?saved={game}&did=screen-shown", "account.saved.screenShown")]
    [Arguments("?saved={game}&did=crawl-stopped", "account.saved.crawlStopped")]
    [Arguments("?saved={game}&did=crawl-resumed", "account.saved.crawlResumed")]
    [Arguments("?saved={game}&did=unlisted", "account.saved.unlisted")]
    [Arguments("?saved={game}&did=relisted", "account.saved.relisted")]
    [Arguments("?resigned=1", "account.resigned.lead")]
    [Arguments("?refused=CODEBASE&because=NotEnrichable", "account.refused.lead")]
    public async Task EveryOutcomeReportsTheActionThatHappened(string query, string expected)
    {
        var words = Render.Words(await World.New()
            .SignedIn()
            .Verified(Ashen)
            .RenderAsync(query.Replace("{game}", Ashen.ToString(), StringComparison.Ordinal)));

        // The id rather than a pasted fragment, so a reworded banner still has to be the banner for
        // the action that happened. The four §11 and listing arms were added to OwnerWrites and
        // never to this table, which is the exact omission the remark above describes — the switch
        // had already reported the wrong branch once for the same reason.
        //
        // Both arguments are supplied to every arm; a pattern that names neither ignores them.
        await Assert.That(words).Contains(Render.Words(Messages.Say(
            Locales.SourceTag, expected, ("game", AshenName), ("field", "CODEBASE"))));
    }

    /// <summary>The fixture game's own name, which the banner quotes and never translates.</summary>
    private const string AshenName = "Ashen Court";

    /// <summary>
    /// Two outcomes at once is one banner, and it is the one the chain says it is.
    /// </summary>
    /// <remarks>
    /// A redirect carries exactly one outcome, so this combination cannot arise from the endpoints —
    /// which is precisely why it is worth pinning. If somebody later reorders the arms, a page that
    /// silently reported the other action would look identical to one that worked.
    /// </remarks>
    [Test]
    public async Task ACraftedUrlCarryingTwoOutcomesStillReportsOnlyOne()
    {
        var words = Render.Words(await World.New()
            .SignedIn()
            .Verified(Ashen)
            .RenderAsync($"?resigned=1&saved={Ashen}&did=fields&refused=CODEBASE"));

        await Assert.That(words).Contains(En("account.resigned.lead"));
        await Assert.That(words).DoesNotContain(Render.Words(
            Messages.Say(Locales.SourceTag, "account.saved.fields", ("game", AshenName))));
        await Assert.That(words).DoesNotContain(Render.Words(
            Messages.Say(Locales.SourceTag, "account.refused.lead", ("field", "CODEBASE"))));
    }

    // ── the language the dashboard is answered in ─────────────────────────────

    /// <summary>
    /// Every word of the dashboard comes out of the bundle, on the fullest page it has.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pseudolocale is the instrument, as it is for the sign-in page: it accents and brackets
    /// anything that reached a reader through <see cref="Messages"/>, so an English sentence
    /// surviving here is one somebody typed into the markup. Driven on a verified claim with a
    /// co-owner and a banner, because that is the state that renders the owner panel, the badge
    /// block, the audit log and the resignation form — most of this page's words are in there.
    /// </para>
    /// <para>
    /// What stays English is named rather than tolerated: a game's name, an operator's chosen
    /// display name, a field name out of the registry, the confirmation word the form compares
    /// against, the badge's own text and an ISO stamp. Every one of those is machine voice, and
    /// translating any of them would break the thing it names.
    /// </para>
    /// </remarks>
    [Test]
    public async Task EveryWordOfTheDashboardComesFromTheBundle()
    {
        var english = Render.Text(await Full(Locales.SourceTag));
        var pseudo = Render.Text(await Full("qps-ploc"));

        await Assert.That(pseudo).Contains("⟦");

        // The page really is the full one, rather than a guard branch rendered twice.
        await Assert.That(english).Contains(En("account.claimed.heading"));
        await Assert.That(english).Contains(En("owner.crawl.heading"));

        foreach (var id in Sayable("account.", "owner."))
        {
            var sentence = Render.Words(Messages.For(Locales.SourceTag, id));

            if (!english.Contains(sentence, StringComparison.Ordinal))
            {
                continue;
            }

            await Assert.That(pseudo)
                .DoesNotContain(sentence)
                .Because($"{id} is rendered as English whatever language the page was asked for");
        }
    }

    /// <summary>
    /// A German request gets German wherever German exists, and the machine voice survives it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Gated on <see cref="Messages.HasOwn"/> rather than on a list of ids, and that is the
    /// point.</b> This page's own ids are new and the four satellites are translated in one round
    /// afterwards, so today most of them fall back to English — which is the designed behaviour and
    /// not a failure. Naming ids here would either freeze that state in or fail the moment the
    /// translations land. Asking the bundle instead means the assertion is "German where there is
    /// German", which is true now and stays true as each id is translated.
    /// </para>
    /// <para>
    /// The counter proves it is not vacuous: the owner panel already renders the provenance word,
    /// which is translated, so this has real German to find today.
    /// </para>
    /// </remarks>
    [Test]
    public async Task AGermanRequestGetsGermanOnTheDashboard()
    {
        var english = Render.Text(await Full(Locales.SourceTag));
        var german = Render.Text(await Full("de"));

        var translated = 0;

        // The provenance family joins by the one id these pages render, not by prefix: the others
        // carry the bare word "measured", which occurs inside half the sentences on this page and
        // would match as a substring rather than as the chip the panel promises.
        foreach (var id in Sayable("account.", "owner.").Append("provenance.game.ownerDeclared"))
        {
            var en = Render.Words(Messages.For(Locales.SourceTag, id));
            var de = Render.Words(Messages.For("de", id));

            // An id that fell back has nothing to assert, and a word German spells the same way
            // proves nothing either way.
            if (!Messages.HasOwn("de", id)
                || string.Equals(en, de, StringComparison.Ordinal)
                || !english.Contains(en, StringComparison.Ordinal))
            {
                continue;
            }

            await Assert.That(german).Contains(de).Because($"{id} is not answered in German");

            translated++;
        }

        await Assert.That(translated)
            .IsGreaterThan(0)
            .Because("no id on this page has German yet, so this test asserted nothing");

        // The page is answered in German rather than merely served under a German tag.
        await Assert.That(german).IsNotEqualTo(english);

        // A fallback shows the English and never the id. That is the one failure mode a reader
        // cannot recover from — English inside a German sentence tells them something true, and
        // `account.title` on the page tells them the site is broken.
        foreach (var id in Sayable("account.", "owner.").Where(i => !Messages.HasOwn("de", i)))
        {
            await Assert.That(german).DoesNotContain(id).Because($"{id} reached a reader as its id");
        }

        // Machine voice is untouched: the game's name and the confirmation word are not translated.
        await Assert.That(german).Contains(AshenName);
        await Assert.That(german).Contains(OwnershipWrites.ResignConfirmation);
    }

    /// <summary>Ids under these prefixes that can be rendered without arguments.</summary>
    /// <remarks>
    /// A pattern cannot be formatted at all without the arguments it names, so a sweep that walks
    /// whole sentences has to leave those out. It costs little: the parameterised ids on these
    /// pages are the ones carrying a game's name, a date or a count, and each has its own test.
    /// </remarks>
    private static IEnumerable<string> Sayable(params string[] prefixes) =>
        Messages.Ids
            .Where(i => prefixes.Any(p => i.StartsWith(p, StringComparison.Ordinal)))
            .Where(i => !IcuMessage.Compile(Messages.Pattern(Locales.SourceTag, i)!).Arguments().Any());

    /// <summary>The dashboard in one locale, in the state that renders the most of it.</summary>
    private static Task<string> Full(string tag) =>
        World.New()
            .In(tag)
            .SignedIn()
            .Verified(Ashen)
            .CoOwnedBy("thistle")
            .RenderAsync($"?saved={Ashen}&did=fields");

    /// <summary>An enrichment refusal names the field and says which rule refused it.</summary>
    /// <remarks>
    /// The two reasons are different sentences and must not be swapped: one says an answer was too
    /// long, the other says the field is measured and that nobody edits a measurement, us included.
    /// A refusal that gave the second reason for the first cause would be the site claiming a rule
    /// it does not have.
    /// </remarks>
    [Test]
    public async Task ARefusalOverLengthSaysSoRatherThanBlamingTheField()
    {
        var words = Render.Words(await World.New()
            .SignedIn()
            .Verified(Ashen)
            .RenderAsync("?refused=FANDOM&because=TooLong"));

        await Assert.That(words).Contains(Render.Words(
            Messages.Say(Locales.SourceTag, "account.refused.lead", ("field", "FANDOM"))));

        await Assert.That(words).Contains(Render.Words(Messages.Say(
            Locales.SourceTag,
            "account.refused.tooLong",
            ("max", OwnerEnrichment.MaxValueLength.ToString(CultureInfo.InvariantCulture)))));

        await Assert.That(words).DoesNotContain(En("account.refused.measured"));
    }

    // ── the routes the page posts to ──────────────────────────────────────────

    /// <summary>
    /// Every form on this page posts to a route the site actually maps.
    /// </summary>
    /// <remarks>
    /// A form whose action nobody maps fails at the browser, not at build — it is a 404 an operator
    /// meets after typing into a box, and no compiler, no renderer and no unit test would say a word
    /// about it. Five features' worth of forms arrived here across a merge chain, each mapped in a
    /// different file, and this is the only check that they all still line up.
    /// </remarks>
    [Test]
    public async Task EveryFormOnThePagePostsToARouteTheSiteMaps()
    {
        var markup = await World.New().SignedIn().Verified(Ashen).RenderAsync();

        var actions = Regex
            .Matches(markup, "<form[^>]*action=\"([^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(5))
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        await Assert.That(actions).IsNotEmpty();

        var routes = MappedRoutes();

        foreach (var action in actions)
        {
            await Assert.That(routes.Any(route => Fits(route, action)))
                .IsTrue()
                .Because($"the page posts to {action}, which no route matches");
        }
    }

    /// <summary>
    /// The route patterns the deployable maps, with a database configured.
    /// </summary>
    /// <remarks>
    /// Built through <see cref="SiteComposition"/>'s own two calls and never started: the account
    /// routes exist only when a connection string does, and the string here is never dialled.
    /// </remarks>
    private static IReadOnlyList<string> MappedRoutes()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
        });

        builder.Logging.ClearProviders();
        builder.Services.AddMuiSite(builder.Configuration, "Host=127.0.0.1;Port=1;Database=m;Username=m");

        var app = builder.Build();
        app.UseMuiSite("Host=127.0.0.1;Port=1;Database=m;Username=m");

        // Off the builder rather than out of the container: the composite EndpointDataSource in DI
        // is empty until the app starts, so resolving it here would have compared the page against
        // no routes at all and passed for the wrong reason.
        return
        [
            .. ((IEndpointRouteBuilder)app).DataSources
                .SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .Select(e => e.RoutePattern.RawText ?? string.Empty),
        ];
    }

    /// <summary>Whether a mapped pattern would match a concrete path, segment by segment.</summary>
    private static bool Fits(string pattern, string path)
    {
        var patternParts = pattern.Trim('/').Split('/');
        var pathParts = path.Trim('/').Split('/');

        if (patternParts.Length != pathParts.Length)
        {
            return false;
        }

        return patternParts.Zip(pathParts).All(pair =>
            pair.First.StartsWith('{')
            || string.Equals(pair.First, pair.Second, StringComparison.OrdinalIgnoreCase));
    }

    private static int Matches(string haystack, string needle) =>
        haystack.Split(needle).Length - 1;

    /// <summary>
    /// One operator, their claims, and the page rendered as they would see it.
    /// </summary>
    /// <remarks>
    /// The stores are in memory and the services over them are the real ones, so what the page
    /// branches on — verified against pending, one owner against several — is decided by
    /// <see cref="ClaimService"/> rather than by the test.
    /// </remarks>
    private sealed class World
    {
        private readonly MuiUser _user = new() { DisplayName = "corvid-admin", CreatedAt = Now };
        private readonly List<MuiUser> _accounts = [];
        private readonly List<GameClaim> _claims = [];

        private bool _signedIn;
        private string _tag = Locales.SourceTag;

        public static World New() => new();

        /// <summary>
        /// The locale the request was answered in, as the middleware leaves it.
        /// </summary>
        /// <remarks>
        /// In <c>HttpContext.Items</c> rather than on a thread's culture, because that is where the
        /// page reads it from — every surface here is handed its locale rather than inferring one.
        /// </remarks>
        public World In(string tag)
        {
            _tag = tag;
            return this;
        }

        public World SignedIn()
        {
            _signedIn = true;
            _accounts.Add(_user);
            return this;
        }

        public World Anonymous() => this;

        public World Verified(Guid game)
        {
            _claims.Add(Claim(game, _user.Id) with
            {
                ClaimedAt = Now.AddDays(-30),
                BeaconLastSeenAt = Now.AddHours(-2),
                VerifiedVia = ClaimChannel.Mssp,
            });

            return this;
        }

        public World Pending(Guid game)
        {
            _claims.Add(Claim(game, _user.Id));
            return this;
        }

        /// <summary>A second account that has proved control of the game claimed most recently.</summary>
        public World CoOwnedBy(string name)
        {
            var other = new MuiUser { DisplayName = name, CreatedAt = Now };
            _accounts.Add(other);

            _claims.Add(Claim(_claims[^1].GameId, other.Id) with
            {
                ClaimedAt = Now.AddDays(-10),
                VerifiedVia = ClaimChannel.ConnectScreen,
            });

            return this;
        }

        public Task<string> RenderAsync(string query = "") =>
            Render.ComponentAsync<Account>(new Dictionary<string, object?>(), services =>
            {
                var fixture = new FixtureGameQueries();
                var claims = new InMemoryClaims(_claims);
                var fields = new InMemoryFields();
                var games = new InMemoryGames();

                services.AddLogging();
                services.AddSingleton<IGameQueries>(fixture);

                // What the page's preview metadata switches on. These are owner surfaces, so the
                // marker changes nothing they render — but the component asks for it, and a graph
                // that cannot answer is a page that cannot render at all.
                services.AddSingleton(new MUI.Web.Data.CatalogueSource(IsMeasured: true));
                services.AddSingleton<TimeProvider>(new Frozen(Now));
                services.AddSingleton<NavigationManager>(new At(query));

                // What supplies [SupplyParameterFromQuery] outside the endpoint infrastructure.
                // Without it every one of those properties is null and the status banner silently
                // never renders — which is indistinguishable from a banner that decided not to.
                services.AddSupplyValueFromQueryProvider();
                services.AddSingleton<AntiforgeryStateProvider, NoAntiforgery>();

                // Cascaded rather than authenticated through a scheme: this is the object the
                // framework hands a page, and the page's own guard on it is what is under test.
                services.AddCascadingValue(_ => Context(_signedIn ? _user : null));

                services.AddSingleton<IClaimStore>(claims);
                services.AddSingleton<IGameFieldStore>(fields);
                services.AddSingleton(new ClaimService(claims, games, new Frozen(Now)));
                services.AddSingleton(new OwnerEnrichment(
                    claims, fields, new FieldReconciler(fields), FieldRegistry.Instance, new Frozen(Now)));

                services.AddSingleton<IUserStore<MuiUser>>(new Accounts(_accounts, Now));
                services.AddIdentityCore<MuiUser>();
            });

        private static GameClaim Claim(Guid game, Guid user) => new()
        {
            Id = Guid.CreateVersion7(),
            GameId = game,
            UserId = user,
            Token = "muidx-" + Guid.NewGuid().ToString("N")[..20],
            IssuedAt = Now.AddDays(-40),
            ExpiresAt = Now.AddDays(20),
        };

        private HttpContext Context(MuiUser? user)
        {
            var context = new DefaultHttpContext();

            context.Items[LocaleRouting.ItemKey] =
                new LocaleContext(Locales.Find(_tag)!, FromPath: _tag != Locales.SourceTag);

            if (user is not null)
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new System.Security.Claims.Claim(ClaimTypes.NameIdentifier, user.Id.ToString())],
                    "Test"));
            }

            return context;
        }
    }

    private sealed class Frozen(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }

    private sealed class At : NavigationManager
    {
        public At(string query) => Initialize("http://localhost/", "http://localhost/account" + query);

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
        }
    }

    private sealed class NoAntiforgery : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken? GetAntiforgeryToken() => null;
    }

    private sealed class InMemoryClaims(List<GameClaim> claims) : IClaimStore
    {
        public Task<GameClaim?> FindAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(claims.FirstOrDefault(c => c.Id == id));

        public Task<IReadOnlyList<GameClaim>> ForGameAsync(Guid game, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GameClaim>>([.. claims.Where(c => c.GameId == game)]);

        public Task<IReadOnlyList<GameClaim>> ForUserAsync(Guid user, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GameClaim>>([.. claims.Where(c => c.UserId == user)]);

        public Task<GameClaim?> FindPendingByTokenAsync(Guid game, string token, CancellationToken ct = default) =>
            Task.FromResult<GameClaim?>(null);

        public Task InsertAsync(GameClaim claim, CancellationToken ct = default) => Task.CompletedTask;

        public Task UpdateAsync(GameClaim claim, CancellationToken ct = default) => Task.CompletedTask;

        public Task RecordEventAsync(ClaimEvent e, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<ClaimEvent>> EventsAsync(Guid claim, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ClaimEvent>>(
                [new ClaimEvent(claim, Now.AddDays(-30), ClaimEventKind.Verified, "Mssp")]);
    }

    private sealed class InMemoryFields : IGameFieldStore
    {
        private readonly Dictionary<(Guid, string, FieldSource), GameField> _rows = [];

        public Task<IReadOnlyList<GameField>> ForGameAsync(Guid game, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GameField>>([.. _rows.Values.Where(f => f.GameId == game)]);

        public Task<IReadOnlyList<GameField>> ForGameAsync(
            Guid game,
            FieldSource only,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GameField>>(
                [.. _rows.Values.Where(f => f.GameId == game && f.Source == only)]);

        public Task UpsertAsync(GameField field, CancellationToken ct = default)
        {
            _rows[(field.GameId, field.Field, field.Source)] = field;
            return Task.CompletedTask;
        }

        public Task RecordChangeAsync(FieldChange change, CancellationToken ct = default) => Task.CompletedTask;

        public Task<DateTimeOffset?> LastChangedAtAsync(Guid game, string field, CancellationToken ct = default) =>
            Task.FromResult<DateTimeOffset?>(null);
    }

    private sealed class InMemoryGames : IGameStore
    {
        public Task<GameRecord?> ByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<GameRecord?>(null);

        public Task<GameRecord?> BySlugAsync(string slug, CancellationToken ct = default) =>
            Task.FromResult<GameRecord?>(null);

        public Task InsertAsync(GameRecord game, CancellationToken ct = default) => Task.CompletedTask;

        public Task ExcludeAsync(Guid id, string reason, DateTimeOffset at, CancellationToken ct = default) =>
            SetStateAsync(id, LifecycleState.Excluded, at, ct);

        public Task IncludeAsync(Guid id, DateTimeOffset at, CancellationToken ct = default) =>
            SetStateAsync(id, LifecycleState.Active, at, ct);

        /// <summary>Which games were unlisted here, and on whose say-so.</summary>
        public Dictionary<Guid, Guid> Unlisted { get; } = [];

        /// <summary>Which games were put back.</summary>
        public List<Guid> Relisted { get; } = [];

        public Task UnlistAsync(Guid id, Guid byUserId, DateTimeOffset at, CancellationToken ct = default)
        {
            Unlisted[id] = byUserId;
            return Task.CompletedTask;
        }

        public Task RelistAsync(Guid id, DateTimeOffset at, CancellationToken ct = default)
        {
            Relisted.Add(id);
            return Task.CompletedTask;
        }

        public Task SetStateAsync(Guid id, LifecycleState state, DateTimeOffset at, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SetClaimedAsync(Guid id, bool claimed, CancellationToken ct = default) => Task.CompletedTask;

        public Task CorroborateAsync(
            Guid id,
            DateTimeOffset at,
            IReadOnlyList<string> signals,
            CancellationToken ct = default) => Task.CompletedTask;

        public Task MarkReachableAsync(Guid id, DateTimeOffset at, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<GameRecord>> UnarchivedAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GameRecord>>([]);

        public Task<string?> RenameAsync(
            Guid id,
            string name,
            string slug,
            DateTimeOffset at,
            CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
    }
}
