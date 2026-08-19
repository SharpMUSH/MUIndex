using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawl;
using MUI.Crawler;
using MUI.Web;
using MUI.Web.Data;
using MUI.Web.Fixtures;
using MUI.Web.Icons;

namespace MUI.Web.Tests;

/// <summary>
/// The graph <c>Program</c> builds, resolved.
/// </summary>
/// <remarks>
/// The site has two compositions and only one was ever exercised: <c>mui-crawl</c> constructs the
/// crawl loop by hand, so its path has tests, while the deployed site assembles the same objects
/// through DI, where a missing registration is not a compile error but a null in an optional
/// parameter or an exception on first request. Every assertion below was a live hole when written.
/// </remarks>
public class CompositionTests
{
    /// <summary>
    /// A connection string is a string. Nothing here opens a connection — <c>NpgsqlDataSource</c> is
    /// lazy — so the graph can be built and validated without a database.
    /// </summary>
    private const string ConnectionString = "Host=127.0.0.1;Port=1;Database=mui;Username=mui";

    /// <summary>
    /// The dashboard could not list a single claim, because nothing registered the store it reads.
    /// </summary>
    /// <remarks>
    /// <c>Account.razor</c> asks the container for <see cref="IClaimStore"/> and treats a null as
    /// "this site has no database" — the page rendered, said nothing was claimed, and was wrong for
    /// every operator. A service-located dependency fails silently, so this is asserted at the
    /// composition root.
    /// </remarks>
    [Test]
    public async Task TheDashboardCanReachTheClaimsItListsAndTheEndpointThatChecksThem()
    {
        await using var site = Site();
        var provider = site.Services;

        await Assert.That(provider.GetService<IClaimStore>()).IsNotNull();
        await Assert.That(provider.GetService<ClaimService>()).IsNotNull();
    }

    /// <summary>
    /// The crawl loop can settle a claim, which is the only thing that ever verifies one.
    /// </summary>
    /// <remarks>
    /// <see cref="CrawlCycle"/> takes its <see cref="ClaimService"/> as an optional parameter, and the
    /// deployed site registered no <c>ClaimService</c> for the container to supply — so the parameter
    /// took its default and no probe ever verified a claim in production. Survived because
    /// <c>mui-crawl</c> passes the service by hand and its tests construct the cycle the same way.
    /// </remarks>
    [Test]
    public async Task TheHostedCrawlerIsGivenTheServiceThatVerifiesAClaim()
    {
        await using var site = Site();
        var provider = site.Services;

        var cycle = provider.GetRequiredService<CrawlCycle>();

        await Assert.That(ClaimsOf(cycle)).IsNotNull();
    }

    /// <summary>
    /// The crawl loop is given the guard that lets the catalogue say we stopped watching.
    /// </summary>
    /// <remarks>
    /// Same shape as the claim service above: <see cref="CrawlGapGuard"/> is an optional parameter on
    /// <c>CrawlerService</c>, and a guard that was never wired logs identically to one with nothing to
    /// do, so there is no symptom to notice. Its own <c>ICrawlCycles</c> is asserted for the same
    /// reason one level down.
    /// </remarks>
    [Test]
    public async Task TheHostedCrawlerIsGivenTheGuardThatClosesACrawlGap()
    {
        await using var site = Site();

        var crawler = site.Services.GetServices<IHostedService>().OfType<CrawlerService>().Single();
        var guard = Collaborator<CrawlGapGuard>(crawler);

        await Assert.That(guard).IsNotNull();
        await Assert.That(Collaborator<ICrawlCycles>(guard!)).IsNotNull();
    }

    /// <summary>
    /// The claim service is not scoped, because the thing that needs it most is a singleton.
    /// </summary>
    /// <remarks>
    /// A background service outlives every request scope, so a scoped <see cref="ClaimService"/> is
    /// one <see cref="CrawlCycle"/> can never be given cleanly — with scope validation on, the wrong
    /// wiring is a startup failure rather than a captive dependency.
    /// </remarks>
    [Test]
    public async Task TheGraphSurvivesScopeValidation()
    {
        await using var site = Site();
        var provider = site.Services;

        await Assert.That(provider.GetRequiredService<CrawlCycle>()).IsNotNull();

        await using var scope = provider.CreateAsyncScope();

        await Assert.That(scope.ServiceProvider.GetRequiredService<ClaimService>()).IsNotNull();
    }

