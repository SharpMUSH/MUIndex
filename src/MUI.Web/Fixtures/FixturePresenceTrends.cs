using MUI.Web.Components;
using MUI.Web.Data;

namespace MUI.Web.Fixtures;

/// <summary>
/// A demo trend: invented, deterministic, and carrying all three of §5.4's states on purpose.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here was measured, and every page that draws it says so in the banner — which is the
/// condition under which the fixture is allowed to invent anything at all. <c>FixturePresenceSeries</c>
/// stays empty for §10's JSON, where there is no banner to carry the confession; see
/// <see cref="IPresenceTrends"/> for why the two differ.
/// </para>
/// <para>
/// <b>It includes gaps and uncountable days deliberately.</b> A demo series that was measured every
/// day would show a smooth line and hide the two behaviours this graphic exists to get right —
/// a break where nothing was measured, and a gutter mark where a probe got in and could not read a
/// count. The one deployment somebody can start without a database should be the one that shows
/// them.
/// </para>
/// <para>
/// Deterministic from the game's id, so the same demo game draws the same chart on every render and
/// a screenshot taken today is comparable with one taken tomorrow.
/// </para>
/// </remarks>
public sealed class FixturePresenceTrends : IPresenceTrends
{
    public Task<TrendSeries> ForGameAsync(
        Guid gameId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        // From the bytes rather than from GetHashCode, which is not contracted to be stable across
        // runs — and a demo chart that redraws differently after a restart is not comparable with
        // yesterday's screenshot.
        var seed = Seed(gameId);
        var baseline = 4 + (seed % 22);
        var days = new List<TrendDay>();
        var index = 0;

        for (var date = from; date <= to; date = date.AddDays(1), index++)
        {
            var noise = ((seed / 7) + (index * 37)) % 100;

            // A fortnight nobody measured, and a scattering of days a probe got in and could not
            // read a count. Both are the states the graphic has to keep apart.
            if (noise < 9)
            {
                days.Add(new TrendDay(date, 0, 0, null, null, null));

                continue;
            }

            if (noise is >= 9 and < 13)
            {
                days.Add(new TrendDay(date, 0, 2, null, null, null));

                continue;
            }

            // A weekly shape over a slow drift, because that is what a MU* actually looks like.
            var weekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 4 : 0;
            var drift = index * (seed % 3 - 1) / 30d;
            var mean = Math.Max(0, baseline + weekend + drift + (noise % 5) - 2);
            var low = (int)Math.Max(0, Math.Floor(mean * 0.4));
            var high = (int)Math.Ceiling(mean * 1.6);

            days.Add(new TrendDay(date, 8, 0, low, high, (decimal)Math.Round(mean, 2)));
        }

        return Task.FromResult(new TrendSeries(from, to, days));
    }

    private static int Seed(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes);

        var seed = 17;

        foreach (var b in bytes)
        {
            seed = ((seed * 31) + b) & 0x7fffff;
        }

        return seed;
    }
}
