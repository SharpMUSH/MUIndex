using MUI.Catalog.Persistence;

namespace MUI.Web.Icons;

/// <summary>
/// Keeps the icon cache current, on a schedule of its own (spec §8.5, icons).
/// </summary>
/// <remarks>
/// <para>
/// <b>Not on page render, and this is the whole reason it is a service rather than a lazy read.</b>
/// Fetching when somebody opens a game page would make a reader's page load wait on a stranger's web
/// server; doing it on the listing would turn one request into a fan-out to fifty hosts. Neither is a
/// thing to do to a reader, and the second is not a thing to do to fifty operators either.
/// </para>
/// <para>
/// <b>Not on the crawl cycle either, though that was the obvious home.</b> The crawler is telnet: it
/// opens sockets, speaks a protocol and writes measurements, and every component in
/// <c>MUI.Crawler</c> is testable against a captured <c>ProbeResult</c> with no network involved.
/// Hanging an HTTP client off the cycle would put a second protocol, a second failure mode and a
/// second set of timeouts inside the loop whose reachability numbers are the site's core claim — and
/// an icon that is late is a decoration that is late, while a probe that is late is a gap in a
/// measured record.
/// </para>
/// <para>
/// <b>One replica too many is harmless here, unlike the crawl.</b> The crawler takes a Postgres
/// advisory lock because two crawlers would double every game's probe rate; two refreshers would at
/// worst fetch the same icon twice a day. No lock, therefore, and no lease: the work is idempotent
/// and the cost of duplicating it is one HTTP request.
/// </para>
/// </remarks>
public sealed class IconRefresher(
    IIconStore icons,
    IconFetcher fetcher,
    TimeProvider time,
    ILogger<IconRefresher> logger) : BackgroundService
{
    /// <summary>How long an icon may go unfetched before it is worth asking again.</summary>
    /// <remarks>
    /// A week, because an icon is a decoration a game changes rarely and a URL whose contents moved
    /// is not a fact anybody is waiting on. A <em>changed</em> <c>ICON</c> field does not wait for
    /// this — <see cref="IIconStore.DueAsync"/> sorts those first, and the next pass takes them.
    /// </remarks>
    public static readonly TimeSpan Stale = TimeSpan.FromDays(7);

    /// <summary>How often a pass runs, and how many icons one pass will fetch.</summary>
    /// <remarks>
    /// Deliberately unhurried. There is no deadline here: a game that publishes an icon today and
    /// sees it tomorrow has lost nothing, and a refresher that hammered through the catalogue would
    /// be spending fifty operators' bandwidth to save a reader nothing.
    /// </remarks>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    private const int PerPass = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);

        do
        {
            try
            {
                await PassAsync(stoppingToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // A pass that throws is a pass skipped, never a service that stops. The icons are a
                // decoration and this loop outliving them is worth more than any one failure.
                logger.LogWarning(error, "An icon refresh pass failed. The next one runs as scheduled");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// One pass: the icons most worth fetching, fetched one at a time.
    /// </summary>
    /// <remarks>
    /// Sequential rather than parallel, on purpose. Twenty concurrent requests from one host is the
    /// shape of something a web server's operator blocks, and there is nothing here worth hurrying —
    /// see <see cref="Interval"/>.
    /// </remarks>
    internal async Task<int> PassAsync(CancellationToken cancellationToken)
    {
        var due = await icons.DueAsync(PerPass, time.GetUtcNow() - Stale, cancellationToken);
        var fetched = 0;

        foreach (var candidate in due)
        {
            // The stored ETag is only worth sending when the URL has not moved. Asking the new
            // address whether the old address's copy has changed would let a far end that ignores
            // conditional requests hand us a 304 for a picture we have never seen.
            var etag = string.Equals(candidate.CachedUrl, candidate.DeclaredUrl, StringComparison.Ordinal)
                ? candidate.ETag
                : null;

            if (await fetcher.FetchAsync(candidate.GameId, candidate.DeclaredUrl, etag, cancellationToken)
                is not { } icon)
            {
                // Refused, unreachable, unchanged or not an image we serve. Nothing is written in any
                // of those cases, and nothing about it enters the game's record (rule 5).
                continue;
            }

            await icons.UpsertAsync(icon, cancellationToken);
            fetched++;
        }

        if (fetched > 0)
        {
            logger.LogInformation("Refreshed {Fetched} of {Due} icons", fetched, due.Count);
        }

        return fetched;
    }
}