    /// <summary>Everything the owner write path needs, from a request scope.</summary>
    [Test]
    public async Task TheOwnerWritePathResolves()
    {
        await using var site = Site();
        var provider = site.Services;
        await using var scope = provider.CreateAsyncScope();

        await Assert.That(scope.ServiceProvider.GetService<IGameFieldStore>()).IsNotNull();
        await Assert.That(scope.ServiceProvider.GetService<IFieldReconciler>()).IsNotNull();

        // Resolving OwnerEnrichment itself: the minter that applies an owner's NAME (§5.7) is
        // optional on the type but required from this composition — asked for softly it would come
        // back null on a broken graph and owners would silently rename games while URLs stayed put.
        await Assert.That(scope.ServiceProvider.GetRequiredService<OwnerEnrichment>()).IsNotNull();
        await Assert.That(scope.ServiceProvider.GetRequiredService<SlugMinter>()).IsNotNull();
    }

    /// <summary>
    /// The icon path, which reaches across three registrations that are made in three places.
    /// </summary>
    /// <remarks>
    /// <see cref="IconFetcher"/> is a typed client from <c>AddMuiIcons</c> and takes
    /// <c>IHostScopeGuard</c>/<c>ProbeOptions</c> from <c>AddMuiCrawler</c>, which must run first.
    /// Resolving it here makes that ordering a tested fact — a fetcher composed without the gate
    /// would reach an attacker-chosen URL with nothing standing in front of it (§7.2).
    /// </remarks>
    [Test]
    public async Task TheIconPathResolvesWithTheAddressGateBehindIt()
    {
        await using var site = Site();
        var provider = site.Services;
        await using var scope = provider.CreateAsyncScope();

        await Assert.That(scope.ServiceProvider.GetRequiredService<IconFetcher>()).IsNotNull();
        await Assert.That(scope.ServiceProvider.GetRequiredService<IIconStore>()).IsNotNull();
        await Assert.That(provider.GetRequiredService<IHostScopeGuard>()).IsNotNull();

        // And the refresher is actually hosted, not merely constructible — otherwise the cache stays
        // empty forever.
        await Assert.That(provider.GetServices<IHostedService>().OfType<IconRefresher>()).IsNotEmpty();
    }

    /// <summary>
    /// The same graph in Production, where nothing validates it for us.
    /// </summary>
    /// <remarks>
    /// The control, not the finding — it <b>passed on the parent commit</b>. Production leaves scope
    /// validation off, so the container resolves the scoped <c>ClaimService</c> from the root: a
    /// captive dependency, harmless for a stateless service over a pooled data source, and why claims
    /// verified in production while Development wouldn't start.
    /// </remarks>
    [Test]
    public async Task TheHostedCrawlerCanBeResolvedInProductionToo()
    {
        await using var site = Site(Environments.Production);

        await Assert.That(site.Services.GetRequiredService<CrawlCycle>()).IsNotNull();
    }

    /// <summary>
    /// The store the dashboard reads, asked for in the environment that can build a container.
    /// </summary>
    /// <remarks>
    /// Separated from the Development assertion: the lifetime mistake stops Development dead but
    /// leaves Production working, while this one is a plain missing registration present in both.
    /// </remarks>
    [Test]
    public async Task TheDashboardsClaimStoreIsMissingInEveryEnvironmentNotJustTheValidatedOne()
    {
        await using var site = Site(Environments.Production);

        await Assert.That(site.Services.GetService<IClaimStore>()).IsNotNull();
    }

    /// <summary>
    /// The crawl loop is handed a claim service in Production, where the container tolerates it.
    /// </summary>
    /// <remarks>
    /// A production site does settle claims, but only by resolving a scoped service from the root —
    /// permitted only because Production leaves scope validation off, so it's correct by accident.
    /// </remarks>
    [Test]
    public async Task TheProductionCrawlLoopDoesGetItsClaimService()
    {
        await using var site = Site(Environments.Production);

        await Assert.That(ClaimsOf(site.Services.GetRequiredService<CrawlCycle>())).IsNotNull();
    }

