using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawler;
using MUI.Web.Accounts;
using MUI.Web.Api;
using MUI.Web.Data;

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
    }

    /// <summary>
    /// The same graph in Production, where nothing validates it for us.
    /// </summary>
    /// <remarks>
    /// Asserted separately because the two environments fail differently and only one of them fails
    /// loudly. Development builds with <c>ValidateOnBuild</c> and refuses to start; Production starts
    /// happily and throws the first time the hosted crawler reaches for the cycle, which is inside a
    /// <c>BackgroundService</c> — so the site serves pages while the thing that gathers every fact on
    /// it is dead. A reviewer reading only the test above could reasonably conclude this was a
    /// development-only annoyance.
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
    /// <c>Program</c>'s service registrations, on <c>Program</c>'s host, with a database configured.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A real <see cref="WebApplicationBuilder"/> rather than a bare <see cref="ServiceCollection"/>,
    /// because half the framework's own registrations want <c>IConfiguration</c> and
    /// <c>IHostEnvironment</c> and a hand-built collection fails validation on those before it can say
    /// anything about ours. What is left after the host supplies them is our graph, and only ours.
    /// </para>
    /// <para>
    /// Scope validation on, which is what <c>WebApplication.CreateBuilder</c> does by itself in
    /// Development — so this is the check a developer already gets on <c>dotnet run</c> and a
    /// production deployment does not.
    /// </para>
    /// <para>
    /// Nothing is started and no migration runs: <c>Program</c> applies those after
    /// <see cref="WebApplicationBuilder.Build"/>, and this stops at the graph.
    /// </para>
    /// </remarks>
    private static WebApplication Site(string? environment = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment ?? Environments.Development,
        });

        builder.Logging.ClearProviders();

        builder.Services.AddRazorComponents();
        builder.Services.AddMuiApi(builder.Configuration);
        builder.Services.AddPostgresCatalogue(ConnectionString);
        builder.Services.AddMuiCrawler(ConnectionString, configure => configure.ApplyMigrations = false);
        builder.Services.AddSingleton(new CatalogueSource(IsMeasured: true));
        builder.Services.AddMuiAccounts(builder.Configuration);
        builder.Services.AddSingleton(TimeProvider.System);

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
    private static object? ClaimsOf(CrawlCycle cycle) =>
        typeof(CrawlCycle)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Single(field => field.FieldType == typeof(ClaimService))
            .GetValue(cycle);
}
