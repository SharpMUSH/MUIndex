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
/// <b>On authentication.</b> Signed-out and no-database states go through <see cref="SiteHost"/> end
/// to end. Signed-in states can't — §8.2 makes passkeys the only way in, and a loopback host has no
/// authenticator — so they're rendered at component level with an <see cref="HttpContext"/> cascaded
/// in, standing in for the store and credential ceremony only; the page's own auth guard and
/// <see cref="ClaimService"/> are real.
/// </remarks>
public class AccountSurfaceTests
{
    private static readonly DateTimeOffset Now = FixtureGameQueries.Now;

    /// <summary>A game from the fixture, so the page's own lookup by id has something to find.</summary>
    private static readonly Guid Ashen = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000007");

    private static readonly Guid Mush = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    // ── the states an operator is in ──────────────────────────────────────────

    /// <summary>A site with no database says so, rather than offering half a claim flow.</summary>
    /// <remarks>Through the real host, reached by the ordinary pipeline rather than a component rendered out of context.</remarks>
    [Test]
    public async Task WithNoDatabaseThePageSaysAccountsNeedOne()
    {
        await using var host = await SiteHost.StartAsync();

        var body = Render.Words(await host.Client.GetStringAsync("/account"));

        // Through the bundle, not a pasted sentence, so this still holds after a reword.
        await Assert.That(body).Contains(En("account.noDatabase"));
        await Assert.That(body).DoesNotContain(En("account.signInButton"));
        await Assert.That(body).DoesNotContain(En("account.resign.summary"));
    }

    /// <summary>One message in the source language, as it reads once rendered.</summary>
    private static string En(string id) => Render.Words(Messages.For(Locales.SourceTag, id));

    /// <summary>
    /// A placed sentence in the source language, with its slots filled as the page fills them.
    /// </summary>
    /// <remarks>Reassembles <see cref="Sentence.Place"/>'s runs into the sentence a reader ends up with, so assertions read the same bundle the page does rather than a pasted English string.</remarks>
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
    /// Both branches: the demo-fixture page and the behind-a-database page are different pages with
    /// different copy. The pseudolocale accents anything routed through <see cref="Messages"/>, so an
    /// English sentence surviving it was typed straight into the markup.
    /// </remarks>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task EveryWordOfSigningInComesFromTheBundle(bool withDatabase)
    {
        var english = Render.Words(await SignInAsync(Locales.SourceTag, withDatabase));
        var pseudo = Render.Words(await SignInAsync("qps-ploc", withDatabase));

        await Assert.That(pseudo).Contains("⟦");

        // Which page this is, asserted rather than assumed.
        await Assert.That(english.Contains(
                Messages.For(Locales.SourceTag, "account.store.heading"), StringComparison.Ordinal))
            .IsEqualTo(withDatabase);

        await Assert.That(english.Contains(
                Messages.For(Locales.SourceTag, "account.signIn.noDatabase"), StringComparison.Ordinal))
            .IsEqualTo(!withDatabase);

        // Every sentence of the English page, absent from the same page in another language.
        // Argument-free ids only — a pattern needing arguments would throw rather than assert.
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
    /// <remarks>Locale arrives via <c>HttpContext.Items</c>, as the middleware leaves it — what the page reads. Identity is registered only when <paramref name="withDatabase"/>, the same condition the page branches on.</remarks>
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
    /// <remarks>All in one place: §8.5's enrichment fields, its MSSP scorecard, owner-published badge, audit log, and §8.4's resignation.</remarks>
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
    /// <remarks>Markup inside markup: checks it survived escaping as copyable text rather than rendered HTML.</remarks>
    [Test]
    public async Task TheBadgeSnippetIsCopyableRatherThanRendered()
    {
        var markup = await World.New().SignedIn().Verified(Ashen).RenderAsync();

        // Entity-encoded, quotes included — read off the rendered bytes, not a decoded assertion message.
        await Assert.That(markup).Contains("&lt;img src=&quot;/g/ashen-court/badge.svg&quot;");
        await Assert.That(markup).DoesNotContain("<img src=\"/g/ashen-court/badge.svg\"");
    }

    /// <summary>Several games each get their own block, and none is opened by default.</summary>
    /// <remarks>A page of five open forms is a page nobody reads; one game is the exception and stays open.</remarks>
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

        // One co-owner, so the message's `one` branch — a different sentence from the plural.
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
    /// <remarks>An else-if chain silently reports the wrong branch when it grows an arm, so every arm is driven here by the querystring the redirect actually carries.</remarks>
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

        // The id rather than a pasted fragment, so a reworded banner still has to be the right one.
        // Both arguments are supplied to every arm; a pattern that names neither ignores them.
        await Assert.That(words).Contains(Render.Words(Messages.Say(
            Locales.SourceTag, expected, ("game", AshenName), ("field", "CODEBASE"))));
    }

