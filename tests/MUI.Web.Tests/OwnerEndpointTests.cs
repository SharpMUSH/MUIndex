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
using MUI.Web.Accounts;

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
/// The pipeline below is <c>Program</c>'s, in <c>Program</c>'s order, and the order is the point. See
/// <see cref="TheTokenIsCheckedAgainstTheSignedInOperatorSoTheOrderOfTheMiddlewareMatters"/>.
/// </para>
/// </remarks>
public class OwnerEndpointTests
{
    private static readonly Guid Game = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000007");

    /// <summary>An owner's form post lands, and sends them back to the dashboard saying so.</summary>
    [Test]
    public async Task AVerifiedOwnersFormPostIsAcceptedAndRedirectsBackSaying()
    {
        await using var host = await Harness.StartAsync();

        var response = await host.PostAsync(
            $"/account/games/{Game}/enrichment",
            new() { [OwnerWrites.FieldPrefix + "FANDOM"] = "Exalted" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Found);
        await Assert.That(response.Headers.Location!.ToString()).IsEqualTo($"/account?saved={Game}");

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
    /// This is the evidence for the ordering comment in <c>Program</c>, and it is asserted rather
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
    private sealed class Harness : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _client;

        private Harness(WebApplication app, HttpClient client, InMemoryFieldStore fields)
        {
            _app = app;
            _client = client;
            Fields = fields;
        }

        public InMemoryFieldStore Fields { get; }

        public static async Task<Harness> StartAsync(
            bool verified = true,
            bool antiforgeryBeforeAuthentication = false)
        {
            var user = new MuiUser { DisplayName = "owner", CreatedAt = DateTimeOffset.UtcNow };
            var fields = new InMemoryFieldStore();
            var claims = new InMemoryClaimStore(Game, user.Id, verified);

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

            var app = builder.Build();

            // Program's order, and the reason for it is asserted above.
            if (antiforgeryBeforeAuthentication)
            {
                app.UseAntiforgery();
                app.UseAuthentication();
                app.UseAuthorization();
            }
            else
            {
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseAntiforgery();
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
                fields);
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

        public Task UpsertAsync(GameField field, CancellationToken cancellationToken = default)
        {
            _rows[(field.GameId, field.Field, field.Source)] = field;
            return Task.CompletedTask;
        }

        public Task RecordChangeAsync(FieldChange change, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
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
