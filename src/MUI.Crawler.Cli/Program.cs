using Dapper;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawl;
using MUI.Crawler;
using MUI.Crawler.Cli;
using MUI.Crawler.Persistence;
using MUI.Discovery;

using Microsoft.Extensions.Logging;

using Npgsql;

// One crawl cycle against a real database, printed. mui-probe answers "what did this server say";
// this answers "what did a cycle write", which is the only question a fixture cannot.
//
// It builds the same graph AddMuiCrawler builds, by hand and without a host, so that what a person
// verifies here is what the web deployable runs — see Arguments.Usage for the switches.

var arguments = Arguments.Parse(args);

if (arguments.Help)
{
    Console.WriteLine(Arguments.Usage);
    return 0;
}

if (arguments.Connection is not { Length: > 0 } connectionString)
{
    Console.Error.WriteLine(
        "No connection string. Pass --connection or set MUI_CRAWL_POSTGRES." + Environment.NewLine
        + Environment.NewLine + Arguments.Usage);

    return 2;
}

using var loggerFactory = LoggerFactory.Create(logging => logging
    .SetMinimumLevel(arguments.Verbose ? LogLevel.Debug : LogLevel.Information)
    .AddSimpleConsole(console =>
    {
        console.SingleLine = true;
        console.TimestampFormat = "HH:mm:ss ";
    }));

await using var source = NpgsqlDataSource.Create(connectionString);
var time = TimeProvider.System;

var applied = await new MigrationRunner(source, loggerFactory.CreateLogger<MigrationRunner>()).ApplyAsync();
Console.WriteLine($"schema        {MigrationRunner.Scripts.Count} migrations known, {applied.Count} applied now");

var discovery = new DiscoveryOptions
{
    BatchSize = arguments.Batch,
    MaxConcurrency = arguments.Concurrency,
    FollowReferrals = arguments.FollowReferrals,
};

discovery.Validate();

var targets = new NpgsqlCrawlTargetRepository(source);
var games = new NpgsqlGameStore(source);
var endpoints = new NpgsqlEndpointStore(source);
var fields = new NpgsqlGameFieldStore(source);
var availability = new NpgsqlAvailabilityStore(source);

var planted = await CrawlSeeds.PlantAsync(targets, arguments.Seeds, time);
Console.WriteLine($"seeds         {arguments.Seeds.Count} configured, {planted} new in the registry");

var cycle = new CrawlCycle(
    targets,
    new TelnetProbe(new ProbeOptions(), loggerFactory.CreateLogger<TelnetProbe>()),
    new HostScopeGuard(new SystemHostResolver()),
    new ProbeIngestor(
        new PresenceWriter(new NpgsqlPresenceStore(source)),
        new AvailabilityWriter(availability),
        new FieldReconciler(fields),
        games,
        new ArchiveSweeper(games, availability, availability),
        loggerFactory.CreateLogger<ProbeIngestor>()),
    new CatalogueBinder(
        games,
        endpoints,
        fields,
        new IdentityMatcher(
            new CatalogueGameDirectory(games),
            new CatalogueEndpointDirectory(endpoints),
            fields,
            new NpgsqlGameFieldIndex(source),
            discovery),
        new NpgsqlDuplicateReviewRepository(source),
        time,
        loggerFactory.CreateLogger<CatalogueBinder>()),
    new ReferralGraphWriter(new NpgsqlReferralRepository(source), targets, discovery, time),
    new CrawlRateLimiter(discovery, time),
    new HostGate(),
    discovery,
    time,
    // §8 — a probe of a claimed game refreshes what we last saw, and a probe of a game whose owner
    // has just published their token settles the claim. Both happen on the ordinary schedule.
    new ClaimService(new NpgsqlClaimStore(source), games, time),
    loggerFactory.CreateLogger<CrawlCycle>());

if (arguments.DryRun)
{
    var due = await targets.DueAsync(time.GetUtcNow(), arguments.Batch, CancellationToken.None);
    Console.WriteLine($"due           {due.Count}");

    foreach (var target in due)
    {
        Console.WriteLine(
            $"  {target.Host}:{target.Port,-6} depth {target.Depth}  failures {target.ConsecutiveFailures}"
            + $"  due {target.NextProbeAt:u}");
    }

    return 0;
}

// Ctrl+C stops the cycle the way the hosted service's stopping token would, so the shutdown a person
// sees here is the shutdown production takes.
using var stopping = new CancellationTokenSource();

Console.CancelKeyPress += (_, cancel) =>
{
    cancel.Cancel = true;
    stopping.Cancel();
};

for (var pass = 1; pass <= arguments.Cycles && !stopping.IsCancellationRequested; pass++)
{
    var report = await cycle.RunAsync(stopping.Token);
    Console.WriteLine($"cycle {pass,-7} {report}");
}

await CrawlSummary.PrintAsync(source);

return 0;