    /// <summary>The fixture game's own name, which the banner quotes and never translates.</summary>
    private const string AshenName = "Ashen Court";

    /// <summary>
    /// Two outcomes at once is one banner, and it is the one the chain says it is.
    /// </summary>
    /// <remarks>A redirect carries exactly one outcome, so this combination can't arise from the endpoints — pinned so a reordered else-if chain can't silently report the wrong action.</remarks>
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
    /// Driven on a verified claim with a co-owner and a banner, so the owner panel, badge block,
    /// audit log and resignation form all render. What stays English (a game's name, a field name, a
    /// confirmation word, an ISO stamp) is machine voice, named rather than tolerated.
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
    /// <b>Gated on <see cref="Messages.HasOwn"/>, not a list of ids.</b> Most of this page's ids fall
    /// back to English today by design, so naming ids here would freeze that in; asking the bundle
    /// means "German where there is German" stays true as translations land. The counter proves it's
    /// not vacuous — the owner panel already renders a translated word.
    /// </remarks>
    [Test]
    public async Task AGermanRequestGetsGermanOnTheDashboard()
    {
        var english = Render.Text(await Full(Locales.SourceTag));
        var german = Render.Text(await Full("de"));

        var translated = 0;

        // By the one id these pages render, not by prefix — the others carry "measured", which
        // occurs as a substring inside half this page's sentences.
        foreach (var id in Sayable("account.", "owner.").Append("provenance.game.ownerDeclared"))
        {
            var en = Render.Words(Messages.For(Locales.SourceTag, id));
            var de = Render.Words(Messages.For("de", id));

            // An id that fell back has nothing to assert; a word German spells the same way proves nothing.
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

        // A fallback shows the English, never the id — a reader seeing an id knows the site is broken.
        foreach (var id in Sayable("account.", "owner.").Where(i => !Messages.HasOwn("de", i)))
        {
            await Assert.That(german).DoesNotContain(id).Because($"{id} reached a reader as its id");
        }

        // Machine voice is untouched: the game's name and the confirmation word are not translated.
        await Assert.That(german).Contains(AshenName);
        await Assert.That(german).Contains(OwnershipWrites.ResignConfirmation);
    }

    /// <summary>Ids under these prefixes that can be rendered without arguments.</summary>
    /// <remarks>A pattern can't be formatted without the arguments it names, so a sweep over whole sentences leaves them out; each parameterised id has its own test.</remarks>
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
    /// <remarks>Two different sentences that must not be swapped: too-long vs. measured-so-not-editable. Giving the wrong reason would be the site claiming a rule it doesn't have.</remarks>
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
    /// <remarks>A form whose action nobody maps fails at the browser as a 404, not at build — nothing else catches it.</remarks>
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
    /// <remarks>Built, never started: account routes exist only when a connection string does, and the string here is never dialled.</remarks>
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

        // Off the builder, not the container: the composite EndpointDataSource in DI is empty until
        // the app starts.
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
    /// <remarks>In-memory stores, real services over them, so what the page branches on is decided by <see cref="ClaimService"/> rather than by the test.</remarks>
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
        /// <remarks>In <c>HttpContext.Items</c>, not a thread's culture — where the page reads it from.</remarks>
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

                // What the page's preview metadata switches on; the component asks for it regardless.
                services.AddSingleton(new MUI.Web.Data.CatalogueSource(IsMeasured: true));
                services.AddSingleton<TimeProvider>(new Frozen(Now));
                services.AddSingleton<NavigationManager>(new At(query));

                // Supplies [SupplyParameterFromQuery] outside the endpoint infrastructure; without it
                // the status banner silently never renders.
                services.AddSupplyValueFromQueryProvider();
                services.AddSingleton<AntiforgeryStateProvider, NoAntiforgery>();

                // Cascaded, not authenticated through a scheme — the page's own guard on it is under test.
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

        public Task<IReadOnlyList<GameClaim>> PendingOrDnsVerifiedAsync(
            DateTimeOffset now,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GameClaim>>(
                [.. claims.Where(c => c.IsPending(now) || (c.IsVerified && c.VerifiedVia is ClaimChannel.DnsTxt))]);

        public Task<GameClaim?> FindPendingByTokenAsync(
            Guid game,
            string token,
            DateTimeOffset now,
            CancellationToken ct = default) =>
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
