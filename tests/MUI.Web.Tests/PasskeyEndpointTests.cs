using System.Net;
using System.Text;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using MUI.Web;

namespace MUI.Web.Tests;

/// <summary>
/// The passkey ceremony's four endpoints, driven as the browser drives them.
/// </summary>
/// <remarks>
/// <para>
/// <b>§8.2 makes passkeys the only way in</b>, so if these refuse a request nobody can sign in,
/// nobody can claim anything, and every owner surface on the site is unreachable. Nothing tested
/// them: the suites that exercise sign-in are the ones that stub it, and the composition tests stop
/// at the graph.
/// </para>
/// <para>
/// These drive the real routes through the real pipeline — <see cref="SiteComposition.AddMuiSite"/>
/// and <see cref="SiteComposition.UseMuiSite"/>, the same two calls the deployable makes — because
/// the property at issue is what the middleware does to a request, and no synthetic endpoint can
/// answer that for the endpoints that actually exist.
/// </para>
/// <para>
/// No database is behind them. The connection string points at a port nothing listens on, which is
/// enough for everything asserted here: <c>assertion-options</c> answers in full without storage,
/// and the two that need it are asserted on what the middleware did rather than on how the handler
/// ended. Against a real Postgres all three answer 200, 200 and 401 — measured, on a host with
/// migrations applied.
/// </para>
/// </remarks>
public class PasskeyEndpointTests
{
    /// <summary>What <c>passkey.js</c> posts: a JSON envelope with the credential as a string.</summary>
    private const string SignInBody =
        """
        {"credential":"{\"id\":\"abc\",\"rawId\":\"abc\",\"type\":\"public-key\",\"response\":{\"clientDataJSON\":\"e30\",\"authenticatorData\":\"e30\",\"signature\":\"e30\",\"userHandle\":null}}"}
        """;

    /// <summary>
    /// The anti-forgery middleware is live on this host, which is what makes the rest meaningful.
    /// </summary>
    /// <remarks>
    /// The control, and it has to come first: "the passkey endpoints are not blocked" says nothing
    /// unless something on the same host <em>is</em>. <c>MapRazorComponents</c> puts anti-forgery
    /// metadata on every component route, so a POST to one without a token is refused — and that is
    /// the same middleware, in the same pipeline, one route over.
    /// </remarks>
    [Test]
    public async Task AntiforgeryIsLiveAndRefusesAnUntokenedPostToAPage()
    {
        await using var host = await Host.StartAsync();

        var response = await host.PostAsync("/account", content: null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// The ceremony's JSON endpoints are not anti-forgery-gated, and answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A minimal API is given anti-forgery metadata only when it binds <em>form</em> data —
    /// <c>IFormCollection</c>, <c>IFormFile</c>, <c>[FromForm]</c>. These bind JSON or a query
    /// string, so they carry none and the middleware passes them through. That is the whole
    /// mechanism, and it is why the control above is refused while these are not.
    /// </para>
    /// <para>
    /// Both of these are reachable before any account exists, which is the point: a first-time
    /// visitor's very first request to this site is one of them. <c>assertion-options</c> mints a
    /// challenge without reading anything, so it is asserted in full; <c>registration-options</c>
    /// reads the account's existing credentials and therefore needs the database this host does not
    /// have, so it is asserted on the only thing under test here — that it was not refused as forged.
    /// </para>
    /// </remarks>
    [Test]
    public async Task TheOptionsEndpointsAnswerAJsonPostWithNoToken()
    {
        await using var host = await Host.StartAsync();

        var assertion = await host.PostAsync("/account/passkey/assertion-options", content: null);
        var registration = await host.PostAsync(
            "/account/passkey/registration-options?name=probe", content: null);

        await Assert.That(assertion.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await assertion.Content.ReadAsStringAsync()).Contains("challenge");

        await Assert.That(registration.StatusCode).IsNotEqualTo(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// The sign-in POST reaches its handler rather than being refused as forged.
    /// </summary>
    /// <remarks>
    /// Asserted as "not 400", deliberately, because what is under test is the middleware and not the
    /// outcome of the ceremony: with no database behind it the handler cannot finish, and pinning a
    /// specific failure would be pinning the wrong thing. Against a real database this same request
    /// answers 401 — measured, on a host with Postgres behind it and migrations applied.
    /// </remarks>
    [Test]
    public async Task TheSignInPostIsNotRefusedAsForged()
    {
        await using var host = await Host.StartAsync();

        var response = await host.PostAsync("/account/passkey/sign-in", SignInBody);

        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.BadRequest);
    }

    /// <summary>The site, composed exactly as the deployable composes it, on a loopback port.</summary>
    private sealed class Host : IAsyncDisposable
    {
        /// <summary>Never connected to. <c>NpgsqlDataSource</c> is lazy and nothing here dials it.</summary>
        private const string ConnectionString = "Host=127.0.0.1;Port=1;Database=mui;Username=mui";

        private readonly WebApplication _app;
        private readonly HttpClient _client;

        private Host(WebApplication app, HttpClient client)
        {
            _app = app;
            _client = client;
        }

        public static async Task<Host> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Production,
            });

            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddMuiSite(builder.Configuration, ConnectionString);

            // The crawl loop is not under test and would spend the test dialling a database that is
            // not there. It is built never to fault the host, so this is noise rather than risk —
            // and removing exactly one descriptor leaves every other registration where it was.
            if (builder.Services.FirstOrDefault(d =>
                    d.ImplementationType?.Name == "CrawlerService") is { } crawler)
            {
                builder.Services.Remove(crawler);
            }

            var app = builder.Build();
            app.UseMuiSite(ConnectionString);

            await app.StartAsync();

            var address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.First();

            return new Host(
                app,
                new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
                {
                    BaseAddress = new Uri(address),
                });
        }

        public Task<HttpResponseMessage> PostAsync(string path, string? content) =>
            _client.PostAsync(
                path,
                content is null
                    ? null
                    : new StringContent(content, Encoding.UTF8, "application/json"));

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
