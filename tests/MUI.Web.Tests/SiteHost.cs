using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;


namespace MUI.Web.Tests;

/// <summary>
/// The <em>site</em> — routed pages and the middleware in front of them — running for real on a
/// loopback port.
/// </summary>
/// <remarks>
/// <see cref="Api.ApiHost"/>'s counterpart: some of what this project promises is HTTP rather than
/// markup, and a claim about a status line can't be read off a rendered component. Calls
/// <see cref="SiteComposition.AddMuiSite"/>/<see cref="SiteComposition.UseMuiSite"/> rather than
/// booting <c>Program</c>, so a test can vary the composition without an environment variable — but
/// calls them rather than restating them, so a status-line assertion here is only worth something if
/// the middleware order under it is the deployed one. Composed with a null connection string by
/// default (the demo-fixture half); a caller that passes one gets the database-backed composition.
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

    /// <param name="measured">
    /// Whether the site should believe a catalogue is configured — <see cref="Fixtures.FixtureGameQueries"/>
    /// still answers every read either way. Several surfaces render differently over the demo
    /// fixture depending on this. Set after <see cref="SiteComposition.AddMuiSite"/>, which
    /// registers the marker itself from the connection string.
    /// </param>
    /// <param name="clock">
    /// What the site should believe the time is. The fixture's facts are stamped at a fixed instant,
    /// so a test asserting on "4m ago" must pin the other end of that subtraction.
    /// </param>
    /// <param name="connectionString">
    /// <c>null</c> for the demo fixture (the default). A non-null value composes the real database
    /// graph instead, the same way <c>Program</c> branches — and turns off the sign-in stub below
    /// (a real one is mapped once there's a database) and the <paramref name="measured"/> override
    /// (the real composition already registers <see cref="MUI.Web.Data.CatalogueSource"/> itself).
    /// </param>
    public static async Task<SiteHost> StartAsync(
        Dictionary<string, string?>? settings = null,
        Action<IServiceCollection>? services = null,
        bool measured = false,
        TimeProvider? clock = null,
        string? connectionString = null)
    {
        // Named for the web project, so UseStaticWebAssets below finds that project's manifest.
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(SiteComposition).Assembly.GetName().Name,
        });

        // Without this the site's stylesheet, icons and manifest 404 here (the test host's content
        // root is the test project's output directory). CreateSlimBuilder doesn't wire it up.
        builder.WebHost.UseStaticWebAssets();

        builder.Logging.ClearProviders();
        if (settings is { Count: > 0 })
        {
            builder.Configuration.AddInMemoryCollection(settings);
        }

        // Before AddMuiSite, because that is the order a real host composes in: whatever a database
        // would have registered is chosen first, and the site's own graph fills in the rest.
        services?.Invoke(builder.Services);

        // The site's own registrations — the fixture catalogue, the clock, the demo marker and the
        // read API — through the one call Program makes.
        builder.Services.AddMuiSite(builder.Configuration, connectionString);

        if (measured && connectionString is null)
        {
            builder.Services.AddSingleton(new MUI.Web.Data.CatalogueSource(IsMeasured: true));
        }

        if (clock is not null)
        {
            builder.Services.AddSingleton(clock);
        }

        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();

        // The pipeline and routes, through the one call Program makes — including the read API, so
        // an unmatched /api route is a 404 and not swallowed by the not-found page.
        app.UseMuiSite(connectionString);

        if (connectionString is null)
        {
            // Standing in for MUI.Web.Accounts, which UseMuiSite leaves unmapped with no connection
            // string. The status line is what matters: Passkeys.cs answers a bad sign-in with a
            // bodiless 401, and passkey.js reads the body as the error text — any pipeline step that
            // filled it with HTML would show a reader a page of markup instead of an error.
            app.MapPost(SignInPath, () => Results.Unauthorized()).DisableAntiforgery();
        }

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
