using System.Globalization;

using Dapper;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Discovery;
using MUI.Import;
using MUI.Import.Sources;

using Npgsql;

// A one-off, deliberately manual verification: read the live TinTin++ MSSP crawler page ONCE, run it
// through the real pipeline into a real PostgreSQL, and print what landed.
//
// This is not a test and is not in the suite — every test in this repository parses a committed
// fixture and no test may touch the network. It exists so that "the parser works against the page as
// it actually is today" is a claim somebody made by doing it, and it is kept so the next person can
// repeat it without writing it again.
//
//   MUI_LIVE_DSN=... dotnet run --project tools/live-tintin-import
//
// With no DSN it parses and reports and writes nothing.

var connectionString = Environment.GetEnvironmentVariable("MUI_LIVE_DSN");

// A saved copy of the page, so a second look at the same data costs the site nothing. Fetching the
// live page twice to check one parsing change is precisely the manners this whole component is about.
var saved = Environment.GetEnvironmentVariable("MUI_LIVE_FILE");

var time = TimeProvider.System;
var etiquette = TinTinMsspCrawlerSource.DefaultEtiquette();
List<ImportedGame> games;

Console.WriteLine($"user agent : {etiquette.UserAgent}");
Console.WriteLine($"route      : {EtiquettePlanner.Decide(etiquette).Route} {EtiquettePlanner.Decide(etiquette).Uri}");

if (saved is { Length: > 0 })
{
    Console.WriteLine($"source     : saved copy at {saved} — no request made");
    games = TinTinMsspCrawlerSource.Parse(await File.ReadAllTextAsync(saved)).ToList();
}
else
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    var fetcher = new DirectoryFetcher(http, etiquette, time);

    await fetcher.PrimeRobotsAsync(CancellationToken.None);

    Console.WriteLine($"robots     : read; effective interval {fetcher.Gate.EffectiveInterval}");

    games = [];
    await foreach (var game in new TinTinMsspCrawlerSource(fetcher).ReadAsync(CancellationToken.None))
    {
        games.Add(game);
    }
}

var endpoints = games.SelectMany(game => game.Endpoints).ToList();

Console.WriteLine();
Console.WriteLine($"hosts found       : {games.Count}");
Console.WriteLine($"endpoints         : {endpoints.Count} "
    + $"({endpoints.Select(endpoint => endpoint.Host).Distinct(StringComparer.OrdinalIgnoreCase).Count()} distinct hosts)");
Console.WriteLine($"with a name       : {games.Count(game => game.Fields.ContainsKey("NAME"))}");
Console.WriteLine($"with a codebase   : {games.Count(game => game.Fields.ContainsKey("CODEBASE"))}");
Console.WriteLine($"with a website    : {games.Count(game => game.Fields.ContainsKey("WEBSITE"))}");
Console.WriteLine($"presence readings : {games.Sum(game => game.Presence.Count)}");
Console.WriteLine($"availability spans: {games.Sum(game => game.Availability.Count)}");
Console.WriteLine($"players (sum)     : {games.Sum(game => game.Presence.Sum(sample => sample.Count))}");

if (connectionString is null)
{
    Console.WriteLine();
    Console.WriteLine("No MUI_LIVE_DSN; parsed only, wrote nothing.");

    return;
}

await using var dataSource = NpgsqlDataSource.Create(connectionString);
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

var report = await pipeline.RunAsync(new ReplaySource(etiquette, games), CancellationToken.None);

Console.WriteLine();
Console.WriteLine(report);

foreach (var note in report.Notes)
{
    Console.WriteLine($"  · {note}");
}

await using var connection = await dataSource.OpenConnectionAsync();
var stored = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM import_provenance");

Console.WriteLine($"provenance rows: {stored.ToString(CultureInfo.InvariantCulture)}");

/// <summary>Replays an already-read listing, so the live page is fetched exactly once.</summary>
internal sealed class ReplaySource(ImportEtiquette etiquette, IReadOnlyList<ImportedGame> games) : IDirectorySource
{
    public string SourceName => TinTinMsspCrawlerSource.Name;

    public ImportTier Tier => ImportTier.Measured;

    public ImportEtiquette Etiquette => etiquette;

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
