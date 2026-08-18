using MUI.Catalog;
using MUI.Catalog.Persistence;

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
}

/// <summary>Reads the pulse from what the crawler wrote.</summary>
/// <remarks>
/// A missing table and a failed query are both "we cannot say", never "it is not running" — the catch
/// is broad and falls back to <see cref="CrawlerPulse.Unknown"/>, so an unavailable strip never costs
/// a reader the front page. The schema check is cached after it first succeeds, since migrations only
/// move forward.
/// </remarks>
public sealed class StoredCrawlerPulse(
    ICrawlCycles cycles,
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
}
