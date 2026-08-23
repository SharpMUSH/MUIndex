using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Discovery;

namespace MUI.Web.Data;

/// <summary>
/// What the front page asks about the crawler.
/// </summary>
/// <remarks>
/// Its own seam rather than a method on <c>IGameQueries</c>, since the fixture answers it differently
/// in kind, not value: a demo has no crawler at all.
/// </remarks>
public interface ICrawlerPulse
{
    Task<CrawlerPulse> ReadAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>The newest completed cycles, newest first — the crawler status page's history table.</summary>
    Task<IReadOnlyList<CrawlCycleRecord>> RecentAsync(int count, CancellationToken cancellationToken = default);

    /// <summary>
    /// The soonest-due targets, soonest first — the crawler status page's "next up".
    /// </summary>
    /// <remarks>
    /// The registry's own address, never a game name: most due targets have not resolved to a game at
    /// all, and one that has may still be a submission nobody has vouched for — this is operator
    /// diagnostics (the page's own framing), not a public listing, so it is not filtered the way
    /// <see cref="IGameQueries.FeedsAsync"/> is.
    /// </remarks>
    Task<IReadOnlyList<DueTarget>> DueSoonAsync(
        DateTimeOffset now, int count, CancellationToken cancellationToken = default);

    /// <summary>
    /// What the loop got through over a span — the status page's headline figure for its own
    /// throughput — or null when it could not be read.
    /// </summary>
    /// <remarks>
    /// <b>Null and <see cref="CrawlWindow.Empty"/> are different answers and may not be merged.</b>
    /// Empty is a measurement: the query ran and no cycle finished inside the span, which is a true
    /// thing to print. Null is the absence of one, and a zero printed in its place is our own failed
    /// read dressed as the instrument having done nothing — on the one page whose job is saying
    /// whether the instrument is working, beside a pulse that already reads "quiet". That is rule 4
    /// ("an unreadable WHO yields unknown, never zero") turned on ourselves, and it is why the
    /// return type is nullable rather than a convenient zero: every caller has to answer for it.
    /// </remarks>
    Task<CrawlWindow?> WindowAsync(
        DateTimeOffset now, TimeSpan span, CancellationToken cancellationToken = default);
}

/// <summary>One address queued for its next probe, as the status page shows it.</summary>
public sealed record DueTarget(string Host, int Port, DateTimeOffset NextProbeAt);

/// <summary>Reads the pulse from what the crawler wrote.</summary>
/// <remarks>
/// A missing table and a failed query are both "we cannot say", never "it is not running" — the catch
/// is broad and falls back to <see cref="CrawlerPulse.Unknown"/>, so an unavailable strip never costs
/// a reader the front page. The schema check is cached after it first succeeds, since migrations only
/// move forward.
/// </remarks>
public sealed class StoredCrawlerPulse(
    ICrawlCycles cycles,
    ICrawlTargetRepository targets,
    ILogger<StoredCrawlerPulse>? logger = null) : ICrawlerPulse
{
    private bool _installed;

    public async Task<CrawlerPulse> ReadAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_installed && !await cycles.IsInstalledAsync(cancellationToken))
            {
                return CrawlerPulse.Unknown;
            }

            _installed = true;

            return await cycles.PulseAsync(now, cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException
            || !cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning(error, "Could not read the crawler pulse; the front page omits the strip");

            return CrawlerPulse.Unknown;
        }
    }

    public async Task<IReadOnlyList<CrawlCycleRecord>> RecentAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await cycles.RecentAsync(count, cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException
            || !cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning(error, "Could not read the crawler's cycle history; the status page omits it");

            return [];
        }
    }

    public async Task<CrawlWindow?> WindowAsync(
        DateTimeOffset now,
        TimeSpan span,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await cycles.WindowAsync(now, span, cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException
            || !cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning(error, "Could not read the crawler's window; the status page omits it");

            // Null rather than an empty window: see the interface. A failed read is not a measured
            // zero, and this is the fallback the sibling methods' empty lists cannot express here —
            // an empty list renders as no rows, but an empty window renders as the figure 0.
            return null;
        }
    }

    public async Task<IReadOnlyList<DueTarget>> DueSoonAsync(
        DateTimeOffset now,
        int count,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var due = await targets.DueAsync(now, count, cancellationToken);

            return [.. due.Select(t => new DueTarget(t.Host, t.Port, t.NextProbeAt))];
        }
        catch (Exception error) when (error is not OperationCanceledException
            || !cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning(error, "Could not read the crawler's due targets; the status page omits them");

            return [];
        }
    }
}
