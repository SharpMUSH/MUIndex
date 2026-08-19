using MUI.Catalog;
using MUI.Web.Components;

namespace MUI.Web.Data;

/// <summary>
/// A game's presence trend over a calendar range, for the page that draws it.
/// </summary>
/// <remarks>
/// A separate port from <see cref="IPresenceSeries"/> because the two consumers differ in whether they
/// carry a demo banner: §10's JSON route does not, so its fixture stays empty rather than inventing
/// measurements; a page always does, so its fixture can safely invent a trend.
/// </remarks>
public interface IPresenceTrends
{
    /// <summary>One game's daily presence across the range, inclusive of both ends.</summary>
    Task<TrendSeries> ForGameAsync(
        Guid gameId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}

/// <summary>The measured trend: the day rollup, filled to a column per day.</summary>
/// <remarks>
/// The day grain rather than the hour, because §5.2 keeps it for ever — a chart a reader can seek
/// back through five years of must not be drawn off the grain retention is allowed to drop.
/// </remarks>
public sealed class PresenceTrends(IPresenceSeries series) : IPresenceTrends
{
    public async Task<TrendSeries> ForGameAsync(
        Guid gameId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(series);

        var buckets = await series.ForGameAsync(
            gameId,
            PresenceGrain.Day,
            new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            new DateTimeOffset(to.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            cancellationToken);

        return TrendSeries.Over(from, to, buckets);
    }
}
