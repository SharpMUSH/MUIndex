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
/// <b>§8.2 makes passkeys the only way in</b> — if these refuse a request, nobody can sign in or
/// claim anything. Nothing else tested them: sign-in suites stub it, and composition tests stop at
/// the graph. These drive the real routes through the real pipeline
/// (<see cref="SiteComposition.AddMuiSite"/>/<see cref="SiteComposition.UseMuiSite"/>) with no
/// database behind them — <c>assertion-options</c> answers in full without storage, and the other
/// two are asserted on what the middleware did rather than how the handler ended. Against real
/// Postgres all three answer 200, 200 and 401.
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
    /// unless something on the same host <em>is</em> blocked by the same middleware.
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
    /// Anti-forgery metadata is only added when a minimal API binds <em>form</em> data; these bind
    /// JSON or a query string, so the middleware passes them through untouched — unlike the control
    /// above. Both are reachable before any account exists: <c>assertion-options</c> mints a
    /// challenge without reading anything, so it's asserted in full; <c>registration-options</c>
    /// needs the database this host doesn't have, so it's only asserted as not refused as forged.
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
    /// Asserted as "not 400" rather than a specific success: with no database the handler can't
    /// finish. Against real Postgres this same request answers 401.
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
