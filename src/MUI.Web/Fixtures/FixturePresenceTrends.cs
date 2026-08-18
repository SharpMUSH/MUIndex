using MUI.Web.Components;
using MUI.Web.Data;

namespace MUI.Web.Fixtures;

/// <summary>
/// A demo trend: invented, deterministic, and carrying all three of §5.4's states on purpose.
/// </summary>
/// <remarks>
/// Every page that draws this says so in the banner, which is the condition under which the fixture
/// is allowed to invent anything at all; <see cref="FixturePresenceSeries"/> stays empty instead,
/// since §10's JSON has no banner to carry that confession. Includes gaps and uncountable days
/// deliberately, so the no-database deployment still shows both break states. Deterministic from the
/// game's id, so a screenshot taken today is comparable with one taken tomorrow.
/// </remarks>
public sealed class FixturePresenceTrends : IPresenceTrends
{
    public Task<TrendSeries> ForGameAsync(
        Guid gameId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        // From the bytes rather than GetHashCode, which is not contracted to be stable across runs.
        var seed = Seed(gameId);
        var baseline = 4 + (seed % 22);
        var days = new List<TrendDay>();
        var index = 0;

        for (var date = from; date <= to; date = date.AddDays(1), index++)
        {
            var noise = ((seed / 7) + (index * 37)) % 100;

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