    /// <summary>
    /// The demo composition, which is a different graph and not a smaller one.
    /// </summary>
    /// <remarks>
    /// With no connection string, sign-in, crawling and claiming are absent rather than present and
    /// broken (§8) — a half-working claim flow over invented games would be worse than none.
    /// </remarks>
    [Test]
    public async Task TheDemoCompositionStandsUpAndAdmitsWhatItIs()
    {
        await using var site = Site(connectionString: null);
        var provider = site.Services;

        await Assert.That(provider.GetService<IGameQueries>()).IsTypeOf<FixtureGameQueries>();
        await Assert.That(provider.GetService<IAvailabilityHistory>()).IsNotNull();

        // Absent, not broken. A page asks the container whether claiming exists at all.
        await Assert.That(provider.GetService<ClaimService>()).IsNull();
        await Assert.That(provider.GetService<IClaimStore>()).IsNull();
        await Assert.That(provider.GetService<CrawlCycle>()).IsNull();

        await Assert.That(provider.GetRequiredService<CatalogueSource>().IsMeasured).IsFalse();
    }

    /// <summary>
    /// One availability store, however many names it answers to.
    /// </summary>
    /// <remarks>
    /// The crawler registers <c>IAvailabilityStore</c> once and exposes it under two interfaces; the
    /// web tier's own <c>AddSingleton</c> skipped the crawler's <c>TryAdd</c>, pointing the concrete
    /// type and <c>IReachableHistory</c> at a second instance. Harmless on a shared pool.
    /// </remarks>
    [Test]
    public async Task TheAvailabilityStoreIsOneObjectUnderEveryNameItHas()
    {
        await using var site = Site();
        var provider = site.Services;

        var concrete = provider.GetRequiredService<NpgsqlAvailabilityStore>();

        await Assert.That(provider.GetRequiredService<IAvailabilityStore>()).IsSameReferenceAs(concrete);
        await Assert.That(provider.GetRequiredService<IReachableHistory>()).IsSameReferenceAs(concrete);
    }

    /// <summary>
    /// §8.1's on-demand check can reach the schedule it claims to move.
    /// </summary>
    /// <remarks>
    /// <c>ClaimService</c> takes <see cref="IOnDemandProbes"/> optionally, so only the composition
    /// can say whether it's wired. Without it, the button records an ask, moves no probe, and tells
    /// the operator it dialled their server.
    /// </remarks>
    [Test]
    public async Task TheOnDemandCheckIsGivenSomethingToBringForward()
    {
        await using var site = Site();

        await Assert.That(site.Services.GetService<IOnDemandProbes>()).IsNotNull();
        await Assert.That(ProbesOf(site.Services.GetRequiredService<ClaimService>())).IsNotNull();
    }

    /// <summary>
    /// The pipeline half of the composition maps the routes the site's two POST forms submit to.
    /// </summary>
    /// <remarks>
    /// <see cref="SiteComposition.UseMuiSite"/> had no test: the graph was asserted and the routes
    /// were not, so a <c>Map…</c> call could be dropped and every service would still resolve. A form
    /// posting to a route nobody mapped is a 404 on submit, with everything else perfectly correct.
    /// </remarks>
    [Test]
    public async Task ThePipelineMapsTheRoutesTheFormsPostTo()
    {
        await using var site = Site();
        site.UseMuiSite(ConnectionString);

        // The builder's own data sources rather than the container's EndpointDataSource, which isn't
        // composed until the host starts.
        //
        // Matched on display name, not route pattern: a Razor page is also an endpoint at "/submit"
        // and answers POST too via static SSR, so matching on path alone would pass with
        // MapMuiSubmissions deleted.
        var mapped = ((IEndpointRouteBuilder)site).DataSources
            .SelectMany(source => source.Endpoints)
            .Select(endpoint => endpoint.DisplayName)
            .ToHashSet(StringComparer.Ordinal);

        await Assert.That(mapped).Contains("HTTP: POST /submit")
            .Because("the submission form posts there, and a form posting to nothing is a 404");
        await Assert.That(mapped).Contains("HTTP: POST /g/{slug}/claim/check")
            .Because("the on-demand check is the site's other POST form");
    }

