using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Dapper;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Discovery;
using MUI.Import;
using MUI.Import.Sources;

using Npgsql;

// A one-off, deliberately manual verification: read a live directory ONCE, run it through the real
// pipeline into a real PostgreSQL, and print what landed.
//
// This is not a test and is not in the suite — every test in this repository parses a committed
// fixture and no test may touch the network. It exists so that "the parser works against the page as
// it actually is today" is a claim somebody made by doing it, and it is kept so the next person can
// repeat it without writing it again.
//
//   dotnet run --project tools/live-import -- --source MudStats --dsn "Host=…" --cache /var/tmp/mudstats
//
// With no --dsn it parses and reports and writes nothing.
//
// --cache names a directory of raw fetched bodies. It exists so that a second look at the same data
// costs the site nothing: re-fetching a hundred and forty-five pages to check one parsing change is
// precisely the manners this whole component is about. IT IS SOMEBODY ELSE'S CATALOGUE AND IT DOES
// NOT BELONG IN THIS REPOSITORY — name a path outside the working tree. The test fixtures under
// tests/MUI.Import.Tests/Fixtures are a handful of hand-trimmed records and are a different thing.

var arguments = Args.Parse(args);

if (arguments.Help)
{
    Console.WriteLine("""
        usage: dotnet run --project tools/live-import -- [options]

          --source <name>   One of the names --list prints. Default: every source whose etiquette
                            permits a fetch; the rest are named and skipped.
          --dsn <conn>      PostgreSQL connection string. Omitted: parse and report, write nothing.
          --cache <dir>     Directory of raw fetched bodies, read before the network and written
                            after it. Point it OUTSIDE the working tree; it is somebody else's
                            catalogue, not ours to commit.
          --list            Print every registered source, its tier and its route, and exit.

        Environment equivalents: MUI_LIVE_SOURCE, MUI_LIVE_DSN, MUI_LIVE_CACHE.
        """);

    return 0;
}

var time = TimeProvider.System;

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

var registry = new SourceRegistry([
    TinTinMsspCrawlerSource.Create(http, time),
    TinTinMsdpCrawlerSource.Create(http, time),
    MudConnectorSource.Create(http, time),
    MudStatsSource.Create(http, time),
    MudVerseSource.Create(http, time),
]);

Console.WriteLine($"user agent : {ImporterIdentity.UserAgent}");
Console.WriteLine();

if (arguments.List)
{
    foreach (var (source, decision) in registry.Plan())
    {
        Console.WriteLine($"{source.SourceName,-28} {source.Tier,-9} {decision.Route,-11} "
            + (decision.Uri?.AbsoluteUri ?? decision.RefusedReason));
    }

    return 0;
}

var selected = registry.Plan()
    .Where(entry => arguments.Source is null
        || string.Equals(entry.Source.SourceName, arguments.Source, StringComparison.OrdinalIgnoreCase))
    .ToList();

if (selected.Count == 0)
{
    Console.Error.WriteLine($"No source named '{arguments.Source}'. Try --list.");

    return 2;
}

var read = new List<(IDirectorySource Source, List<ImportedGame> Games)>();

foreach (var (source, decision) in selected)
{
    if (decision.Route is FetchRoute.None)
    {
        Console.WriteLine($"skipped: {source.SourceName} — {decision.RefusedReason}.");
        Console.WriteLine();

        continue;
    }

    Console.WriteLine($"{source.SourceName}");
    Console.WriteLine($"  tier     : {source.Tier}");
    Console.WriteLine($"  route    : {decision.Route} {decision.Uri}");

    var fetcher = Fetcher(source.Etiquette);
    await fetcher.PrimeRobotsAsync(CancellationToken.None);

    Console.WriteLine($"  robots   : read; effective interval {Interval(fetcher)}");

    var started = time.GetUtcNow();
    var games = new List<ImportedGame>();

    await foreach (var game in Rebuild(source, fetcher).ReadAsync(CancellationToken.None))
    {
        games.Add(game);
    }

    Console.WriteLine($"  elapsed  : {time.GetUtcNow() - started:hh\\:mm\\:ss}");
    Report(games);
    Console.WriteLine();

    read.Add((source, games));
}

if (arguments.Dsn is null)
{
    Console.WriteLine("No --dsn; parsed only, wrote nothing.");

    return 0;
}

await using var dataSource = NpgsqlDataSource.Create(arguments.Dsn);
await new MigrationRunner(dataSource).ApplyAsync();

