using Microsoft.AspNetCore.Builder;
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

        // The same pipeline Program builds, in the same order: an error response nobody wrote a
        // body for is re-executed against the site's own not-found page, and a slug a game used to
        // have is redirected before the route that would have said there was nothing at it.
        app.UseStatusCodePagesWithReExecute("/not-found");
        app.UseAntiforgery();
        app.UseFormerSlugRedirects();
        app.MapRazorComponents<App>();

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
