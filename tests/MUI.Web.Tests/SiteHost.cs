using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using MUI.Catalog;
using MUI.Web.Api;
using MUI.Web.Components;
using MUI.Web.Data;
using MUI.Web.Fixtures;

namespace MUI.Web.Tests;

/// <summary>
/// The <em>site</em> — routed pages and the middleware in front of them — running for real on a
/// loopback port.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Api.ApiHost"/>'s counterpart, and it exists for the same reason: some of what this
/// project promises is HTTP rather than markup. "A slug a game used to have redirects to its page,
/// permanently" cannot be read off a rendered component — <c>Render</c> invokes a component with a
/// stubbed navigation manager and no <c>HttpContext</c>, which is the right tool for a claim about
/// what a browser is handed and no tool at all for a claim about a status line.
/// </para>
/// <para>
/// It composes the same way <c>Program</c> does rather than booting it, so a test can vary what a
/// deployment would have decided — the catalogue behind it, the configured aliases — without an
/// environment variable and without the solution taking a test-hosting package for one file.
/// </para>
/// </remarks>
public sealed class SiteHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private SiteHost(WebApplication app, HttpClient client)
    {
        _app = app;
        Client = client;
    }

    public HttpClient Client { get; }

    /// <summary>The route <c>passkey.js</c> posts a credential to, as <c>Passkeys.cs</c> maps it.</summary>
    public const string SignInPath = "/account/passkey/sign-in";

    public static Task<SiteHost> StartAsync(Action<IServiceCollection> services) =>
        StartAsync(null, services);

    public static async Task<SiteHost> StartAsync(
        Dictionary<string, string?>? settings = null,
        Action<IServiceCollection>? services = null)
    {
        var builder = WebApplication.CreateSlimBuilder();

        builder.Logging.ClearProviders();
        if (settings is { Count: > 0 })
        {
            builder.Configuration.AddInMemoryCollection(settings);
        }

        builder.Services.AddRazorComponents();

        var fixture = new FixtureGameQueries();
        builder.Services.AddSingleton<IGameQueries>(fixture);
        builder.Services.AddSingleton<IAvailabilityHistory>(fixture);
        builder.Services.AddSingleton(TimeProvider.System);

        // The demo fixture, and the page says so — which is what a reader of this host sees, exactly
        // as a reader of the site with no database does.
        builder.Services.AddSingleton(new CatalogueSource(IsMeasured: false));

        services?.Invoke(builder.Services);
        builder.Services.AddMuiApi(builder.Configuration);

        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();

        // The same pipeline Program builds, in the same order, through the same calls — a harness
        // that reassembled it by hand is a harness that can quietly stop describing the site.
        app.UseMuiNotFoundPage();
        app.UseAntiforgery();
        app.UseFormerSlugRedirects();
        app.MapRazorComponents<App>();

        // The read API, because the deployable is one process and a rule about how *pages* answer
        // must not reach the endpoints beside them. An unmatched route under /api is a 404 nobody
        // wrote a body for, which is exactly the shape a not-found page would have swallowed.
        app.MapMuiApi();

        // Standing in for MUI.Web.Accounts, which cannot be mapped here: passkeys need a database
        // and this host has none. The status line is what matters — Passkeys.cs answers a bad
        // sign-in with Results.Unauthorized(), a bodiless 401, and passkey.js renders
        // `throw new Error(await response.text())` straight into the sign-in status line. If
        // anything in the pipeline fills that body with HTML, a reader who mistyped their passkey
        // is shown a page of markup.
        app.MapPost(SignInPath, () => Results.Unauthorized()).DisableAntiforgery();

        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        // Redirects are not followed: the 301 is the assertion, and a client that quietly followed
        // it would report a 200 and prove nothing.
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(address),
        };

        return new SiteHost(app, client);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
