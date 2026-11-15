using MUI.Catalog.Persistence;

namespace MUI.Web.Icons;

/// <summary>
/// Fetches the pictures a connect screen names by address (an MXP <c>IMAGE</c>), on the icon
/// refresher's terms.
/// </summary>
/// <remarks>
/// <para>
/// A picture that arrived inline (sixel, Kitty, iTerm2) is stored by the probe and never reaches this
/// loop. One named by address is the same kind of thing as a game's <c>ICON</c>: a URL a stranger
/// chose, fetched so this site can serve it rather than hand every reader's address to that stranger
/// (§11). So it goes through <see cref="IconFetcher"/> — the §7.2 gate, one redirect hop ruled on
/// again, the body read to a ceiling and the type read from the bytes — with larger ceilings, since
/// a picture drawn across a dozen rows of a screen is not a favicon.
/// </para>
/// <para>
/// A failure writes nothing about the game (rule 5). It writes when we tried and when to ask again,
/// with the icon's back-off, so a dead web server is asked as often as a dead icon host and no more.
/// </para>
/// </remarks>
public sealed class ScreenPictureRefresher(
    IScreenPictureStore pictures,
    IconFetcher fetcher,
    TimeProvider time,
    ILogger<ScreenPictureRefresher> logger) : BackgroundService
{
    /// <summary>The most one picture may be, which is migration 0044's ceiling.</summary>
    public const int MaxBytes = 1024 * 1024;

    /// <summary>
    /// The largest picture worth holding in either direction: a full 256-column, 100-row screen at
    /// sixteen pixels a cell, which is more than any connect screen draws.
    /// </summary>
    public const int MaxDimension = 4096;

    private const int PerPass = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(IconRefresher.Interval, time);

        do
        {
            try
            {
                await PassAsync(stoppingToken);
            }
            catch (Exception error)
                when (error is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                // A pass that throws is a pass skipped, never a host stopped; IconRefresher's
                // ExecuteAsync says why the filter asks the token rather than the exception type.
                logger.LogWarning(error, "A screen picture pass failed. The next one runs as scheduled");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One pass: the addresses that are due, fetched one at a time.</summary>
    internal async Task<int> PassAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var due = await pictures.DueAsync(PerPass, now, cancellationToken);
        var fetched = 0;

        foreach (var candidate in due)
        {
            var result = await fetcher.FetchAsync(
                candidate.GameId, candidate.SourceUrl, etag: null, MaxBytes, MaxDimension, cancellationToken);

            if (result is { Outcome: IconFetchOutcome.Fetched, Icon: { } image })
            {
                // Asked again a staleness window later, so a picture replaced at the same address is
                // picked up without a new probe.
                await pictures.StoreFetchedAsync(
                    candidate.GameId,
                    candidate.Ordinal,
                    candidate.SourceUrl,
                    image.ContentType,
                    image.Bytes,
                    now,
                    now + IconRefresher.Stale,
                    cancellationToken);

                fetched++;
                continue;
            }

            await pictures.RecordFailureAsync(
                candidate.GameId,
                candidate.Ordinal,
                candidate.SourceUrl,
                now,
                now + IconRefresher.Backoff(candidate.Failures + 1),
                cancellationToken);
        }

        if (fetched > 0)
        {
            logger.LogInformation("Fetched {Fetched} of {Due} screen pictures", fetched, due.Count);
        }

        return fetched;
    }
}
