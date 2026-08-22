using System.Diagnostics;

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
// this answers "what did a cycle write". Builds the same graph AddMuiCrawler builds, by hand and
// without a host, so what's verified here is what the web deployable runs.

var arguments = Arguments.Parse(args);

if (arguments.Help)
{
    Console.WriteLine(Arguments.Usage);
    return 0;
}

// §11, deliberately before the connection string is required: checking an opt-out record is a DNS
// question and shouldn't need our database.
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

    // No port named: check the port an opt-out covering everything would have to cover, i.e. any of
    // them — 4201 stands in for "some listener".
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

// One Intermud-3 pass, then out. Shares the registry and presence store with the crawl above and
// touches nothing else — the same code the hosted service runs, against the same tables.
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

// §11. Writes one row and stops — recording an opt-out isn't a request to crawl; the next cycle
// honours it before it dials.
if (arguments.OptOut is { } request)
{
    var recorded = await optOut.RecordRequestAsync(
        request.Host, request.Port, arguments.Because!, CancellationToken.None);

    Console.WriteLine($"opt-out       {recorded.Wording}");

    return 0;
}

// §7.8's residue: a submission the rubric couldn't publish is otherwise a row nobody sees, waiting on
// a claim from an operator with no reason to know this site exists.
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
        // Never a verdict of ours: the rubric's signals come from a live probe and aren't in the
        // database, so print where to go look for them.
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

