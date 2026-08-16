using MUI.Catalog;
using MUI.Web.Components;

namespace MUI.Web.Data;

/// <summary>
/// A game's presence trend over a calendar range, for the page that draws it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not just <see cref="IPresenceSeries"/>.</b> It is that, over Postgres — see
/// <see cref="PresenceTrends"/>, which is a dozen lines of mapping. The port exists because the two
/// consumers have different obligations. §10's series route publishes JSON, and a JSON body carries
/// no demo banner, so <c>FixturePresenceSeries</c> answers empty over the fixture on purpose: handing
/// a consumer rolled-up buckets of measurements nobody took would be a lie in a format whose whole
/// contract is that a bucket tallies real probes.
/// </para>
/// <para>
/// A page is the other case. Every fixture surface — the heatmap, the counts, the change feed — is
/// invented already, and every page rendering it carries the banner saying so. Making the trend the
/// one graphic that stays blank over the demo would not be more honest; it would just hide the
/// component that most needs looking at from the only deployment somebody can start without a
/// database.
/// </para>
/// <para>
/// So the split is the banner: this port feeds surfaces that carry one, and <see cref="IPresenceSeries"/>
/// feeds the route that cannot.
/// </para>
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
