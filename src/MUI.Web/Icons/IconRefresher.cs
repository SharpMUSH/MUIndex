using MUI.Catalog.Persistence;

namespace MUI.Web.Icons;

/// <summary>
/// Keeps the icon cache current, on a schedule of its own (spec §8.5, icons).
/// </summary>
/// <remarks>
/// Not on page render — fetching on open or listing would make a reader's load wait on a stranger's
/// web server, or fan one request out to fifty hosts. Not on the crawl cycle either: that loop's
/// reachability numbers are the site's core claim, and a late icon is just a late decoration where a
/// late probe is a gap in a measured record. No lock or lease, unlike the crawler: two refreshers
/// would at worst fetch the same icon twice a day.
/// </remarks>
public sealed class IconRefresher(
    IIconStore icons,
    IconFetcher fetcher,
    TimeProvider time,
    ILogger<IconRefresher> logger) : BackgroundService
{
    /// <summary>How long an icon may go unfetched before it is worth asking again.</summary>
    /// <remarks>
    /// A <em>changed</em> <c>ICON</c> field does not wait for this — <see cref="IIconStore.DueAsync"/>
    /// sorts those first, and the next pass takes them.
    /// </remarks>
    public static readonly TimeSpan Stale = TimeSpan.FromDays(7);

    /// <summary>How often a pass runs, and how many icons one pass will fetch.</summary>
    /// <remarks>
    /// Deliberately unhurried: a game that publishes an icon today and sees it tomorrow has lost
    /// nothing, and hammering through the catalogue would spend operators' bandwidth to save nothing.
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
            catch (Exception error)
                when (error is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                // A pass that throws is a pass skipped, never a service that stops. See IconFetcher's
                // FetchAsync for why the filter asks the token who cancelled, never the exception type.
                logger.LogWarning(error, "An icon refresh pass failed. The next one runs as scheduled");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// One pass: the icons most worth fetching, fetched one at a time.
    /// </summary>
    /// <remarks>
    /// Sequential rather than parallel: twenty concurrent requests from one host is the shape of
    /// something a web server's operator blocks.
    /// </remarks>
    internal async Task<int> PassAsync(CancellationToken cancellationToken)
    {
        var due = await icons.DueAsync(PerPass, time.GetUtcNow() - Stale, cancellationToken);
        var fetched = 0;

        foreach (var candidate in due)
        {
            // The stored ETag is only worth sending when the URL has not moved.
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
