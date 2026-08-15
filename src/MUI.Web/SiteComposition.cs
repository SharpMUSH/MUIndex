using MUI.Catalog;
using MUI.Crawler;
using MUI.Web.Accounts;
using MUI.Web.Api;
using MUI.Web.Components;
using MUI.Web.Data;
using MUI.Web.Fixtures;

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
            });

            // Claiming needs a database: an account, a passkey and a claim are all rows (spec §8).
            services.AddMuiAccounts(configuration);
        }
        else
        {
            // Against the demo fixture the sign-in and claim surfaces are simply absent rather than
            // present and broken — half a claim flow over invented games would be a worse answer
            // than none.
            services.AddSingleton<FixtureGameQueries>();
            services.AddSingleton<IGameQueries>(s => s.GetRequiredService<FixtureGameQueries>());
            services.AddSingleton<IAvailabilityHistory>(s => s.GetRequiredService<FixtureGameQueries>());
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

        app.UseStaticFiles();

        // Razor Components register anti-forgery metadata on every endpoint, so the middleware has
        // to be present even though this site has no POST form yet. The facet panel is a GET form
        // deliberately — a filter is a bookmarkable question, not a state change — so nothing here
        // is token-protected.
        app.UseAntiforgery();

        if (connectionString is not null)
        {
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapMuiAccounts();
        }

        app.MapRazorComponents<App>();
        app.MapMuiApi();

        return app;
    }
}
