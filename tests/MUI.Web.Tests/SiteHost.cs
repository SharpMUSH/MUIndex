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
/// <para>
/// <see cref="Api.ApiHost"/>'s counterpart, and it exists for the same reason: some of what this
/// project promises is HTTP rather than markup. "A slug a game used to have redirects to its page,
/// permanently" cannot be read off a rendered component — <c>Render</c> invokes a component with a
/// stubbed navigation manager and no <c>HttpContext</c>, which is the right tool for a claim about
/// what a browser is handed and no tool at all for a claim about a status line.
/// </para>
/// <para>
/// It calls <see cref="SiteComposition.AddMuiSite"/> and <see cref="SiteComposition.UseMuiSite"/>
/// rather than booting <c>Program</c>, so a test can vary what a deployment would have decided — the
/// catalogue behind it, the configured aliases — without an environment variable and without the
/// solution taking a test-hosting package for one file. <b>It calls them rather than restating
/// them</b>: this host assembled the graph and the pipeline by hand until <c>SiteComposition</c>
/// existed, and a harness that reassembles a pipeline is a harness that can quietly stop describing
/// the site. Every assertion here about a status line is worth something only if the middleware
/// order under it is the deployed one.
/// </para>
/// <para>
/// Composed with a null connection string by default, which is the demo-fixture half: no database,
/// so no accounts and no submission endpoint, and the page says what it is showing was not measured
/// — exactly what a reader of the real site with no <c>MUI_POSTGRES</c> sees. A caller that passes
/// one gets the other composition, for the tests that need a real one running behind real HTTP.
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

    /// <param name="measured">
    /// Whether the site should believe a catalogue is configured. <see cref="Fixtures.FixtureGameQueries"/>
    /// still answers every read — this asks what the site renders when the numbers are real, not
    /// what a real database holds, which is the same knob <c>Render.PageAsync</c> already has and
    /// exists for the same reason: several surfaces are deliberately different over the demo
    /// fixture, and a suite that can only start one of the two worlds can only test one of them.
    /// It goes on after <see cref="SiteComposition.AddMuiSite"/> because that call registers the
    /// marker itself, from the connection string it was handed.
    /// </param>
    /// <param name="clock">
    /// What the site should believe the time is. The fixture stamps its facts at a fixed instant on
    /// purpose — a demo on which nothing is ever old would not show the state worth showing — so
    /// every age it renders is measured against the wall clock and grows by a day each day. A test
    /// asserting on "4m ago" has to pin the other end of that subtraction or it is asserting on the
    /// date it was written.
    /// </param>
    /// <param name="connectionString">
    /// <c>null</c> for the demo fixture (the default, and every existing caller). A non-null value
    /// composes the real database graph instead — <see cref="AddMuiSite"/> and
    /// <see cref="UseMuiSite"/> both branch on this the same way <c>Program</c> does, so a test asking
    /// for the database-backed composition gets the composition rather than a restatement of it.
    /// Passing one turns off the two things that only make sense against the fixture: the sign-in
    /// stub below, because <see cref="UseMuiSite"/> maps a real one when there is a database, and the
    /// <paramref name="measured"/> override, because the real composition already registers
    /// <see cref="MUI.Web.Data.CatalogueSource"/> from the connection string itself.
    /// </param>
    public static async Task<SiteHost> StartAsync(
        Dictionary<string, string?>? settings = null,
        Action<IServiceCollection>? services = null,
        bool measured = false,
        TimeProvider? clock = null,
        string? connectionString = null)
    {
        // Named for the web project, so UseStaticWebAssets below finds the manifest that project
        // wrote into this one's output — see the call for why that matters.
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(SiteComposition).Assembly.GetName().Name,
        });

        // wwwroot. Without this the site's own stylesheet, its icons and its manifest are 404s here
        // and nowhere else, because the content root of a test host is the test project's output
        // directory — so the one thing that would notice a renamed or unshipped asset could not see
        // any of them. CreateSlimBuilder does not wire it up; that is what makes it slim.
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

        // The pipeline and the routes, through the one call Program makes. That includes the read
        // API, because the deployable is one process and a rule about how *pages* answer must not
        // reach the endpoints beside them: an unmatched route under /api is a 404 nobody wrote a
        // body for, which is exactly the shape a not-found page would have swallowed.
        app.UseMuiSite(connectionString);

        if (connectionString is null)
        {
            // Standing in for MUI.Web.Accounts, which UseMuiSite leaves unmapped with no connection
            // string: passkeys need a database and this host has none. The status line is what
            // matters — Passkeys.cs answers a bad sign-in with Results.Unauthorized(), a bodiless
            // 401, and passkey.js renders `throw new Error(await response.text())` straight into the
            // sign-in status line. If anything in the pipeline fills that body with HTML, a reader
            // who mistyped their passkey is shown a page of markup. With a database configured
            // UseMuiSite maps a real one at this same path, so the stub would only ever shadow it.
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
