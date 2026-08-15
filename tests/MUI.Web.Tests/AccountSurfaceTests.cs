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
using MUI.Web.Components.Pages;
using MUI.Web.Fixtures;

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

        await Assert.That(body).Contains("Accounts need a database");
        await Assert.That(body).DoesNotContain("Sign in");
        await Assert.That(body).DoesNotContain("give up this claim");
    }

    /// <summary>A signed-out visitor is offered the way in and nothing else.</summary>
    [Test]
    public async Task ASignedOutVisitorIsOfferedSignInAndNoWriteSurface()
    {
        var markup = await World.New().Anonymous().RenderAsync();

        await Assert.That(markup).Contains("/account/sign-in");
        await Assert.That(markup).DoesNotContain("<form");
        await Assert.That(Render.Words(markup)).DoesNotContain("Claimed");
    }

    /// <summary>An account with nothing claimed is told where to start.</summary>
    [Test]
    public async Task AnAccountWithNoClaimsIsPointedAtTheListing()
    {
        var markup = await World.New().SignedIn().RenderAsync();
        var words = Render.Words(markup);

        await Assert.That(words).Contains("You have not claimed anything yet");
        await Assert.That(words).Contains("/games");
        await Assert.That(words).DoesNotContain("Waiting on a token");
        await Assert.That(markup).DoesNotContain("resign");
    }

    /// <summary>A pending claim is a link back to the page with the token on it.</summary>
    [Test]
    public async Task APendingClaimLinksToItsToken()
    {
        var markup = await World.New().SignedIn().Pending(Mush).RenderAsync();
        var words = Render.Words(markup);

        await Assert.That(words).Contains("Waiting on a token");
        await Assert.That(markup).Contains("/g/m-u-s-h/claim");

        // Pending is not owning: none of the owner surfaces appear for it.
        await Assert.That(words).DoesNotContain("Claimed");
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

        await Assert.That(words).Contains("Claimed");
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
        await Assert.That(words).Contains("history");
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

        await Assert.That(words).Contains("Also owned by thistle");
        await Assert.That(words).Contains("verified a token of their own");

        // The resign copy is the one that differs when somebody else holds the game too.
        await Assert.That(words).Contains("the game stays claimed if anybody else owns it");
    }

    /// <summary>A sole owner sees no co-owner line at all.</summary>
    [Test]
    public async Task ASoleOwnerIsNotToldAboutOwnersWhoDoNotExist()
    {
        var words = Render.Words(await World.New().SignedIn().Verified(Ashen).RenderAsync());

        await Assert.That(words).DoesNotContain("Also owned by");
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
    [Arguments("?saved={game}&did=fields", "now shows it as owner-declared")]
    [Arguments("?saved={game}&did=screen-hidden", "stopped republishing")]
    [Arguments("?saved={game}&did=screen-shown", "connect screen is on its page again")]
    [Arguments("?resigned=1", "Given up.")]
    [Arguments("?refused=CODEBASE&because=NotEnrichable", "CODEBASE was not changed")]
    public async Task EveryOutcomeReportsTheActionThatHappened(string query, string expected)
    {
        var words = Render.Words(await World.New()
            .SignedIn()
            .Verified(Ashen)
            .RenderAsync(query.Replace("{game}", Ashen.ToString(), StringComparison.Ordinal)));

        await Assert.That(words).Contains(expected);
    }

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

        await Assert.That(words).Contains("Given up.");
        await Assert.That(words).DoesNotContain("now shows it as owner-declared");
        await Assert.That(words).DoesNotContain("was not changed");
    }

    /// <summary>An enrichment refusal names the field and says which rule refused it.</summary>
    [Test]
    public async Task ARefusalOverLengthSaysSoRatherThanBlamingTheField()
    {
        var words = Render.Words(await World.New()
            .SignedIn()
            .Verified(Ashen)
            .RenderAsync("?refused=FANDOM&because=TooLong"));

        await Assert.That(words).Contains("FANDOM was not changed");
        await Assert.That(words).Contains($"{OwnerEnrichment.MaxValueLength} characters");
        await Assert.That(words).DoesNotContain("That field is measured");
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

        public static World New() => new();

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

                services.AddSingleton<IUserStore<MuiUser>>(new Accounts(_accounts));
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

        private static HttpContext Context(MuiUser? user)
        {
            var context = new DefaultHttpContext();

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

        public Task SetStateAsync(Guid id, LifecycleState state, DateTimeOffset at, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SetClaimedAsync(Guid id, bool claimed, CancellationToken ct = default) => Task.CompletedTask;

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

    /// <summary>
    /// Enough of a user store to find an account and list its passkeys.
    /// </summary>
    /// <remarks>
    /// Identity's <c>UserManager</c> is a concrete type over a store interface, so a page that calls
    /// it needs one. This is persistence rather than authorisation: which account the page is for
    /// comes from the cascaded principal, and what that account may see comes from the claim store.
    /// </remarks>
    private sealed class Accounts(List<MuiUser> users) : IUserStore<MuiUser>, IUserPasskeyStore<MuiUser>
    {
        public Task<MuiUser?> FindByIdAsync(string id, CancellationToken ct) =>
            Task.FromResult(users.FirstOrDefault(u => u.Id.ToString() == id));

        public Task<string> GetUserIdAsync(MuiUser user, CancellationToken ct) =>
            Task.FromResult(user.Id.ToString());

        public Task<string?> GetUserNameAsync(MuiUser user, CancellationToken ct) =>
            Task.FromResult<string?>(user.DisplayName);

        public Task<string?> GetNormalizedUserNameAsync(MuiUser user, CancellationToken ct) =>
            Task.FromResult<string?>(user.NormalisedName);

        public Task<MuiUser?> FindByNameAsync(string name, CancellationToken ct) =>
            Task.FromResult(users.FirstOrDefault(u => u.NormalisedName == name));

        public Task SetUserNameAsync(MuiUser user, string? name, CancellationToken ct) => Task.CompletedTask;

        public Task SetNormalizedUserNameAsync(MuiUser user, string? name, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<IdentityResult> CreateAsync(MuiUser user, CancellationToken ct) =>
            Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> UpdateAsync(MuiUser user, CancellationToken ct) =>
            Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> DeleteAsync(MuiUser user, CancellationToken ct) =>
            Task.FromResult(IdentityResult.Success);

        public Task AddOrUpdatePasskeyAsync(MuiUser user, UserPasskeyInfo passkey, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<IList<UserPasskeyInfo>> GetPasskeysAsync(MuiUser user, CancellationToken ct) =>
            Task.FromResult<IList<UserPasskeyInfo>>(
            [
                new UserPasskeyInfo(
                    [1, 2, 3],
                    [4, 5, 6],
                    Now.AddYears(-1),
                    0u,
                    ["internal"],
                    true,
                    true,
                    true,
                    [],
                    [])
                {
                    Name = "the yubikey in the drawer",
                },
            ]);

        public Task<MuiUser?> FindByPasskeyIdAsync(byte[] id, CancellationToken ct) =>
            Task.FromResult<MuiUser?>(null);

        public Task<UserPasskeyInfo?> FindPasskeyAsync(MuiUser user, byte[] id, CancellationToken ct) =>
            Task.FromResult<UserPasskeyInfo?>(null);

        public Task RemovePasskeyAsync(MuiUser user, byte[] id, CancellationToken ct) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }
}
