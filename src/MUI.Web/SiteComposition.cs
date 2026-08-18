using Microsoft.Extensions.Localization;

using MUI.Catalog;
using MUI.Crawler;
using MUI.Web.Accounts;
using MUI.Web.Api;
using MUI.Web.Components;
using MUI.Web.Data;
using MUI.Web.Fixtures;
using MUI.Web.Icons;
using MUI.Web.Localization;
using MUI.Web.Submissions;
using MUI.Web.Theme;

namespace MUI.Web;

/// <summary>
/// Everything the deployable is made of, as two calls <c>Program</c> makes and a test can make too.
/// </summary>
/// <remarks>
/// This exists so the composition has one spelling: previously living in <c>Program.cs</c>'s
/// top-level statements, a composition test had to restate the graph rather than resolve the same
/// one — a second copy that only agreed with the first until somebody edited one of them.
/// <c>Program.cs</c> keeps what has side effects (reading the connection string, applying
/// migrations, logging) since those are things a process does on the way up, not parts of the graph.
/// </remarks>
public static class SiteComposition
{
    /// <summary>
    /// Registers the whole site, with a database behind it or on the demo fixture.
    /// </summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="configuration">The host's configuration.</param>
    /// <param name="connectionString">
    /// A PostgreSQL connection string, or null for the demo fixture. Never presented as real data: the
    /// demo path is announced in the log and marked on every page through <see cref="CatalogueSource"/>.
    /// </param>
    public static IServiceCollection AddMuiSite(
        this IServiceCollection services,
        IConfiguration configuration,
        string? connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddRazorComponents();

        // The chrome's own words, before anything that renders them.
        services.AddMuiLocalization();

        // The read API (spec §10) reads through the same IGameQueries the pages do, so the two
        // surfaces cannot disagree about a fact.
        services.AddMuiApi(configuration);

        // A shared clock so the plain surface and the rendered page can't reach for
        // DateTimeOffset.UtcNow and disagree by a tick. Registered before the two worlds below.
        services.AddSingleton(TimeProvider.System);

        if (connectionString is not null)
        {
            services.AddPostgresCatalogue(connectionString);
            services.AddMuiCrawler(connectionString, configure =>
            {
                // MUI.Web already applies migrations during startup; the hosted crawler should not
                // repeat them when it takes the lease.
                configure.ApplyMigrations = false;
                configure.Apply(configuration);
            });

            // Claiming needs a database: an account, a passkey and a claim are all rows (spec §8).
            services.AddMuiAccounts(configuration);

            // After AddMuiCrawler, which owns §7.2's address gate and the contact address the icon
            // client announces — both reused rather than restated.
            services.AddMuiIcons();
        }
        else
        {
            // Against the demo fixture the sign-in and claim surfaces are absent, not present and
            // broken — half a claim flow over invented games would be a worse answer than none.
            services.AddSingleton<FixtureGameQueries>();
            services.AddSingleton<IGameQueries>(s => s.GetRequiredService<FixtureGameQueries>());
            services.AddSingleton<IAvailabilityHistory>(s => s.GetRequiredService<FixtureGameQueries>());
            services.AddSingleton<IPresenceSeries, FixturePresenceSeries>();

            services.AddSingleton<IPresenceTrends, FixturePresenceTrends>();

            // No crawler, so no pulse — and no invented one. The strip renders nothing rather than a
            // fabricated heartbeat.
            services.AddSingleton<ICrawlerPulse, NoCrawlerPulse>();
        }

        services.AddSingleton(new CatalogueSource(connectionString is not null));

        // /health for the reverse proxy's routing decision (see HealthEndpoint for why it checks the
        // database and not the crawler).
        services.AddMuiHealth(connectionString);

        return services;
    }

    /// <summary>
    /// The resource set the chrome's strings are read from.
    /// </summary>
    /// <remarks>
    /// <c>AddLocalization</c> over a <c>Resources</c> folder, resolved through a marker class, with
    /// the SDK compiling one satellite assembly per culture. Values are ICU patterns, not
    /// composite-format strings. See <see cref="Localization.Messages"/>.
    /// </remarks>
    public static IServiceCollection AddMuiLocalization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddLocalization(options => options.ResourcesPath = "Resources");

