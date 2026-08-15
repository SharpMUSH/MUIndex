using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Discovery;
using MUI.Web.Accounts;
using MUI.Web.Fixtures;

namespace MUI.Web.Tests;

/// <summary>
/// The owner write path over real HTTP: the form posts, the token is checked, and the pipeline is in
/// the order that makes both true.
/// </summary>
/// <remarks>
/// <para>
/// A real server rather than a called handler, because what is being asserted is not the service —
/// that is pinned against Postgres in <c>OwnerEnrichmentPostgresTests</c> — but the wiring around it:
/// route, anti-forgery, authentication, redirect. Every one of those is invisible to a unit test and
/// every one of them ships broken silently.
/// </para>
/// <para>
/// The pipeline below is <c>Program</c>'s, through <c>Program</c>'s own
/// <see cref="SiteComposition.UseMuiAntiforgeryAfterAuthentication"/>, and the order is the point.
/// See <see cref="TheTokenIsCheckedAgainstTheSignedInOperatorSoTheOrderOfTheMiddlewareMatters"/>.
/// </para>
/// </remarks>
public class OwnerEndpointTests
{
    private static readonly Guid Game = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000007");

    /// <summary>The fixture game with two listeners, which is what makes the scoping assertable.</summary>
    private static readonly Guid Mush = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    /// <summary>An owner's form post lands, and sends them back to the dashboard saying so.</summary>
    [Test]
    public async Task AVerifiedOwnersFormPostIsAcceptedAndRedirectsBackSaying()
    {
        await using var host = await Harness.StartAsync();

        var response = await host.PostAsync(
            $"/account/games/{Game}/enrichment",
            new() { [OwnerWrites.FieldPrefix + "FANDOM"] = "Exalted" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Found);
        await Assert.That(response.Headers.Location!.ToString())
            .IsEqualTo($"/account?saved={Game}&did=fields");

        var stored = (await host.Fields.ForGameAsync(Game)).Single();

        await Assert.That(stored.Source).IsEqualTo(FieldSource.Owner);
        await Assert.That(stored.Value).IsEqualTo("Exalted");
    }

    /// <summary>§11's one click, and the field it sets.</summary>
    [Test]
    public async Task SuppressingTheConnectScreenIsOneUnexplainedPost()
    {
        await using var host = await Harness.StartAsync();

        var response = await host.PostAsync(
            $"/account/games/{Game}/connect-screen",
            new() { ["suppress"] = "true" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Found);

        var stored = (await host.Fields.ForGameAsync(Game)).Single();

        await Assert.That(stored.Field).IsEqualTo(InternalFields.ConnectScreenSuppressed);
        await Assert.That(stored.Value).IsEqualTo("true");
    }

    /// <summary>
    /// The two writes are two different things, and the dashboard is told which one happened.
    /// </summary>
    /// <remarks>
    /// Both endpoints redirected with <c>?saved=</c> and nothing else, so hiding a connect screen
    /// came back as the enrichment sentence — "your page now shows it as owner-declared" — about an
    /// action nobody took. On the surface whose whole job is to say what happened.
    /// </remarks>
    [Test]
    public async Task HidingAScreenAndSavingAFieldDoNotReportTheSameThing()
    {
        await using var host = await Harness.StartAsync();

        var fields = await host.PostAsync(
            $"/account/games/{Game}/enrichment",
            new() { [OwnerWrites.FieldPrefix + "FANDOM"] = "Exalted" });

        var hidden = await host.PostAsync(
            $"/account/games/{Game}/connect-screen",
            new() { ["suppress"] = "true" });

        var shown = await host.PostAsync(
            $"/account/games/{Game}/connect-screen",
            new() { ["suppress"] = "false" });

        await Assert.That(fields.Headers.Location!.ToString()).EndsWith("did=fields");
        await Assert.That(hidden.Headers.Location!.ToString()).EndsWith("did=screen-hidden");
        await Assert.That(shown.Headers.Location!.ToString()).EndsWith("did=screen-shown");
    }

    /// <summary>
    /// A field outside §8.5's set is refused out loud, and the dashboard is told which one.
    /// </summary>
    [Test]
    public async Task APostNamingAMeasurementComesBackNamingIt()
    {
        await using var host = await Harness.StartAsync();

        var response = await host.PostAsync(
            $"/account/games/{Game}/enrichment",
            new() { [OwnerWrites.FieldPrefix + "CODEBASE"] = "PennMUSH 9.9.9" });

        await Assert.That(response.Headers.Location!.ToString())
            .IsEqualTo("/account?refused=CODEBASE&because=NotEnrichable");
        await Assert.That((await host.Fields.ForGameAsync(Game)).Count).IsEqualTo(0);
    }

    /// <summary>Somebody with no verified claim on the game gets a refusal, not a dashboard note.</summary>
    /// <remarks>
    /// It is not a mistake an owner can correct on a page, so it does not get a page. The service is
    /// what decides this; the endpoint only declines to dress it up as something else.
    /// </remarks>
    [Test]
    public async Task SomebodyWhoNeverProvedAnythingIsRefusedOutright()
    {
        await using var host = await Harness.StartAsync(verified: false);

        var response = await host.PostAsync(
            $"/account/games/{Game}/enrichment",
            new() { [OwnerWrites.FieldPrefix + "FANDOM"] = "Exalted" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That((await host.Fields.ForGameAsync(Game)).Count).IsEqualTo(0);
    }

    /// <summary>A post with no anti-forgery token is not a post from our form.</summary>
    [Test]
    public async Task APostWithoutATokenIsRejected()
    {
        await using var host = await Harness.StartAsync();

        var response = await host.PostAsync(
            $"/account/games/{Game}/enrichment",
            new() { [OwnerWrites.FieldPrefix + "FANDOM"] = "Exalted" },
            withToken: false);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await host.Fields.ForGameAsync(Game)).Count).IsEqualTo(0);
    }

    /// <summary>
    /// The token carries who it was issued to, so anti-forgery must run after authentication.
    /// </summary>
    /// <remarks>
    /// This is the evidence for the ordering rule in <see cref="SiteComposition"/>, and it is
    /// asserted rather
    /// than believed because the failure it prevents is total and silent: validated before the
    /// authentication middleware, every signed-in operator's form post is compared against an
    /// anonymous user and rejected as forged, while every public page — all of them GET — goes on
    /// working perfectly.
    /// </remarks>
    [Test]
    public async Task TheTokenIsCheckedAgainstTheSignedInOperatorSoTheOrderOfTheMiddlewareMatters()
    {
        await using var host = await Harness.StartAsync(antiforgeryBeforeAuthentication: true);

        var response = await host.PostAsync(
            $"/account/games/{Game}/enrichment",
            new() { [OwnerWrites.FieldPrefix + "FANDOM"] = "Exalted" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// The two endpoints on a loopback port, behind the pipeline <c>Program</c> builds.
    /// </summary>
    /// <summary>
    /// An owner's stop covers exactly the addresses their game answers on — and no more.
    /// </summary>
    /// <remarks>
    /// The one thing worth getting right here. A record with a null port means <em>every port on
    /// this host</em>, and a shared machine — a hosting provider, somebody running four games on one
    /// box — would then have three other people's games delisted by one owner's button. So the rows
    /// name ports, and the game with two of them gets two rows.
    /// </remarks>
    [Test]
    public async Task StoppingCoversTheGamesOwnPortsAndNeverTheWholeHost()
    {
        await using var host = await Harness.StartAsync(game: Mush);

        var response = await host.PostAsync($"/account/games/{Mush}/crawl", new() { ["stop"] = "true" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Found);
        await Assert.That(response.Headers.Location!.ToString())
            .IsEqualTo($"/account?saved={Mush}&did=crawl-stopped");

        var recorded = host.OptOuts.All;

        await Assert.That(recorded).Count().IsEqualTo(2);
        await Assert.That(recorded.Select(o => o.Port).ToList()).IsEquivalentTo(new int?[] { 4201, 4202 });

        // Never the whole host, however convenient that would have been to write.
        await Assert.That(recorded.Any(o => o.Port is null)).IsFalse();
        await Assert.That(recorded.All(o => o.Source is OptOutSource.Request)).IsTrue();

        // §11 requires the request route to say who asked, and the account is the one that can.
        await Assert.That(recorded[0].Detail).Contains("owner");
    }

    /// <summary>Taken back, with the row kept — nothing is ever deleted.</summary>
    [Test]
    public async Task AnOwnerCanStartUsAgainAndTheRecordSurvives()
    {
        await using var host = await Harness.StartAsync(game: Mush);

        await host.PostAsync($"/account/games/{Mush}/crawl", new() { ["stop"] = "true" });
        var response = await host.PostAsync($"/account/games/{Mush}/crawl", new() { ["stop"] = "false" });

        await Assert.That(response.Headers.Location!.ToString())
            .IsEqualTo($"/account?saved={Mush}&did=crawl-resumed");

        // Withdrawn, not gone: "they asked us to stop, and later asked us back" is a thing the
        // register has to be able to say.
        await Assert.That(host.OptOuts.All).Count().IsEqualTo(2);
        await Assert.That(host.OptOuts.All.All(o => o.Standing)).IsFalse();
    }

    /// <summary>Somebody without a verified claim is refused, and writes nothing.</summary>
    [Test]
    public async Task AnUnverifiedClaimCannotStopTheCrawl()
    {
        await using var host = await Harness.StartAsync(verified: false, game: Mush);

        var response = await host.PostAsync($"/account/games/{Mush}/crawl", new() { ["stop"] = "true" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(host.OptOuts.All).IsEmpty();
    }

    /// <summary>
    /// A game we hold no address for is told so, rather than told we stopped.
    /// </summary>
    /// <remarks>
    /// The failure mode of a quiet success here is an owner who believes we have stopped dialling
    /// them and has not been told we never had an address to stop dialling.
    /// </remarks>
    [Test]
    public async Task AGameWithNoAddressIsRefusedOutLoud()
    {
        var unlisted = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000f");
        await using var host = await Harness.StartAsync(game: unlisted);

        var response = await host.PostAsync(
            $"/account/games/{unlisted}/crawl", new() { ["stop"] = "true" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Found);
        await Assert.That(response.Headers.Location!.ToString()).Contains("refused=crawl");
        await Assert.That(host.OptOuts.All).IsEmpty();
    }

    /// <summary>§11's register, in memory. Keeps rows and withdraws them, exactly as the real one.</summary>
    private sealed class InMemoryOptOuts : ICrawlOptOutRepository
    {
        private readonly List<CrawlOptOut> _rows = [];

        public IReadOnlyList<CrawlOptOut> All => _rows;

        public Task<CrawlOptOut?> StandingAsync(string host, int port, CancellationToken ct) =>
            Task.FromResult(_rows.FirstOrDefault(r => r.Standing && r.Covers(host, port)));

        public Task<OptOutRecording> RecordAsync(CrawlOptOut optOut, CancellationToken ct)
        {
            var existing = _rows.FindIndex(
                r => r.Host == optOut.Host && r.Port == optOut.Port && r.Source == optOut.Source);

            if (existing >= 0)
            {
                // A repeat ask confirms and un-withdraws, and leaves the first date alone.
                _rows[existing] = _rows[existing] with
                {
                    LastConfirmedAt = optOut.LastConfirmedAt,
                    WithdrawnAt = null,
                };

                return Task.FromResult(new OptOutRecording(_rows[existing], IsFirstAsk: false));
            }

            _rows.Add(optOut);

            return Task.FromResult(new OptOutRecording(optOut, IsFirstAsk: true));
        }

        public Task WithdrawAsync(
            string host, int? port, OptOutSource route, DateTimeOffset at, CancellationToken ct)
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Host == host && _rows[i].Port == port && _rows[i].Source == route)
                {
                    _rows[i] = _rows[i] with { WithdrawnAt = at };
                }
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CrawlOptOut>> AllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CrawlOptOut>>(_rows);
    }

    /// <summary>
    /// No zone to read.
    /// </summary>
    /// <remarks>
    /// Answers <see cref="DnsTxtAnswer.NoRecord"/> rather than <see cref="DnsTxtAnswer.NoAnswer"/>:
    /// DNS replying "no such record" is a fact, and a lookup that failed is not one. The owner route
    /// asks DNS nothing either way, and this says so honestly rather than by silence.
    /// </remarks>
    private sealed class NoDns : IDnsTxtResolver
    {
        public Task<DnsTxtAnswer> LookupAsync(
            string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(DnsTxtAnswer.NoRecord);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _client;

        private Harness(
            WebApplication app,
            HttpClient client,
            InMemoryFieldStore fields,
            InMemoryOptOuts optOuts)
        {
            _app = app;
            _client = client;
            Fields = fields;
            OptOuts = optOuts;
        }

        public InMemoryFieldStore Fields { get; }

        public InMemoryOptOuts OptOuts { get; }

        /// <param name="game">
        /// Which game the claim is on. Defaults to the one the enrichment tests use; the opt-out
        /// tests name the fixture game with two listeners, because one address cannot demonstrate
        /// that a stop is scoped to ports rather than to a whole host.
        /// </param>
        public static async Task<Harness> StartAsync(
            bool verified = true,
            bool antiforgeryBeforeAuthentication = false,
            Guid? game = null)
        {
            var user = new MuiUser { DisplayName = "owner", CreatedAt = DateTimeOffset.UtcNow };
            var fields = new InMemoryFieldStore();
            var claims = new InMemoryClaimStore(game ?? Game, user.Id, verified);

            var builder = WebApplication.CreateSlimBuilder();

            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");

            builder.Services.AddAntiforgery();
            builder.Services.AddAuthorization();
            builder.Services
                .AddAuthentication(StubAuthentication.Name)
                .AddScheme<AuthenticationSchemeOptions, StubAuthentication>(
                    StubAuthentication.Name, _ => { });

            builder.Services.AddSingleton(user);
            builder.Services.AddIdentityCore<MuiUser>().AddUserStore<StubUserStore>();

            builder.Services.AddSingleton<OwnerEnrichment>(_ => new OwnerEnrichment(
                claims, fields, new FieldReconciler(fields), FieldRegistry.Instance, TimeProvider.System));

            // §11's register, in memory, behind the real OptOutGate — the recording path under test
            // is the deployed one rather than a second implementation of it written here.
            var optOuts = new InMemoryOptOuts();

            builder.Services.AddSingleton(optOuts);
            builder.Services.AddSingleton<ICrawlOptOutRepository>(optOuts);
            builder.Services.AddSingleton<IGameQueries, FixtureGameQueries>();
            builder.Services.AddSingleton(s => new OptOutGate(
                optOuts, new NoDns(), TimeProvider.System));
            builder.Services.AddSingleton<OwnerOptOut>();

            var app = builder.Build();

            // The site's own order, through the site's own call — not a copy of it. A harness that
            // restated these three lines would assert its own ordering and go on passing through the
            // edit that reordered the deployed one, which is precisely the failure the test below
            // exists to prevent. Only the WRONG order is built by hand here, because there is no
            // other way to build a thing that is not supposed to exist.
            if (antiforgeryBeforeAuthentication)
            {
                app.UseAntiforgery();
                app.UseAuthentication();
                app.UseAuthorization();
            }
            else
            {
                app.UseMuiAntiforgeryAfterAuthentication(withAccounts: true);
            }

            // What <AntiforgeryToken /> does while a page renders, which is to say after the
            // authentication middleware has run and with the operator signed in.
            app.MapGet("/token", (HttpContext context, IAntiforgery antiforgery) =>
                antiforgery.GetAndStoreTokens(context).RequestToken);

            app.MapMuiOwnerWrites();

            await app.StartAsync();

            var address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.First();

            var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                UseCookies = true,
                AllowAutoRedirect = false,
            };

            return new Harness(
                app,
                new HttpClient(handler) { BaseAddress = new Uri(address) },
                fields,
                optOuts);
        }

        public async Task<HttpResponseMessage> PostAsync(
            string path,
            Dictionary<string, string> form,
            bool withToken = true)
        {
            if (withToken)
            {
                form["__RequestVerificationToken"] = await _client.GetStringAsync("/token");
            }

            return await _client.PostAsync(path, new FormUrlEncodedContent(form));
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    /// <summary>A signed-in operator, so the endpoints have somebody to be.</summary>
    private sealed class StubAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        MuiUser user)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "Stub";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())], Name);

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Name)));
        }
    }

    /// <summary>Enough of a user store for <c>UserManager.GetUserAsync</c> to find one account.</summary>
    private sealed class StubUserStore(MuiUser user) : IUserStore<MuiUser>
    {
        public Task<MuiUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(string.Equals(userId, user.Id.ToString(), StringComparison.Ordinal) ? user : null);

        public Task<string> GetUserIdAsync(MuiUser u, CancellationToken cancellationToken) =>
            Task.FromResult(u.Id.ToString());

        public Task<string?> GetUserNameAsync(MuiUser u, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(u.DisplayName);

        public Task<string?> GetNormalizedUserNameAsync(MuiUser u, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(u.NormalisedName);

        public Task<MuiUser?> FindByNameAsync(string normalisedName, CancellationToken cancellationToken) =>
            Task.FromResult<MuiUser?>(null);

        public Task SetUserNameAsync(MuiUser u, string? name, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SetNormalizedUserNameAsync(MuiUser u, string? name, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IdentityResult> CreateAsync(MuiUser u, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> UpdateAsync(MuiUser u, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> DeleteAsync(MuiUser u, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// The field store, keyed as the real table is.
    /// </summary>
    /// <remarks>
    /// A fake must never be more lenient than the real thing: this is keyed
    /// <c>(game, field, source)</c> like <c>game_field</c>, because one keyed on <c>(game, field)</c>
    /// would collapse a declared value onto a measured one and hide the property these tests exist
    /// to protect.
    /// </remarks>
    private sealed class InMemoryFieldStore : IGameFieldStore
    {
        private readonly Dictionary<(Guid, string, FieldSource), GameField> _rows = [];

        public Task<IReadOnlyList<GameField>> ForGameAsync(
            Guid gameId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GameField>>(
                _rows.Values.Where(row => row.GameId == gameId).ToList());

    public Task<IReadOnlyList<GameField>> ForGameAsync(
        Guid gameId,
        FieldSource only,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GameField>>(
            [.. _rows.Values.Where(f => f.GameId == gameId && f.Source == only)]);

        public Task UpsertAsync(GameField field, CancellationToken cancellationToken = default)
        {
            _rows[(field.GameId, field.Field, field.Source)] = field;
            return Task.CompletedTask;
        }

        public Task RecordChangeAsync(FieldChange change, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        /// <summary>
        /// Never, because this store does not keep the changes §5.7's rename grace reads.
        /// </summary>
        /// <remarks>
        /// Null is the honest answer and not a stub: <see cref="RecordChangeAsync"/> above already
        /// discards what it is handed, so "no change has been recorded for this field" is exactly
        /// true of this store. Nothing on the owner write path asks — the caller is
        /// <c>SlugMinter</c>, which these tests do not exercise.
        /// </remarks>
        public Task<DateTimeOffset?> LastChangedAtAsync(
            Guid gameId,
            string field,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<DateTimeOffset?>(null);
    }

    /// <summary>One account, one game, and whether they ever proved anything.</summary>
    private sealed class InMemoryClaimStore(Guid game, Guid user, bool verified) : IClaimStore
    {
        private readonly GameClaim _claim = new()
        {
            Id = Guid.CreateVersion7(),
            GameId = game,
            UserId = user,
            Token = "muidx-22222222222222222222",
            IssuedAt = DateTimeOffset.UtcNow.AddDays(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(29),
            ClaimedAt = verified ? DateTimeOffset.UtcNow.AddDays(-1) : null,
            VerifiedVia = verified ? ClaimChannel.Mssp : null,
        };

        public Task<GameClaim?> FindAsync(Guid claimId, CancellationToken cancellationToken = default) =>
            Task.FromResult<GameClaim?>(_claim.Id == claimId ? _claim : null);

        public Task<IReadOnlyList<GameClaim>> ForGameAsync(
            Guid gameId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GameClaim>>(_claim.GameId == gameId ? [_claim] : []);

        public Task<IReadOnlyList<GameClaim>> ForUserAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GameClaim>>(_claim.UserId == userId ? [_claim] : []);

        public Task<GameClaim?> FindPendingByTokenAsync(
            Guid gameId,
            string token,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<GameClaim?>(null);

        public Task InsertAsync(GameClaim claim, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UpdateAsync(GameClaim claim, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RecordEventAsync(ClaimEvent claimEvent, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<ClaimEvent>> EventsAsync(
            Guid claimId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ClaimEvent>>([]);
    }
}
