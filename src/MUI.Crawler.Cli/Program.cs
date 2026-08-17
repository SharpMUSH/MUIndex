using Dapper;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawl;
using MUI.Crawler;
using MUI.Crawler.Cli;
using MUI.Crawler.Persistence;
using MUI.Discovery;
using MUI.I3;

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

// §11, and deliberately before the connection string is required: "has this host asked us to stop"
// is a question about DNS and about nothing else, and an operator who wants to check that their
// record took effect should not need our database to do it.
if (arguments.OptOutCheck is { } check)
{
    var name = OptOutVocabulary.DnsNameFor(check.Host);
    var answer = await new DnsTxtResolver().LookupAsync(name);

    Console.WriteLine($"asked         {name} IN TXT");

    if (!answer.Answered)
    {
        Console.WriteLine("answer        none — the resolver did not reply. Nothing may be concluded from that.");
        return 1;
    }

    foreach (var record in answer.Records)
    {
        Console.WriteLine($"record        \"{record}\"");
    }

    if (answer.Records.Count == 0)
    {
        Console.WriteLine("answer        no such record");
    }

    // A host with no port named is checked at the port an opt-out record would have to cover to stop
    // everything, which is any of them; 4201 stands in for "some listener".
    var reading = OptOutVocabulary.ReadDns(answer.Records, check.Port ?? 4201);

    Console.WriteLine(reading is null
        ? $"verdict       nothing here asks us to stop dialling {check}"
        : $"verdict       {check} would not be dialled — {reading.Detail}");

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
var slugs = new NpgsqlSlugHistoryStore(source);

// One Intermud-3 pass, and then out. It shares the registry and the presence store with the crawl
// above and touches nothing else — which is the point of running it here: what a person watches is
// the same code the hosted service runs, against the same tables, with the graph built by hand.
if (arguments.I3)
{
    var separator = arguments.I3Gateway.LastIndexOf(':');
    if (separator <= 0 || !int.TryParse(arguments.I3Gateway[(separator + 1)..], out var gatewayPort))
    {
        Console.Error.WriteLine($"--i3-gateway is '{arguments.I3Gateway}', which is not host:port.");
        return 1;
    }

    var i3Options = new I3ServiceOptions
    {
        Enabled = true,
        Gateway = new GatewayOptions
        {
            Host = arguments.I3Gateway[..separator],
            Port = gatewayPort,
            ApiKey = arguments.I3Key ?? "",
        },
    };

    i3Options.Validate();

    await using var i3 = await new I3GatewayFactory(i3Options.Gateway, loggerFactory)
        .ConnectAsync(CancellationToken.None);

    var i3Result = await new I3Cycle(
            i3,
            targets,
            new NpgsqlI3BindingRepository(source),
            new NpgsqlPresenceStore(source),
            fields,
            i3Options.Pass,
            time,
            loggerFactory.CreateLogger<I3Cycle>())
        .RunAsync(CancellationToken.None);

    Console.WriteLine($"i3 pass       {i3Result}");

    await I3Summary.PrintAsync(source);

    return 0;
}

var optOut = new OptOutGate(
    new NpgsqlCrawlOptOutRepository(source),
    new DnsTxtResolver(),
    time,
    loggerFactory.CreateLogger<OptOutGate>());

// §11 — recording a request somebody made off the wire. It writes one row and stops: an operator
// recording an opt-out is not asking for a crawl, and the next cycle honours it before it dials.
if (arguments.OptOut is { } request)
{
    var recorded = await optOut.RecordRequestAsync(
        request.Host, request.Port, arguments.Because!, CancellationToken.None);

    Console.WriteLine($"opt-out       {recorded.Wording}");

    return 0;
}

// §7.8's residue, and the whole of the operator surface over it. A submission the rubric could not
// publish is otherwise a row nobody sees, waiting on a claim from an operator with no reason to know
// this site exists — which is the state one game was in for a fortnight before §7.8 was written.
if (arguments.Submissions)
{
    var pending = await new NpgsqlSubmissionQueue(source).AwaitingAsync(CancellationToken.None);

    Console.WriteLine($"submissions   {pending.Count} awaiting a decision");

    foreach (var row in pending)
    {
        var reachable = row.LastReachableAt is { } seen
            ? $"reachable {(time.GetUtcNow() - seen).TotalHours:F0}h ago"
            : "never reached";

        Console.WriteLine(
            $"  {row.Slug,-32} {row.Address,-38} submitted "
            + $"{(time.GetUtcNow() - row.SubmittedAt).TotalDays:F0}d ago, {reachable}");
        Console.WriteLine($"  {row.Name}");
    }

    if (pending.Count > 0)
    {
        // Never a verdict of ours: the rubric's signals are read from a live probe and are not in
        // the database, so the honest thing to print is where to go and look for them.
        Console.WriteLine();
        Console.WriteLine(
            "  mui-probe <host> <port> shows what one says now. --release <slug> --because \"…\" "
            + "publishes it as a decision of yours.");
    }

    return 0;
}

if (arguments.Release is { } slug)
{
    var released = await new NpgsqlSubmissionQueue(source)
        .ReleaseAsync(slug, time.GetUtcNow(), CancellationToken.None);

    Console.WriteLine(released
        ? $"released      {slug} is listed, recorded as staff — {arguments.Because}"
        : $"released      nothing to do: {slug} is not a submission awaiting a decision");

    return released ? 0 : 1;
}

// §5.7's grace, waived by hand for one pass. The rule waits fourteen days because it cannot tell a
// settled name from a flapping one without watching; a person with the change feed in front of them
// can, and this is where they say so. Nothing else about the rename is different — the same minter
// makes the same decision, writes the same game_slug_history row, and the retired slug redirects for
// ever — so a name this refuses is a name a cycle would refuse too, and the reason to run it twice
// is that the catalogue has moved, not that it might answer differently.
if (arguments.MintNow)
{
    var minter = new SlugMinter(
        games, fields, slugs, grace: TimeSpan.Zero, loggerFactory.CreateLogger<SlugMinter>());

    await using var connection = await source.OpenConnectionAsync();

    // Every game, including the archived and the excluded: §3 keeps their pages, their URLs and
    // their history, so a game that is off the listing is still one somebody can reach and still
    // one whose URL should say what it is called.
    var everyGame = (await connection.QueryAsync<Guid>("SELECT id FROM game ORDER BY name")).ToList();

    Console.WriteLine($"mint          {everyGame.Count} games considered with no grace — {arguments.Because}");

    var minted = 0;

    foreach (var id in everyGame)
    {
        if (await minter.ConsiderAsync(id, time.GetUtcNow()) is not { } rename)
        {
            continue;
        }

        minted++;

        Console.WriteLine(
            $"  {rename.FormerSlug ?? "(same slug)",-34} → {rename.Slug,-34} {rename.Name}");
    }

    Console.WriteLine($"mint          {minted} renamed, {everyGame.Count - minted} left as they were");

    return 0;
}

var planted = await CrawlSeeds.PlantAsync(targets, arguments.Seeds, time);
Console.WriteLine($"seeds         {arguments.Seeds.Count} configured, {planted} new in the registry");

// §11. A dry run dials nobody, so it owes nobody an address; a real one about to open sockets under
// the placeholder is telling every server it reaches to complain to a domain that does not exist.
if (!arguments.DryRun && arguments.InfoUrl == new ProbeOptions().InfoUrl)
{
    Console.WriteLine(
        "contact       PLACEHOLDER — every server dialled is told to write to an address that "
        + "answers nobody. Set MUI_CRAWL_INFO_URL, or pass --info-url.");
}

var cycle = new CrawlCycle(
    targets,
    new TelnetProbe(
        new ProbeOptions { InfoUrl = arguments.InfoUrl },
        loggerFactory.CreateLogger<TelnetProbe>()),
    optOut,
    new HostScopeGuard(new SystemHostResolver()),
    new ProbeIngestor(
        new PresenceWriter(new NpgsqlPresenceStore(source)),
        new AvailabilityWriter(availability),
        new FieldReconciler(fields),
        games,
        new ArchiveSweeper(games, availability, availability),
        new SlugMinter(games, fields, slugs, grace: null, loggerFactory.CreateLogger<SlugMinter>()),
        loggerFactory.CreateLogger<ProbeIngestor>()),
    new CatalogueBinder(
        games,
        endpoints,
        fields,
        slugs,
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
    // §11 — a CLI crawl fills the same replay window an in-process one does.
    new NpgsqlProbePayloads(source),
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
