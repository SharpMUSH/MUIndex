using MUI.Catalog;
using MUI.Catalog.Persistence;

namespace MUI.Web.Data;

/// <summary>
/// What the front page asks about the crawler.
/// </summary>
/// <remarks>
/// Its own seam rather than a method on <c>IGameQueries</c>, because the fixture answers it
/// differently in kind and not merely in value: a demo has no crawler at all, and folding that into
/// the games interface would put a null-shaped special case in every implementation of it.
/// </remarks>
public interface ICrawlerPulse
{
    Task<CrawlerPulse> ReadAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

/// <summary>Reads the pulse from what the crawler wrote.</summary>
/// <remarks>
/// <para>
/// <b>A missing table and a failed query are both "we cannot say", never "it is not running".</b>
/// This is on the path of the most-served document on the site, and it is a decoration on it: the
/// front page's job is the games, and an unavailable strip may not cost a reader the page. The
/// catch is therefore broad and the fallback is <see cref="CrawlerPulse.Unknown"/>, which renders as
/// nothing at all rather than as an alarm.
/// </para>
/// <para>
/// The schema check is cached after it first succeeds. It cannot go back to false — migrations only
/// move forward — so asking Postgres on every front-page render would be a round trip to learn
/// something that stopped being able to change.
/// </para>
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
