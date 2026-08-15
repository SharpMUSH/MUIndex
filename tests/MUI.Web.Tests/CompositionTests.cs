using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawler;
using MUI.Web;
using MUI.Web.Data;
using MUI.Web.Fixtures;

namespace MUI.Web.Tests;

/// <summary>
/// The graph <c>Program</c> builds, resolved.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the site has two compositions and only one of them was ever exercised.
/// <c>mui-crawl</c> constructs the crawl loop by hand and passes every collaborator explicitly, so
/// its claim path works and has tests; the deployed site assembles the same objects through DI, and
/// there a missing registration is not a compile error — it is a null in an optional parameter, a
/// service locator answering null, or an exception on the first request to one endpoint.
/// </para>
/// <para>
/// Every assertion below was a live hole when it was written. None of them could have been caught by
/// a unit test of the thing that was broken, because none of the things were broken: the wiring
/// between them was.
/// </para>
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
    /// "this site has no database", which is exactly what it looked like: the page rendered, said
    /// nothing was claimed, and was wrong for every operator who had ever claimed anything. A
    /// service-located dependency fails silently by construction, which is why this is asserted at
    /// the composition root rather than left to the page.
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
    /// <para>
    /// <see cref="CrawlCycle"/> takes its <see cref="ClaimService"/> as an optional parameter — a
    /// crawl with no database behind it is a crawl doing slightly less, not one that should refuse to
    /// run — and the deployed site registered no <c>ClaimService</c> the container could put there.
    /// So the parameter took its default, <c>SettleClaimsAsync</c> returned on its first line, and no
    /// probe has ever verified a claim on the one deployment that has operators.
    /// </para>
    /// <para>
    /// The symptom named in §8.5 — nothing sets <c>game.is_claimed</c>, so the listing badge and
    /// §7.5's ceiling grace are unexercised — is this, and it survived claiming shipping because
    /// <c>mui-crawl</c> passes the service by hand and its tests construct the cycle the same way.
    /// </para>
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
    /// The claim service is not scoped, because the thing that needs it most is a singleton.
    /// </summary>
    /// <remarks>
    /// A background service outlives every request scope, so a scoped <see cref="ClaimService"/> is
    /// one <see cref="CrawlCycle"/> can never be given: with scope validation on it is a startup
    /// failure, and with it off it is a captive dependency holding one scope's objects for the
    /// lifetime of the process. Validation is switched on here so the wrong answer cannot pass.
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

        // Resolving OwnerEnrichment itself, because the interesting half of it is a constructor
        // argument rather than a registration: the minter that applies an owner's NAME (§5.7) is
        // optional on the type and required from this composition. Asked for softly it would come
        // back null on a broken graph and owners would rename their games while every URL stayed
        // where it was, silently — which is §8.5's lesson about optional dependencies, restated by
        // the one path that acquired a new one.
        await Assert.That(scope.ServiceProvider.GetRequiredService<OwnerEnrichment>()).IsNotNull();
        await Assert.That(scope.ServiceProvider.GetRequiredService<SlugMinter>()).IsNotNull();
    }

    /// <summary>
    /// The same graph in Production, where nothing validates it for us.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asserted separately because the two environments behaved differently, and this one is the
    /// control rather than the finding: it <b>passed on the parent commit</b>. Production leaves
    /// scope validation off, so the container resolved the scoped <c>ClaimService</c> from the root
    /// and handed <c>CrawlCycle</c> a working one — a captive dependency, harmless for a stateless
    /// service over a pooled data source, and the reason claims verified in production while
    /// Development would not start at all.
    /// </para>
    /// <para>
    /// It is kept because it is what stops the fix regressing in the other direction: Production
    /// must go on resolving the cycle once the lifetime changes.
    /// </para>
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
    /// Separated from the Development assertion so the two findings cannot be confused for one. The
    /// lifetime mistake stops Development dead and leaves Production working; this one is a plain
    /// missing registration and is missing in both, which is why every operator's dashboard was
    /// empty on a running production site.
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
    /// This is what makes §8.5's closing note stale rather than true: a production site does settle
    /// beacons and does set <c>game.is_claimed</c>. It gets there by resolving a scoped service from
    /// the root, which the container permits only because Production leaves scope validation off — so
    /// the behaviour is correct by accident and stops the moment anybody switches validation on.
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
    /// With no connection string there are no accounts, no crawler and no claims — the sign-in and
    /// claim surfaces are absent rather than present and broken (§8), and half a claim flow over
    /// invented games would be a worse answer than none. The assertion that matters is the last one:
    /// the fixture composition must still say on every page that nothing on it was measured.
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
    /// The crawler registers it once and exposes it through two interfaces, saying in a comment that
    /// two instances would be two connection paths answering one question. The web tier then
    /// registered its own with <c>AddSingleton</c>, so the crawler's <c>TryAdd</c> for
    /// <c>IAvailabilityStore</c> was skipped and the concrete type and <c>IReachableHistory</c>
    /// pointed at a second one. Harmless on a shared pool, and a comment that had stopped being true.
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
    /// The same shape as the bug this file exists for: <c>ClaimService</c> takes
    /// <see cref="IOnDemandProbes"/> optionally, because it is constructible without a crawl registry
    /// and <c>mui-crawl</c> constructs it that way — so nothing but the composition can say whether
    /// the deployed site has one. Without it the button records an ask, moves no probe, and tells the
    /// operator it dialled their server.
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
    /// <para>
    /// <see cref="SiteComposition.UseMuiSite"/> is the other half of this file's argument and had no
    /// test at all: the graph was asserted and the routes were not, so a <c>Map…</c> call could be
    /// dropped and every service it needs would still resolve. That is not hypothetical — the
    /// submission route was registered in <c>Program</c>'s top-level statements on one branch while
    /// the pipeline moved into <see cref="SiteComposition"/> on another, and merging the two is
    /// exactly the edit that loses a line like this one.
    /// </para>
    /// <para>
    /// A form that posts to a route nobody mapped is a 404 on submit, with the page, the service and
    /// every unit test around them perfectly correct.
    /// </para>
    /// </remarks>
    [Test]
    public async Task ThePipelineMapsTheRoutesTheFormsPostTo()
    {
        await using var site = Site();
        site.UseMuiSite(ConnectionString);

        // The builder's own data sources rather than the container's EndpointDataSource, which is not
        // composed until the host starts and nothing here starts a server.
        //
        // Matched on the display name and not on the route pattern, because a Razor page is also an
        // endpoint at "/submit" and static server-side rendering makes it answer POST as well — so
        // asserting the path, or even the path and the verb, passes with MapMuiSubmissions deleted.
        // Both weaker forms were written first and both went green with the line removed.
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
    /// <para>
    /// <c>CrawlerSettings.Apply</c> has thorough tests and every one of them calls it on a builder it
    /// constructed itself, so all of them pass on a site that never calls it. The call is one line
    /// inside <see cref="SiteComposition.AddMuiSite"/> and it has already moved once — it was written
    /// in <c>Program</c>'s top-level statements and the pipeline moved out from under it during a
    /// restack.
    /// </para>
    /// <para>
    /// Losing it is silent and expensive in both directions: <c>MUI_CRAWL_ENABLED=false</c> is a
    /// deployment that believes it is a pure web tier and is still dialling other people's servers,
    /// and <c>MUI_CRAWL_SEEDS</c> is the only thing a first deployment knows about.
    /// </para>
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
    /// <para>
    /// <b><see cref="SiteComposition.AddMuiSite"/> and not a copy of it.</b> This restated the
    /// registrations to begin with, which is a second copy that agrees with the first only until
    /// somebody edits one of them — and the edit that breaks it is precisely the one these tests
    /// exist to catch: a scoped service consumed by a singleton, or <c>AddMuiAccounts</c> moving.
    /// The graph therefore moved out of <c>Program</c>'s top-level statements so that the site and
    /// this can call the same thing.
    /// </para>
    /// <para>
    /// A real <see cref="WebApplicationBuilder"/> rather than a bare <see cref="ServiceCollection"/>,
    /// because half the framework's own registrations want <c>IConfiguration</c> and
    /// <c>IHostEnvironment</c> and a hand-built collection fails validation on those before it can say
    /// anything about ours. What is left after the host supplies them is our graph, and only ours.
    /// </para>
    /// <para>
    /// Development by default, where <see cref="WebApplication.CreateBuilder(WebApplicationOptions)"/>
    /// switches scope validation on by itself — so this is the check a developer already gets on
    /// <c>dotnet run</c> and a production deployment does not.
    /// </para>
    /// <para>
    /// Nothing is started and no migration runs: <c>Program</c> does both after the graph is built,
    /// and this stops at the graph. <see cref="ConnectionString"/> is never connected to —
    /// <c>NpgsqlDataSource</c> is lazy.
    /// </para>
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
    /// Reflection, because the collaborator is a private primary-constructor parameter and the
    /// property under test is precisely that the container supplied one rather than letting the
    /// default stand. Exposing it publicly to be asserted on would widen the type for the test's
    /// convenience; a wiring test is allowed to look at the wiring.
    /// </remarks>
    private static object? ClaimsOf(CrawlCycle cycle) => Collaborator<ClaimService>(cycle);

    private static object? ProbesOf(ClaimService claims) => Collaborator<IOnDemandProbes>(claims);

    /// <summary>
    /// One privately-held collaborator, read off an instance.
    /// </summary>
    /// <remarks>
    /// Reflection, because both of these are private primary-constructor parameters and the property
    /// under test is precisely that the container supplied one rather than letting the default stand.
    /// Exposing them publicly to be asserted on would widen two types for a test's convenience; a
    /// wiring test is allowed to look at the wiring.
    /// </remarks>
    private static object? Collaborator<T>(object instance) =>
        instance.GetType()
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Single(field => field.FieldType == typeof(T))
            .GetValue(instance);
}
