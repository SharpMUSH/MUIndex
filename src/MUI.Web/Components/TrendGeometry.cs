using System.Globalization;
using System.Text;

namespace MUI.Web.Components;

/// <summary>One column of the trend chart — a day, and the strip of canvas it owns.</summary>
public sealed record TrendColumn(double X, double Width, TrendDay Day);

/// <summary>A day whose neighbours were not measured, drawn as a point because a line needs two.</summary>
public sealed record TrendDot(double X, double Y, string Label);

/// <summary>A day probed all through and never counted: §5.4's hatched state, on the baseline.</summary>
public sealed record TrendTick(double X, double Width, string Label);

/// <summary>
/// The trend chart's geometry, as plain arithmetic over a series.
/// </summary>
/// <remarks>
/// <para>
/// A separate type from the component because the rule this chart has to keep is a geometric one and
/// wants testing as arithmetic rather than as markup: <b>the line is broken wherever a day was not
/// measured</b>. MUDStats and every status-page chart draw one continuous polyline, which
/// interpolates across a gap — and an interpolated gap is our crawl schedule drawn as their quiet
/// fortnight. §5.4's third state is the absence of a measurement, so here it is the absence of ink.
/// </para>
/// <para>
/// No script. The paths are computed on the server and the SVG is inert, which is what lets the
/// same numbers reach a text browser through <see cref="TrendSeries.PerWeek"/> rather than through a
/// second implementation that could disagree with this one.
/// </para>
/// </remarks>
public static class TrendGeometry
{
    /// <summary>The canvas, in user units. The SVG itself scales to whatever box it is given.</summary>
    public const double Width = 720;

    public const double Height = 168;

    /// <summary>Headroom above the peak, so the highest point is not drawn on the frame.</summary>
    private const double TopPad = 8;

    /// <summary>Where the baseline sits, leaving room for the uncountable ticks beneath the plot.</summary>
    private const double Baseline = Height - 14;

    /// <summary>Every day as a column, measured or not — the strip each tooltip belongs to.</summary>
    public static IReadOnlyList<TrendColumn> Columns(TrendSeries series)
    {
        ArgumentNullException.ThrowIfNull(series);

        var width = Width / Math.Max(series.Days.Count, 1);

        return series.Days
            .Select((day, i) => new TrendColumn(i * width, width, day))
            .ToList();
    }

    /// <summary>
    /// The mean line, as one path per unbroken run of measured days.
    /// </summary>
    /// <remarks>
    /// A run of one day produces no path at all — a single point is not a line — and reaches the
    /// reader through <see cref="Dots"/> instead. Returning a one-point path would render as
    /// nothing at all in most engines, which is a measurement silently dropped.
    /// </remarks>
    public static IReadOnlyList<string> MeanPaths(TrendSeries series)
    {
        var ceiling = Ceiling(series);

        return Runs(series)
            .Where(run => run.Count > 1)
            .Select(run => Path(run.Select(c => (X(c), Y(c.Day.Average ?? 0, ceiling)))))
            .ToList();
    }

    /// <summary>
    /// The min–max band, as one closed area per unbroken run.
    /// </summary>
    /// <remarks>
    /// Out along the maxima and back along the minima, so the band shows the spread between a game's
    /// quietest and busiest probe that day. It is the shape the mean alone hides: a game that peaks
    /// at forty and idles at two has the same mean as one that sits at twenty-one all evening, and
    /// they are not the same game.
    /// </remarks>
    public static IReadOnlyList<string> BandPaths(TrendSeries series)
    {
        var ceiling = Ceiling(series);

        return Runs(series)
            .Where(run => run.Count > 1)
            .Select(run =>
            {
                var top = run.Select(c => (X(c), Y(c.Day.Max ?? 0, ceiling)));
                var bottom = run.AsEnumerable().Reverse()
                    .Select(c => (X(c), Y(c.Day.Min ?? 0, ceiling)));

                return Path(top.Concat(bottom)) + " Z";
            })
            .ToList();
    }

    /// <summary>Counted days with no counted neighbour, which no path can carry.</summary>
    public static IReadOnlyList<TrendDot> Dots(TrendSeries series)
    {
        var ceiling = Ceiling(series);

        return Runs(series)
            .Where(run => run.Count == 1)
            .Select(run => new TrendDot(
                X(run[0]),
                Y(run[0].Day.Average ?? 0, ceiling),
                run[0].Day.Label))
            .ToList();
    }

    /// <summary>Days probed and never counted. Beneath the plot, never on the zero line.</summary>
    /// <remarks>
    /// Below the baseline rather than at it, because a mark <em>on</em> the zero line reads as a
    /// measured zero — which is the collapse of §5.4's middle state into a filled cell, and the
    /// worst bug this codebase can ship. It sits in its own gutter and the legend names it.
    /// </remarks>
    public static IReadOnlyList<TrendTick> Ticks(TrendSeries series) =>
        Columns(series)
            .Where(c => c.Day.IsUncountable)
            .Select(c => new TrendTick(c.X, c.Width, c.Day.Label))
            .ToList();

    /// <summary>Where the baseline is drawn, and the floor of the plot.</summary>
    public static double Zero => Baseline;

    /// <summary>
    /// Gridline values and their heights — nought, the peak, and the middle if there is room.
    /// </summary>
    public static IReadOnlyList<(int Value, double Y)> Gridlines(TrendSeries series)
    {
        var ceiling = Ceiling(series);
        var lines = new List<(int, double)> { (0, Y(0, ceiling)), (ceiling, Y(ceiling, ceiling)) };

        if (ceiling >= 4)
        {
            lines.Insert(1, (ceiling / 2, Y(ceiling / 2, ceiling)));
        }

        return lines;
    }

    /// <summary>The axis top. At least one, so a game measured at nought still has a plot.</summary>
    private static int Ceiling(TrendSeries series)
    {
        ArgumentNullException.ThrowIfNull(series);

        return Math.Max(series.Ceiling, 1);
    }

    /// <summary>Unbroken runs of counted days. A day that was not counted ends the run it touches.</summary>
    private static List<List<TrendColumn>> Runs(TrendSeries series)
    {
        var runs = new List<List<TrendColumn>>();
        var current = new List<TrendColumn>();

        foreach (var column in Columns(series))
        {
            if (column.Day.IsCounted)
            {
                current.Add(column);

                continue;
            }

            if (current.Count > 0)
            {
                runs.Add(current);
                current = [];
            }
        }

        if (current.Count > 0)
        {
            runs.Add(current);
        }

        return runs;
    }

    /// <summary>The centre of a column, which is where its day is plotted.</summary>
    private static double X(TrendColumn column) => column.X + (column.Width / 2);

    private static double Y(double value, int ceiling) =>
        Baseline - (value / ceiling * (Baseline - TopPad));

    private static string Path(IEnumerable<(double X, double Y)> points)
    {
        var b = new StringBuilder();

        foreach (var (x, y) in points)
        {
            b.Append(b.Length == 0 ? 'M' : 'L');
            b.Append(Round(x));
            b.Append(' ');
            b.Append(Round(y));
            b.Append(' ');
        }

        return b.ToString().TrimEnd();
    }

    /// <summary>
    /// Two decimals, invariant.
    /// </summary>
    /// <remarks>
    /// The culture is not a detail: SVG path data is comma-and-space separated and a machine running
    /// under a locale that writes <c>0,5</c> would emit coordinates a renderer reads as two numbers.
    /// </remarks>
    private static string Round(double value) =>
        Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
}