    /// <summary>
    /// What a deployment says about the crawler reaches the crawler.
    /// </summary>
    /// <remarks>
    /// <c>CrawlerSettings.Apply</c> is thoroughly tested against builders it constructs itself, so
    /// none of those tests exercise a site that never calls it — one line inside
    /// <see cref="SiteComposition.AddMuiSite"/>. Losing it is silent: <c>MUI_CRAWL_ENABLED=false</c>
    /// would leave a "pure web tier" deployment still dialling other servers.
    /// </remarks>
    [Test]
    public async Task TheDeploymentsCrawlerSettingsReachTheCrawler()
    {
        await using var off = Site(settings: new Dictionary<string, string?>
        {
            [CrawlerSettings.EnabledConfigurationKey] = "false",
            [CrawlerSettings.SeedsConfigurationKey] = "mush.example.org:4201",
        });

        var options = off.Services.GetRequiredService<CrawlerOptions>();

        await Assert.That(options.Enabled).IsFalse()
            .Because("a replica told not to crawl must not be given a hosted crawler");
        await Assert.That(options.Seeds.Select(seed => $"{seed.Host}:{seed.Port}"))
            .Contains("mush.example.org:4201");

        // And the exemption is not something configuration can hand out (§7.2): the seed above went
        // through the same parser mui-crawl uses, and it is not an operator seed.
        await Assert.That(options.Seeds.Any(seed => seed.IsOperatorSeed)).IsFalse();

        await using var on = Site();

        await Assert.That(on.Services.GetRequiredService<CrawlerOptions>().Enabled).IsTrue()
            .Because("saying nothing leaves the crawler on, which is what the deployed site does");
    }

    /// <summary>
    /// <c>Program</c>'s graph, built by calling <c>Program</c>'s own registration.
    /// </summary>
    /// <remarks>
    /// <b><see cref="SiteComposition.AddMuiSite"/> and not a copy of it</b> — a restated copy agrees
    /// with the original only until one of them is edited. A real <see cref="WebApplicationBuilder"/>
    /// rather than a bare <see cref="ServiceCollection"/>, because much of the framework's own
    /// registrations want <c>IConfiguration</c>/<c>IHostEnvironment</c>. Development by default, where
    /// <see cref="WebApplication.CreateBuilder(WebApplicationOptions)"/> switches scope validation on
    /// — the check a developer already gets on <c>dotnet run</c> and production does not. Nothing is
    /// started and no migration runs; <see cref="ConnectionString"/> is never connected to since
    /// <c>NpgsqlDataSource</c> is lazy.
    /// </remarks>
    private static WebApplication Site(
        string? environment = null,
        string? connectionString = ConnectionString,
        Dictionary<string, string?>? settings = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment ?? Environments.Development,
        });

        builder.Logging.ClearProviders();

        if (settings is { Count: > 0 })
        {
            builder.Configuration.AddInMemoryCollection(settings);
        }

        builder.Services.AddMuiSite(builder.Configuration, connectionString);

        return builder.Build();
    }

    /// <summary>
    /// The cycle's claim service, read off the instance.
    /// </summary>
    /// <remarks>
    /// Reflection, because the collaborator is a private primary-constructor parameter and what's
    /// under test is precisely that the container supplied one. Exposing it publicly for the test
    /// would widen the type for the test's convenience.
    /// </remarks>
    private static object? ClaimsOf(CrawlCycle cycle) => Collaborator<ClaimService>(cycle);

    private static object? ProbesOf(ClaimService claims) => Collaborator<IOnDemandProbes>(claims);

    /// <summary>One privately-held collaborator, read off an instance.</summary>
    /// <remarks>Reflection, for the same reason as <see cref="ClaimsOf"/> — a wiring test is allowed to look at the wiring.</remarks>
    private static object? Collaborator<T>(object instance) =>
        instance.GetType()
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Single(field => field.FieldType == typeof(T))
            .GetValue(instance);
}