// §5.7's grace, waived by hand for one pass. The fourteen-day wait exists because the rule can't tell
// a settled name from a flapping one without watching; a person with the change feed in front of them
// can. Nothing else about the rename differs — same minter, same decision, same history row.
if (arguments.MintNow)
{
    var minter = new SlugMinter(
        games, fields, slugs, grace: TimeSpan.Zero, loggerFactory.CreateLogger<SlugMinter>());

    await using var connection = await source.OpenConnectionAsync();

    // Every game, including archived and excluded ones: §3 keeps their pages and URLs, so a delisted
    // game's URL should still say what it's called.
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

// §7.3's operator surface: a person names the winner and loser; an open review for this pair carries
// its score onto the merge log and is closed, and a pair with no open review is still mergeable as a
// judgement with no signals (migration 0018).
if (arguments.Merge is { } mergeRequest)
{
    var gameDirectory = new CatalogueGameDirectory(games);
    var mergeLog = new NpgsqlMergeLog(source);
    var mergeService = new ReviewMergeService(
        gameDirectory,
        new NpgsqlDuplicateReviewRepository(source),
        new MergeApplier(new CatalogueEndpointDirectory(endpoints), fields, mergeLog, time),
        mergeLog,
        time,
        new NpgsqlUnitOfWorkFactory(source));

    Guid? winnerId = null;
    Guid? loserId = null;

    try
    {
        winnerId = await ResolveGameIdAsync(games, mergeRequest.Winner, CancellationToken.None);
        loserId = await ResolveGameIdAsync(games, mergeRequest.Loser, CancellationToken.None);

        if (winnerId is not { } winner)
        {
            Console.Error.WriteLine($"merge         '{mergeRequest.Winner}' is not a known slug or game id.");
            return 1;
        }

        if (loserId is not { } loser)
        {
            Console.Error.WriteLine($"merge         '{mergeRequest.Loser}' is not a known slug or game id.");
            return 1;
        }

        var verdict = await mergeService.MergeAsync(winner, loser, arguments.Because!, CancellationToken.None);

        switch (verdict)
        {
            case MergeVerdict.Merged(var result):
                Console.WriteLine(
                    $"merge         {mergeRequest.Loser} -> {mergeRequest.Winner}  merge id {result.MergeId}");
                Console.WriteLine(result.ResolvedReviewId is { } reviewId
                    ? $"review        {reviewId} resolved, score {result.Score:F2}"
                    : "review        no open review named this pair; recorded as a judgement with no signals");
                if (result.MootReviewsResolved > 0)
                {
                    Console.WriteLine(
                        $"moot          {result.MootReviewsResolved} further review(s) closed: both sides "
                        + "now redirect to the winner");
                }

                return 0;

            case MergeVerdict.SelfMerge:
                Console.Error.WriteLine("merge         refused: A game cannot be merged into itself.");
                return 1;

            case MergeVerdict.UnknownGame(var id):
                Console.Error.WriteLine($"merge         refused: {id} does not name a game.");
                return 1;

            // merge_log's own guards: a game already absorbed elsewhere, or a redirect chain — the
            // schema refusing rather than us, so its message is more specific than anything we'd add.
            case MergeVerdict.AlreadyAbsorbed(var databaseMessage):
                Console.Error.WriteLine($"merge         refused by the database: {databaseMessage}");
                return 1;

            case MergeVerdict.RedirectChain(var databaseMessage):
                Console.Error.WriteLine($"merge         refused by the database: {databaseMessage}");
                return 1;

            default:
                throw new UnreachableException($"Unhandled {nameof(MergeVerdict)}: {verdict}");
        }
    }
    catch (Exception error) when (error is ArgumentException or InvalidOperationException)
    {
        Console.Error.WriteLine($"merge         refused: {error.Message}");
        return 1;
    }
}

// §7.3's other verdict: the pair is two games. The row is closed with the reason beside it and nothing
// else happens — which is the point, since the only thing a false positive needs is to stop being asked
// about. Deliberately has no winner: nothing is absorbed, so neither side is named first.
if (arguments.Distinct is { } distinctRequest)
{
    var mergeLog = new NpgsqlMergeLog(source);
    var distinctService = new ReviewMergeService(
        new CatalogueGameDirectory(games),
        new NpgsqlDuplicateReviewRepository(source),
        new MergeApplier(new CatalogueEndpointDirectory(endpoints), fields, mergeLog, time),
        mergeLog,
        time,
        new NpgsqlUnitOfWorkFactory(source));

    try
    {
        var left = await ResolveGameIdAsync(games, distinctRequest.Left, CancellationToken.None);
        var right = await ResolveGameIdAsync(games, distinctRequest.Right, CancellationToken.None);

        if (left is not { } leftId)
        {
            Console.Error.WriteLine($"distinct      '{distinctRequest.Left}' is not a known slug or game id.");
            return 1;
        }

        if (right is not { } rightId)
        {
            Console.Error.WriteLine($"distinct      '{distinctRequest.Right}' is not a known slug or game id.");
            return 1;
        }

        var verdict = await distinctService.KeepDistinctAsync(
            leftId, rightId, arguments.Because!, CancellationToken.None);

        switch (verdict)
        {
            case DistinctVerdict.Kept(var reviewId, var score):
                Console.WriteLine(
                    $"distinct      {distinctRequest.Left} + {distinctRequest.Right}  two games");
                Console.WriteLine($"review        {reviewId} resolved, score {score:F2}");
                return 0;

            case DistinctVerdict.NoOpenReview:
                Console.Error.WriteLine(
                    "distinct      no open review names this pair; nothing to close.");
                return 1;

            case DistinctVerdict.SameGame:
                Console.Error.WriteLine("distinct      refused: both sides name the same game.");
                return 1;

            case DistinctVerdict.UnknownGame(var id):
                Console.Error.WriteLine($"distinct      refused: {id} does not name a game.");
                return 1;

            case DistinctVerdict.AlreadyOneListing(var listing):
                Console.Error.WriteLine(
                    $"distinct      refused: a merge still in force already makes these one listing "
                    + $"({listing}). Revert it first.");
                return 1;

            default:
                throw new UnreachableException($"Unhandled {nameof(DistinctVerdict)}: {verdict}");
        }
    }
    catch (Exception error) when (error is ArgumentException or InvalidOperationException)
    {
        Console.Error.WriteLine($"distinct      refused: {error.Message}");
        return 1;
    }
}

// §5.7's operator-driven rename: the same immediate, no-grace mint-and-rename SlugMinter.ApplyAsync
// performs for a verified owner's own save (OwnerEnrichment -> IOwnerRenames), for a game with no
// claimed owner or where staff has decided what it is called. NAME is written as staff first — the
// same declared record an owner's write produces — so the value has provenance and reaches the
// change feed even on the rare attempt where the mint itself has to wait for a retry.
if (arguments.Rename is { } renameRequest)
{
    try
    {
        var gameId = await ResolveGameIdAsync(games, renameRequest.Slug, CancellationToken.None)
            ?? throw new ArgumentException($"'{renameRequest.Slug}' is not a known slug or game id.");

        var game = await games.ByIdAsync(gameId, CancellationToken.None)
            ?? throw new ArgumentException($"'{renameRequest.Slug}' is not a known slug or game id.");

        var newName = renameRequest.NewName.Trim();

        if (string.Equals(newName, game.Name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"'{game.Slug}' is already named '{newName}'; nothing to rename.");
        }

        var now = time.GetUtcNow();

        var existingName = (await fields.ForGameAsync(gameId, FieldSource.Staff, CancellationToken.None))
            .FirstOrDefault(f => string.Equals(f.Field, "NAME", StringComparison.Ordinal));

        await fields.UpsertAsync(
            new GameField(gameId, "NAME", FieldSource.Staff, newName, now, now), CancellationToken.None);

        if (existingName is null || !string.Equals(existingName.Value, newName, StringComparison.Ordinal))
        {
            await fields.RecordChangeAsync(
                new FieldChange(gameId, "NAME", FieldSource.Staff, existingName?.Value, newName, now),
                CancellationToken.None);
        }

        var renameMinter = new SlugMinter(
            games, fields, slugs, grace: null, loggerFactory.CreateLogger<SlugMinter>());

        var rename = await renameMinter.ApplyAsync(gameId, newName, CancellationToken.None)
            ?? throw new InvalidOperationException(
                $"'{newName}' could not be minted a unique slug right now (a database-level "
                + "collision SlugMinter could not resolve on this attempt). NAME was still written "
                + "as staff and will win the ordinary crawl cycle's own re-mint once one next runs.");

        Console.WriteLine(
            $"rename        {rename.FormerSlug ?? game.Slug,-34} → {rename.Slug,-34} {rename.Name}"
            + $" — {arguments.Because}");

        return 0;
    }
    catch (Exception error) when (error is ArgumentException or InvalidOperationException)
    {
        Console.Error.WriteLine($"rename        refused: {error.Message}");
        return 1;
    }
    catch (PostgresException error)
    {
        Console.Error.WriteLine($"rename        refused by the database: {error.MessageText}");
        return 1;
    }
}

var planted = await CrawlSeeds.PlantAsync(targets, arguments.Seeds, time);
Console.WriteLine($"seeds         {arguments.Seeds.Count} configured, {planted} new in the registry");

// §11. A dry run dials nobody so owes nobody an address; a real one under the placeholder would tell
// every server it reaches to complain to a domain that doesn't exist.
if (!arguments.DryRun && arguments.InfoUrl == new ProbeOptions().InfoUrl)
{
    Console.WriteLine(
        "contact       PLACEHOLDER — every server dialled is told to write to an address that "
        + "answers nobody. Set MUI_CRAWL_INFO_URL, or pass --info-url.");
}

// One resolver, shared: HostScopeGuard and the identity matcher's ResolvedEndpoint signal (§7.3) both
// read the same DNS answers rather than standing up a second lookup path.
var hostResolver = new SystemHostResolver();

var cycle = new CrawlCycle(
    targets,
    new TelnetProbe(
        new ProbeOptions { InfoUrl = arguments.InfoUrl },
        loggerFactory.CreateLogger<TelnetProbe>()),
    optOut,
    new HostScopeGuard(hostResolver),
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
            discovery,
            hostResolver,
            new NpgsqlMergeLog(source)),
        new NpgsqlDuplicateReviewRepository(source),
        new NpgsqlMergeLog(source),
        time,
        loggerFactory.CreateLogger<CatalogueBinder>()),
    new ReferralGraphWriter(new NpgsqlReferralRepository(source), targets, discovery, time),
    new CrawlRateLimiter(discovery, time),
    new HostConcurrencyGate(),
    discovery,
    time,
    // §8 — a probe of a claimed game refreshes what we last saw; a probe of one whose owner just
    // published their token settles the claim.
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

/// <summary>
/// A game id, read the way an operator would type it: the slug on the page, or the id itself. Null
/// when neither names a game — never thrown, so a caller can say which of two arguments was wrong
/// rather than which parsed first.
/// </summary>
static async Task<Guid?> ResolveGameIdAsync(IGameStore games, string identifier, CancellationToken ct)
{
    if (Guid.TryParse(identifier, out var id))
    {
        return await games.ByIdAsync(id, ct) is not null ? id : null;
    }

    return await games.BySlugAsync(identifier, ct) is { } bySlug ? bySlug.Id : null;
}