var targets = new InMemoryTargets();
var pipeline = new ImportPipeline(
    targets,
    new NpgsqlGameStore(dataSource),
    new NpgsqlEndpointStore(dataSource),
    new NpgsqlGameFieldStore(dataSource),
    new NpgsqlPresenceStore(dataSource),
    new ImportedAvailabilityWriter(new NpgsqlAvailabilityStore(dataSource)),
    new NpgsqlImportProvenanceStore(dataSource),
    time);

foreach (var (source, games) in read)
{
    var report = await pipeline.RunAsync(new ReplaySource(source, games), CancellationToken.None);

    Console.WriteLine(report);

    foreach (var note in report.Notes)
    {
        Console.WriteLine($"  · {note}");
    }

    Console.WriteLine();
}

await using var connection = await dataSource.OpenConnectionAsync();
var stored = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM import_provenance");

Console.WriteLine($"crawl targets seeded: {targets.Targets.Count.ToString(CultureInfo.InvariantCulture)}");
Console.WriteLine($"provenance rows     : {stored.ToString(CultureInfo.InvariantCulture)}");

return 0;

void Report(IReadOnlyList<ImportedGame> games)
{
    var endpoints = games.SelectMany(game => game.Endpoints).ToList();
    var hosts = endpoints
        .Select(endpoint => endpoint.Host)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();

    Console.WriteLine($"  worlds found       : {games.Count}");
    Console.WriteLine($"  endpoints          : {endpoints.Count} ({hosts} distinct hosts)");
    Console.WriteLine($"  distinct host:port : "
        + endpoints.Select(e => $"{e.Host.ToLowerInvariant()}:{e.Port}").Distinct(StringComparer.Ordinal).Count());
    Console.WriteLine($"  with a codebase    : {games.Count(game => game.Fields.ContainsKey("CODEBASE"))}");
    Console.WriteLine($"  with a website     : {games.Count(game => game.Fields.ContainsKey("WEBSITE"))}");
    Console.WriteLine($"  dated player counts: {games.Count(game => game.Presence.Count > 0)} "
        + $"({games.Sum(game => game.Presence.Count)} readings, "
        + $"{games.Sum(game => game.Presence.Sum(sample => sample.Count))} players in total)");
    Console.WriteLine($"  availability spans : {games.Sum(game => game.Availability.Count)}");
}

IDirectoryFetcher Fetcher(ImportEtiquette etiquette)
{
    var live = new DirectoryFetcher(http, etiquette, time);

    return arguments.Cache is null ? live : new CachingFetcher(live, arguments.Cache);
}

TimeSpan Interval(IDirectoryFetcher fetcher) => fetcher switch
{
    DirectoryFetcher direct => direct.Gate.EffectiveInterval,
    CachingFetcher caching => caching.Inner.Gate.EffectiveInterval,
    _ => TimeSpan.Zero,
};

// Every source is constructed twice: once by the registry, so its etiquette and tier are read from
// the same place the application reads them, and once here around the fetcher this run wants. There
// is no seam for pointing an existing source at another fetcher, and adding one would be a hole in
// the rule that a source takes its etiquette FROM its fetcher.
IDirectorySource Rebuild(IDirectorySource source, IDirectoryFetcher fetcher) => source switch
{
    TinTinMsspCrawlerSource => new TinTinMsspCrawlerSource(fetcher),
    TinTinMsdpCrawlerSource => new TinTinMsdpCrawlerSource(fetcher),
    MudConnectorSource => new MudConnectorSource(fetcher),
    MudStatsSource => new MudStatsSource(fetcher, time),
    MudVerseSource => new MudVerseSource(fetcher),
    _ => throw new InvalidOperationException($"No live constructor for {source.SourceName}."),
};

/// <summary>The command line, and the environment variables that stand in for each switch.</summary>
internal sealed record Args(string? Source, string? Dsn, string? Cache, bool List, bool Help)
{
    public static Args Parse(string[] args)
    {
        string? source = Environment.GetEnvironmentVariable("MUI_LIVE_SOURCE");
        string? dsn = Environment.GetEnvironmentVariable("MUI_LIVE_DSN");
        string? cache = Environment.GetEnvironmentVariable("MUI_LIVE_CACHE");
        var list = false;
        var help = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--source" when i + 1 < args.Length:
                    source = args[++i];
                    break;
                case "--dsn" when i + 1 < args.Length:
                    dsn = args[++i];
                    break;
                case "--cache" when i + 1 < args.Length:
                    cache = args[++i];
                    break;
                case "--list":
                    list = true;
                    break;
                default:
                    help = true;
                    break;
            }
        }

        return new Args(Blank(source), Blank(dsn), Blank(cache), list, help);
    }

    private static string? Blank(string? value) => value is { Length: > 0 } ? value : null;
}

