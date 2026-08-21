using System.ComponentModel;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawler;
using MUI.Discovery;

using Npgsql;

namespace MUI.Web.Mcp;

/// <summary>
/// The six crawl-administration tools -- the half of the nine that replace the ssh/scp/
/// <c>docker compose run --entrypoint mui-crawl</c> dance for the registry and the crawl cycle
/// itself, mirroring <c>mui-crawl</c>'s CLI surface (see <c>src/MUI.Crawler.Cli/Arguments.cs</c>).
/// The three catalogue-write tools -- <see cref="GameAdminTools.GameFieldSetAsync"/>,
/// <see cref="GameAdminTools.GameRenameAsync"/> and <see cref="GameAdminTools.GameMergeAsync"/> --
/// are <see cref="GameAdminTools"/>, a separate <c>[McpServerToolType]</c> registered alongside this
/// one (<c>MuiMcp.AddMuiMcp</c>): the two groups share no state beyond <c>time</c>/<c>logger</c>, so
/// there is nothing a combined class bought except size.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every dependency here is a singleton the running deployable already assembled.</b> Nothing in
/// this class builds its own <see cref="CrawlCycle"/> the way <c>mui-crawl</c>'s <c>Program.cs</c>
/// does -- it asks the container for the same instances <c>CrawlerService</c> uses, registered by
/// <c>AddMuiCrawler</c>/<c>AddMuiCrawlerCore</c>. That is what makes <see cref="CrawlRunCycleAsync"/>'s
/// lock-contention behaviour correct rather than accidental: there is exactly one
/// <see cref="CrawlCycle"/> per process, so a manual invocation and the hosted crawler are contending
/// for the same advisory lock rather than running two unrelated crawls at once.
/// </para>
/// <para>
/// Registered against DI's per-request scope in stateless HTTP mode (<c>MuiMcp.AddMuiMcp</c>), so a
/// tool call reuses the request's own service provider exactly as a minimal API endpoint would.
/// </para>
/// </remarks>
[McpServerToolType]
public sealed class CrawlAdminTools(
    ICrawlTargetRepository targets,
    OptOutGate optOut,
    IDnsTxtResolver dns,
    NpgsqlDataSource source,
    CrawlerOptions crawlerOptions,
    CrawlCycle cycle,
    TimeProvider time,
    ILogger<CrawlAdminTools>? logger = null)
{
    /// <summary>
    /// How many game lines <c>crawl_summary</c> lists when nobody says. The registry is far larger
    /// than an answer can carry -- an unbounded listing is not a long answer, it is a refused one --
    /// so the default is a page and the totals carry the whole count.
    /// </summary>
    private const int DefaultSummaryGames = 25;

    [McpServerTool(Name = "crawl_seed_add", Destructive = false)]
    [Description("""
        Adds one address to the crawl registry -- same semantics as mui-crawl's --seed/--seed-exempt.
        Idempotent: an address already known keeps its own schedule and is not dragged forward, so
        calling this again about a game already being crawled does not burst traffic at it.
        """)]
    public async Task<CrawlSeedAddResult> CrawlSeedAddAsync(
        [Description("Host name or literal address (bracket an IPv6 literal, e.g. 2001:db8::1).")]
        string host,
        [Description(
            "Port to dial. Required -- a crawl target dials exactly one port, and unlike an "
            + "opt-out address there is no safe default to fall back to.")]
        int port,
        [Description(
            "Exempt this address from the resolved-address scope gate (spec section 7.2). Only for "
            + "an address chosen on purpose, e.g. an operator's own 127.0.0.1.")]
        bool exempt = false,
        CancellationToken cancellationToken = default)
    {
        var seed = ParseSeedOrThrow(host, port, exempt);

        var planted = await CrawlSeeds.PlantAsync(targets, [seed], time, cancellationToken);

        return new CrawlSeedAddResult(seed.Host, seed.Port, exempt, planted > 0);
    }

    [McpServerTool(Name = "crawl_opt_out_record")]
    [Description("""
        Records that somebody asked us to stop crawling an address (spec section 11) -- same as
        mui-crawl's --opt-out plus --because. A bare host with no port covers every port on it.
        """)]
    public async Task<CrawlOptOut> CrawlOptOutRecordAsync(
        [Description("The host they asked about.")] string host,
        [Description(
            "Who asked and how. Required -- this is a claim about somebody else's wishes and must "
            + "never be defaulted or inferred (see the ContactedMaintainer defect in CLAUDE.md).")]
        string because,
        [Description("The single port they asked about, or omit for every port on the host.")]
        int? port = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(because))
        {
            throw new McpException("because is required: say who asked and how.");
        }

        return await optOut.RecordRequestAsync(host, port, because, cancellationToken);
    }

    [McpServerTool(Name = "crawl_opt_out_check", ReadOnly = true, Destructive = false)]
    [Description("""
        Read-only DNS TXT lookup for a standing opt-out at _muindex.<host> -- same as mui-crawl's
        --opt-out-check. Touches no database and no game server.
        """)]
    public async Task<CrawlOptOutCheckResult> CrawlOptOutCheckAsync(
        [Description("The host to ask DNS about.")] string host,
        [Description(
            "Port to check the record against. Defaults to 4201 (\"some listener\") when omitted, "
            + "matching the CLI.")]
        int? port = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        var name = OptOutVocabulary.DnsNameFor(host);
        var answer = await dns.LookupAsync(name, cancellationToken);

        if (!answer.Answered)
        {
            return new CrawlOptOutCheckResult(
                name,
                Answered: false,
                Records: [],
                Verdict: "the resolver did not reply -- nothing may be concluded from that");
        }

        var reading = OptOutVocabulary.ReadDns(answer.Records, port ?? 4201);
        var address = port is { } p ? $"{host}:{p}" : host;

        return new CrawlOptOutCheckResult(
            name,
            Answered: true,
            Records: answer.Records,
            Verdict: reading is null
                ? $"nothing here asks us to stop dialling {address}"
                : $"{address} would not be dialled -- {reading.Detail}");
    }

    [McpServerTool(Name = "crawl_due_targets", ReadOnly = true, Destructive = false)]
    [Description("""
        What is due to be probed next -- same as mui-crawl's --dry-run. Read-only: plants no seeds and
        runs no cycle.
        """)]
    public async Task<IReadOnlyList<CrawlDueTarget>> CrawlDueTargetsAsync(
        [Description("How many due targets to list. Default 50.")] int batch = 50,
        CancellationToken cancellationToken = default) =>
        ToDue(await targets.DueAsync(time.GetUtcNow(), Math.Max(1, batch), cancellationToken));

    [McpServerTool(Name = "crawl_run_cycle")]
    [Description("""
        Plants any given seeds, then either lists what's due (dryRun) or runs `cycles` real crawl
        passes through the process's own already-composed CrawlCycle -- the exact same object, and the
        exact same AdvisoryLease/CrawlerOptions.AdvisoryLockKey machinery, that the hosted crawler
        (CrawlerService) uses. The crawl lease is session-level and the hosted crawler holds it for its
        whole lifetime, so a real run here CONTENDS for that same lock: if the hosted crawler is
        active on this replica, this call acquires nothing, runs zero cycles, and says so in the
        result's Note. That is correct, safe behaviour and not a bug -- it is genuinely useful when the
        hosted crawler is disabled here (MUI_CRAWL_ENABLED=false) or for an ad hoc forced pass.

        mui-crawl's --batch/--concurrency/--no-referrals/--info-url are deliberately NOT exposed here
        as per-call overrides the way the CLI offers them, because the CLI builds a throwaway
        CrawlCycle per invocation and this tool reuses the deployment's one singleton -- which is the
        whole point of the lock-contention behaviour above. A parameter that looked like it changed a
        bound but silently did not would be exactly the kind of quiet no-op this codebase refuses to
        ship. `batch` is still honoured, but only for the dry-run listing, where it means "how many due
        targets to show" rather than a crawl bound.
        """)]
    public async Task<CrawlRunCycleResult> CrawlRunCycleAsync(
        [Description(
            "host:port seed addresses to add to the registry, e.g. [\"mush.pennmush.org:4201\"].")]
        IReadOnlyList<string>? seeds = null,
        [Description(
            "Same shape as seeds, but exempt from the resolved-address gate (spec section 7.2) -- "
            + "only for addresses chosen on purpose.")]
        IReadOnlyList<string>? exemptSeeds = null,
        [Description("How many passes to run when this is a real (non-dry) run. Default 1.")]
        int cycles = 1,
        [Description("Dry-run only: how many due targets to list. Default 50.")]
        int batch = 50,
        [Description("Plant seeds and list what is due, without running anything.")]
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        var toPlant = ParseSeedListOrThrow(seeds, isOperatorSeed: false)
            .Concat(ParseSeedListOrThrow(exemptSeeds, isOperatorSeed: true))
            .ToList();

        var planted = await CrawlSeeds.PlantAsync(targets, toPlant, time, cancellationToken);

        if (dryRun)
        {
            var due = await targets.DueAsync(time.GetUtcNow(), Math.Max(1, batch), cancellationToken);

            return new CrawlRunCycleResult(
                toPlant.Count,
                planted,
                DryRun: true,
                LeaseHeld: false,
                Due: ToDue(due),
                Cycles: null,
                Note: $"{due.Count} due; a dry run writes nothing beyond the seeds above.");
        }

        // Exactly what CrawlerService.ExecuteAsync does per cycle (MUI.Crawler/CrawlerService.cs):
        // ask for the lease, and do nothing if another session -- the hosted crawler, almost always --
        // already holds it. See this method's own doc comment for why that is correct here rather
        // than a race to work around.
        await using var lease = await AdvisoryLease.TryAcquireAsync(
            source, crawlerOptions.AdvisoryLockKey, cancellationToken);

        if (lease is null)
        {
            logger?.LogInformation(
                "crawl_run_cycle: the crawl lease is held elsewhere; {Planted} seed(s) planted and no cycle ran",
                planted);

            return new CrawlRunCycleResult(
                toPlant.Count,
                planted,
                DryRun: false,
                LeaseHeld: false,
                Due: null,
                Cycles: [],
                Note: "Another process -- almost certainly the hosted crawler on this or another "
                    + "replica -- already holds the crawl lease. Zero cycles ran; this is the correct, "
                    + "not-double-crawling behaviour, not a failure.");
        }

        var reports = new List<CycleReport>();

        for (var pass = 0; pass < Math.Max(1, cycles) && !cancellationToken.IsCancellationRequested; pass++)
        {
            reports.Add(await cycle.RunAsync(cancellationToken));
        }

        return new CrawlRunCycleResult(
            toPlant.Count,
            planted,
            DryRun: false,
            LeaseHeld: true,
            Due: null,
            Cycles: reports,
            Note: $"Ran {reports.Count} cycle(s) while holding the crawl lease.");
    }

    [McpServerTool(Name = "crawl_summary", ReadOnly = true, Destructive = false)]
    [Description("""
        Registry/crawl snapshot read back out of the database -- the same figures mui-crawl prints
        after a cycle (see MUI.Crawler.CrawlSummary, which this calls directly).

        The totals always count the whole registry. The per-game listing is a page, because there are
        far more games than fit in one answer: ask for the next page with `offset`, or for `games` = 0
        when the totals are all you wanted.
        """)]
    public Task<CrawlSummaryData> CrawlSummaryAsync(
        [Description(
            "How many game lines to list, in slug order. Default 25; 0 for totals alone.")]
        int games = DefaultSummaryGames,
        [Description("How many game lines to skip before taking that page. Default 0.")]
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        if (games < 0 || offset < 0)
        {
            throw new McpException("games and offset cannot be negative.");
        }

        return CrawlSummary.CollectAsync(source, games, offset, cancellationToken);
    }

    private static IReadOnlyList<CrawlDueTarget> ToDue(IReadOnlyList<CrawlTarget> due) =>
        [.. due.Select(t => new CrawlDueTarget(t.Host, t.Port, t.Depth, t.ConsecutiveFailures, t.NextProbeAt))];

    /// <summary>
    /// <see cref="CrawlSeed"/>'s own constructor and <see cref="CrawlSeed.Validate"/> throw a plain
    /// <see cref="ArgumentException"/>, which the MCP SDK reports to a caller as a generic "an error
    /// occurred" — see the SDK's own docs on automatic exception handling. Re-thrown as
    /// <see cref="McpException"/> here so the caller actually sees why an address was refused, the way
    /// every other refusal in this class already does.
    /// </summary>
    private static CrawlSeed ParseSeedOrThrow(string host, int port, bool isOperatorSeed)
    {
        try
        {
            var seed = new CrawlSeed(host, port, isOperatorSeed);
            seed.Validate();
            return seed;
        }
        catch (ArgumentException error)
        {
            throw new McpException(error.Message, error);
        }
    }

    private static IReadOnlyList<CrawlSeed> ParseSeedListOrThrow(
        IReadOnlyList<string>? values, bool isOperatorSeed)
    {
        if (values is not { Count: > 0 })
        {
            return [];
        }

        try
        {
            return [.. values.Select(value => CrawlSeed.Parse(value, isOperatorSeed))];
        }
        catch (ArgumentException error)
        {
            throw new McpException(error.Message, error);
        }
    }
}
