using MUI.Catalog;
using MUI.Crawler;
using MUI.Web.Accounts;
using MUI.Web.Api;
using MUI.Web.Components;
using MUI.Web.Data;
using MUI.Web.Fixtures;
using MUI.Web.Icons;
using MUI.Web.Submissions;

namespace MUI.Web;

/// <summary>
/// Everything the deployable is made of, as two calls <c>Program</c> makes and a test can make too.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists so that the composition has one spelling.</b> The graph used to live in
/// <c>Program.cs</c>'s top-level statements, where nothing but the running site could reach it — so
/// a test of the composition had to <em>restate</em> it, and a restatement is a second copy that
/// agrees with the first only until somebody edits one of them. The failure that motivates the whole
/// of <c>CompositionTests</c> is a registration nobody noticed was wrong; a harness that mirrored
/// <c>Program</c> would have gone on passing through exactly that edit.
/// </para>
/// <para>
/// <c>Program.cs</c> keeps what has side effects — reading the connection string, applying
/// migrations, saying in the log which of the two worlds it is in — because those are things a
/// process does on the way up rather than parts of the graph.
/// </para>
/// </remarks>
public static class SiteComposition
{
    /// <summary>
    /// Registers the whole site, with a database behind it or on the demo fixture.
    /// </summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="configuration">The host's configuration.</param>
    /// <param name="connectionString">
    /// A PostgreSQL connection string, or null for the demo fixture. Null is not a fallback so much
    /// as a confession: a directory whose whole claim is that its data is measured must never
    /// quietly present invented data as though it were real, so the demo path is opt-in by absence,
    /// announced in the log, and marked on every page through <see cref="CatalogueSource"/>.
    /// </param>
    public static IServiceCollection AddMuiSite(
        this IServiceCollection services,
        IConfiguration configuration,
        string? connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddRazorComponents();

        // The read API (spec §10) reads through the same IGameQueries the pages do, so the two
        // surfaces cannot disagree about a fact. What it adds of its own — the dataset licence, the
        // slug aliases and the attribution list — is configuration, because none of it is a
        // measurement.
        services.AddMuiApi(configuration);

        // Ages are relative to a clock, and a clock is a dependency like any other — the plain
        // surface and the rendered page must not each reach for DateTimeOffset.UtcNow and disagree
        // by a tick. Registered before the two worlds below, because both want it.
        services.AddSingleton(TimeProvider.System);

        if (connectionString is not null)
        {
            services.AddPostgresCatalogue(connectionString);
            services.AddMuiCrawler(connectionString, configure =>
            {
                // MUI.Web already applies migrations during startup; the hosted crawler should not
                // repeat them when it takes the lease.
                configure.ApplyMigrations = false;

                // Whether this replica crawls at all, and what it knows on day one. The advisory
                // lock already guarantees one crawler across N replicas, so MUI_CRAWL_ENABLED=false
                // is a deliberate choice about where the crawl runs rather than a safety net.
                configure.Apply(configuration);
            });

            // Claiming needs a database: an account, a passkey and a claim are all rows (spec §8).
            services.AddMuiAccounts(configuration);

            // Icons: the cache, the one HTTP client this codebase has, and the service that keeps it
            // current. After AddMuiCrawler, which owns §7.2's address gate and the contact address
            // the client announces — both of which this reuses rather than restating.
            services.AddMuiIcons();
        }
        else
        {
            // Against the demo fixture the sign-in and claim surfaces are simply absent rather than
            // present and broken — half a claim flow over invented games would be a worse answer
            // than none.
            services.AddSingleton<FixtureGameQueries>();
            services.AddSingleton<IGameQueries>(s => s.GetRequiredService<FixtureGameQueries>());
            services.AddSingleton<IAvailabilityHistory>(s => s.GetRequiredService<FixtureGameQueries>());
            services.AddSingleton<IPresenceSeries, FixturePresenceSeries>();

            // The series stays empty and the trend does not, and that is the banner rule rather than
            // an inconsistency: §10's JSON has nowhere to say the games are invented, and a page has
            // the banner at the top of it. See IPresenceTrends.
            services.AddSingleton<IPresenceTrends, FixturePresenceTrends>();

            // No crawler, so no pulse — and no invented one. The strip renders nothing at all rather
            // than a fabricated heartbeat, which on the one page whose whole argument is "this was
            // measured" would be the worst possible thing to make up.
            services.AddSingleton<ICrawlerPulse, NoCrawlerPulse>();
        }

        services.AddSingleton(new CatalogueSource(connectionString is not null));

        return services;
    }