/// <summary>
/// A fetcher that reads a directory of saved bodies before it reaches for the network, and writes
/// every body it does fetch into the same place.
/// </summary>
/// <remarks>
/// The politeness that matters most in a one-off job is not fetching the same page twice. A MudStats
/// run walks a hundred and forty-five pages fifteen seconds apart; without this, every parsing
/// mistake costs somebody else's server that walk again.
/// </remarks>
internal sealed class CachingFetcher(DirectoryFetcher inner, string directory) : IDirectoryFetcher
{
    public DirectoryFetcher Inner { get; } = inner;

    public ImportEtiquette Etiquette => Inner.Etiquette;

    public Task PrimeRobotsAsync(CancellationToken cancellationToken) =>
        Inner.PrimeRobotsAsync(cancellationToken);

    public async Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken) =>
        await Read(uri, cancellationToken, required: true).ConfigureAwait(false)
        ?? throw new InvalidOperationException($"{uri} returned nothing.");

    public Task<string?> TryGetStringAsync(Uri uri, CancellationToken cancellationToken) =>
        Read(uri, cancellationToken, required: false);

    /// <remarks>
    /// A page the site does not have is cached as an empty file, so that a resumed run does not ask
    /// again for something it already knows is gone.
    /// </remarks>
    private async Task<string?> Read(Uri uri, CancellationToken cancellationToken, bool required)
    {
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, Key(uri));

        if (File.Exists(path))
        {
            var cached = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"    cached  {uri}{(cached.Length == 0 ? " (absent)" : string.Empty)}");

            return cached.Length == 0 && !required ? null : cached;
        }

        var body = required
            ? await Inner.GetStringAsync(uri, cancellationToken).ConfigureAwait(false)
            : await Inner.TryGetStringAsync(uri, cancellationToken).ConfigureAwait(false);

        await File.WriteAllTextAsync(path, body ?? string.Empty, cancellationToken).ConfigureAwait(false);

        Console.WriteLine(body is null
            ? $"    absent  {uri}"
            : $"    fetched {uri} ({body.Length} chars)");

        return body;
    }

    private static string Key(Uri uri) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)))[..32] + ".txt";
}

/// <summary>Replays an already-read listing, so the live pages are fetched exactly once.</summary>
internal sealed class ReplaySource(IDirectorySource source, IReadOnlyList<ImportedGame> games) : IDirectorySource
{
    public string SourceName => source.SourceName;

    public ImportTier Tier => source.Tier;

    public ImportEtiquette Etiquette => source.Etiquette;

    public async IAsyncEnumerable<ImportedGame> ReadAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var game in games)
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return game;

            await Task.Yield();
        }
    }
}

/// <summary>
/// The crawl registry has no Npgsql implementation yet (it is another plan's), so this run holds the
/// seeded targets in memory and reports how many there would be.
/// </summary>
internal sealed class InMemoryTargets : ICrawlTargetRepository
{
    public List<CrawlTarget> Targets { get; } = [];

    public Task<CrawlTarget?> ByAddressAsync(string host, int port, CancellationToken ct) =>
        Task.FromResult(Targets.FirstOrDefault(
            target => CanonicalHost.Same(target.Host, host) && target.Port == port));

    public Task<Guid> AddAsync(CrawlTarget target, CancellationToken ct)
    {
        Targets.Add(target);

        return Task.FromResult(target.Id);
    }

    public Task<IReadOnlyList<CrawlTarget>> DueAsync(DateTimeOffset now, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CrawlTarget>>(Targets);

    public Task RecordAttemptAsync(
        Guid id,
        DateTimeOffset at,
        bool succeeded,
        TimeSpan? crawlDelay,
        DateTimeOffset nextProbeAt,
        CancellationToken ct) =>
        throw new InvalidOperationException("An import never schedules a probe (§7.1).");

    public Task AttachGameAsync(Guid id, Guid gameId, CancellationToken ct) => Task.CompletedTask;
}