        return services;
    }

    /// <summary>
    /// The middleware and the routes, in the order they have to be in.
    /// </summary>
    /// <remarks>The order is part of the composition, not a detail of it, so it lives here rather than in a file only the running process reads.</remarks>
    public static WebApplication UseMuiSite(this WebApplication app, string? connectionString)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Before anything reads a client address (the rate limit). Off unless a deployment says how
        // many proxies are in front of it (Submissions:TrustedProxyHops).
        app.UseSubmitterAddress();

        // Before routing: the locale is a path segment every @page directive omits, and this
        // middleware moves the prefix into PathBase so one route table serves every language. Also
        // before the not-found page, so a mistyped URL inside a locale stays in that locale.
        app.UseMuiLocale();

        // Explicit and load-bearing: an app with no UseRouting call gets one inserted at the very top
        // of the pipeline, resolving the endpoint before the middleware above rewrites the path —
        // which 404'd every localized URL.
        app.UseRouting();

        // /health, before anything that reads HttpContext.User: the reverse proxy dials this
        // unauthenticated on every routing decision.
        app.MapMuiHealth();

        // <NotFound> inside <Router> is never rendered under static server rendering, so this answers
        // page 404s directly. Scoped to pages only — a rule about how a page says "nothing here" must
        // not reach the API or account endpoints.
        app.UseMuiNotFoundPage();

        // MapStaticAssets rather than UseStaticFiles: it publishes the fingerprinted address
        // App.razor links the stylesheet by, serving it immutable while the plain one revalidates.
        // The manifest is named explicitly because the argumentless overload derives the name from
        // the *entry* assembly — the test host when a test builds this pipeline, not the site.
        app.MapStaticAssets("MUI.Web.staticwebassets.endpoints.json");

        app.UseMuiAntiforgeryAfterAuthentication(withAccounts: connectionString is not null);

        if (connectionString is not null)
        {
            app.MapMuiAccounts();

            // Registry is registered alongside the database. Against the demo fixture the form is
            // absent, not present and silently doing nothing — same choice the claim surface makes.
            app.MapMuiSubmissions();

            // Served from this origin so a reader's address is never spent on a third-party host.
            app.MapMuiIcons();
        }

        // Outside the guard above: it only writes a cookie, so a demo-deployment reader has the same
        // experience as a real one.
        app.MapMuiTheme();
        app.MapMuiLocale();

        // §5.7, before the route that would answer "not found": a URL somebody is still holding
        // redirects permanently to the page it has now, archived game or not.
        app.UseFormerSlugRedirects();

        // A no-script facet panel submits every control, empty ones included; this canonicalizes the
        // URL before the pages, so a redirected request never costs a catalogue read.
        app.UseCanonicalListingUrls();

        // §11's contact address, mapped before the pages: the crawler has already published this URL
        // to dialled admins.
        app.MapMuiCrawlerContact();

        app.MapRazorComponents<App>();
        app.MapMuiApi();

        // robots.txt and sitemap.xml, after the pages since they're about the pages.
        app.MapMuiSiteIndex();

        return app;
    }

    /// <summary>
    /// Authentication, then authorisation, then anti-forgery — three lines whose <b>order is the
    /// whole point</b>.
    /// </summary>
    /// <param name="app">The pipeline.</param>
    /// <param name="withAccounts">
    /// Whether this deployment has a database behind it. With none there are no accounts to
    /// authenticate, and the anti-forgery middleware still has to be present because Razor Components
    /// puts anti-forgery metadata on every endpoint.
    /// </param>
    /// <remarks>
    /// An anti-forgery token issued to a signed-in operator carries their identity, and the middleware
    /// compares it against <c>HttpContext.User</c> — validating <em>before</em> authentication runs
    /// compares a token minted for somebody against nobody, rejecting every owner's form post as
    /// forged. Public surfaces never notice, since they post with no signed-in identity to disagree
    /// with. A method rather than three lines inline so <c>OwnerEndpointTests</c> can call this for
    /// its correct-order pipeline and hand-build only the wrong one for comparison.
    /// </remarks>
    public static WebApplication UseMuiAntiforgeryAfterAuthentication(
        this WebApplication app,
        bool withAccounts)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (withAccounts)
        {
            app.UseAuthentication();
            app.UseAuthorization();
        }

        app.UseAntiforgery();

        return app;
    }
}