    /// <summary>
    /// The middleware and the routes, in the order they have to be in.
    /// </summary>
    /// <remarks>
    /// The order is part of the composition and not a detail of it, so it lives here with the rest of
    /// it rather than in a file only the running process reads.
    /// </remarks>
    public static WebApplication UseMuiSite(this WebApplication app, string? connectionString)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Before anything reads a client address. Off unless a deployment says how many proxies are
        // in front of it (Submissions:TrustedProxyHops), because a forwarded header nobody counted is
        // a header anybody may write — and the one thing that reads a client address here is a rate
        // limit.
        app.UseSubmitterAddress();

        // A reader who mistyped a URL, and a crawler indexing one, both got a 404 with an empty body:
        // the <NotFound> fragment inside <Router> is never rendered under static server rendering, so
        // the site's own "no game here" paragraph was dead copy. This answers those with the page —
        // and only those. The scoping is NotFoundPage's own, because a rule about how a page says
        // "there is nothing here" must not reach the API or the account endpoints beside it.
        app.UseMuiNotFoundPage();

        // MapStaticAssets rather than UseStaticFiles: it is what publishes the fingerprinted address
        // App.razor links the stylesheet by, and it serves that address as immutable while the plain
        // one keeps revalidating. See the comment there for the deploy this is the fix for.
        //
        // The manifest is named rather than inferred, because the argumentless overload derives the
        // name from the *entry* assembly — which is this site when it is the site, and the test host
        // when a test builds this very pipeline to assert what it maps. Seven of those tests went
        // from passing to throwing on a manifest the test project has no reason to have.
        app.MapStaticAssets("MUI.Web.staticwebassets.endpoints.json");

        app.UseMuiAntiforgeryAfterAuthentication(withAccounts: connectionString is not null);

        if (connectionString is not null)
        {
            app.MapMuiAccounts();

            // Submitting needs a crawl registry to write an address into, and the registry is
            // registered alongside the database. Against the demo fixture the form is absent rather
            // than present and silently doing nothing, which is the same choice the claim surface
            // makes.
            app.MapMuiSubmissions();

            // The icon we hold, served from this origin so a reader's address is never spent on a
            // third-party host. Inside the guard because there is nothing to serve without a
            // database — the demo fixture has no icons and renders no element for them.
            app.MapMuiIcons();
        }

        // §5.7, and before the route that would answer with "not found": a slug this game used to
        // have is a URL somebody is still holding, and it redirects to the page it has now —
        // permanently, and for an archived game exactly as for a live one.
        app.UseFormerSlugRedirects();

        // §11's contact address, and it is mapped before the pages because the crawler has already
        // published it: whatever else moves on this site, the URL a dialled admin was handed has to
        // keep landing on the part of /about that tells them how to make us stop.
        app.MapMuiCrawlerContact();

        app.MapRazorComponents<App>();
        app.MapMuiApi();

        // robots.txt and sitemap.xml. After the pages, because they are about the pages — and the
        // sitemap reads the catalogue through the same IGameQueries every surface reads, so it
        // cannot advertise a game the site would not render.
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
    /// <para>
    /// An anti-forgery token issued to a signed-in operator carries their identity, and the
    /// middleware compares it against <c>HttpContext.User</c> — so validating <em>before</em> the
    /// authentication middleware has run compares a token minted for somebody against nobody, and
    /// every owner's form post is rejected as forged. The public surfaces never notice: a filter is a
    /// GET form deliberately, because a bookmarkable question is not a state change, and the
    /// submission form and the on-demand claim check post with no signed-in identity to disagree
    /// with. So the failure is total for the one surface that writes and invisible everywhere else.
    /// </para>
    /// <para>
    /// <b>It is a method rather than three lines inline because the test that proves it matters used
    /// to build its own copy of them.</b> A harness that restates an ordering asserts its own
    /// ordering and stops describing the site's the moment the site's moves — and it did move, twice,
    /// while the pipeline was being pulled out of <c>Program</c>. <c>OwnerEndpointTests</c> calls this
    /// for its correct-order pipeline and hand-builds only the wrong one, so reordering these three
    /// lines fails an owner's form post in a test rather than in production.
    /// </para>
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
