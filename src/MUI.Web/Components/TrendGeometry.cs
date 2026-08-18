using System.Globalization;
using System.Text;

using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>One column of the trend chart — a day, and the strip of canvas it owns.</summary>
public sealed record TrendColumn(double X, double Width, TrendDay Day);

/// <summary>
/// A day we counted, as the two stacked rectangles that carry it.
/// </summary>
/// <remarks>
/// <see cref="MeanTop"/> is the top of the solid bar — the mean of the counts read that day, grown
/// from the baseline like any magnitude. <see cref="PeakTop"/> is the top of the pale cap above it,
/// which reaches the busiest probe of that day: a game that peaks at forty and idles at two has the
/// same mean as one that sits at twenty-one all evening, and the cap is what tells them apart.
/// </remarks>
public sealed record TrendBar(double X, double Width, double PeakTop, double MeanTop, TrendDay Day)
{
    /// <summary>Where the cap stops, leaving a surface gap so the two segments read as two.</summary>
    public double CapBottom => MeanTop - TrendGeometry.CapGap;

    /// <summary>
    /// Whether the spread is worth a cap at all.
    /// </summary>
    /// <remarks>
    /// Below a couple of user units the cap is thinner than the gap under it and renders as a smudge
    /// on the bar rather than as a fact. The number itself is never lost: the day's own tooltip and
    /// the "read as text" lines carry min, mean and max whatever the chart does with them.
    /// </remarks>
    public bool HasCap => CapBottom - PeakTop >= TrendGeometry.MinimumCap;
}

/// <summary>A day whose neighbours were not measured, drawn as a point because a line needs two.</summary>
public sealed record TrendDot(double X, double Y, string Label);

/// <summary>A day probed all through and never counted: §5.4's hatched state, below the baseline.</summary>
public sealed record TrendTick(double X, double Width, string Label);

/// <summary>A month boundary, as a rule in the plot and a label under it.</summary>
public sealed record TrendMonth(double X, double Fraction, string Label);

/// <summary>
/// The trend chart's geometry, as plain arithmetic over a series.
/// </summary>
/// <remarks>
/// <para>
/// A separate type from the component because the rule this chart has to keep is a geometric one and
/// wants testing as arithmetic rather than as markup: <b>a day nobody measured gets no ink at all</b>.
/// MUDStats and every status-page chart draw one continuous polyline, which interpolates across a
/// gap — and an interpolated gap is our crawl schedule drawn as their quiet fortnight. §5.4's third
/// state is the absence of a measurement, so here it is the absence of a bar.
/// </para>
/// <para>
/// <b>Columns rather than a line, because most games are measured sparsely.</b> The first draft drew
/// a broken line with a min–max band, which is the right picture for a game probed every day and an
/// empty box for everyone else: a game counted on three days of ninety rendered as a single two-pixel
/// dot in a dark rectangle, and a run of one day cannot be a line at all. A bar does not need its
/// neighbours to exist, so a day we measured looks like a day we measured however alone it is.
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

    /// <summary>The surface gap between a bar and the cap above it, so the two segments read apart.</summary>
    public const double CapGap = 1;

    /// <summary>A cap thinner than this is not drawn; the tooltip and the text lines still carry it.</summary>
    public const double MinimumCap = 2;

    /// <summary>
    /// The least ink a counted day may have.
    /// </summary>
    /// <remarks>
    /// A measured zero is a measurement — we got in and nobody was there — and a bar of no height is
    /// indistinguishable from the day beside it that nobody looked at. So every counted day keeps a
    /// sliver on the baseline, which is also what saves a count of one against a ceiling of a
    /// thousand from rounding away to nothing.
    /// </remarks>
    public const double MinimumInk = 2;

    /// <summary>Headroom above the peak, so the highest bar is not drawn against the frame.</summary>
    private const double TopPad = 8;

    /// <summary>Where the baseline sits, leaving room for the uncountable ticks beneath the plot.</summary>
    private const double Baseline = Height - 14;

    /// <summary>The surface gap between neighbouring days.</summary>
    private const double BarGap = 2;

    /// <summary>How much of its slot a bar keeps when the gap would otherwise eat it — a wide range
    /// has columns narrower than the gap, and there the gap gives way rather than the measurement.</summary>
    private const double MinimumBarShare = 0.6;

    /// <summary>The rounded data-end. Only the top corners; a bar is square where it meets zero.</summary>
    private const double CornerRadius = 2;

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
    /// One bar per counted day, and nothing whatever for the days we did not count.
    /// </summary>
    /// <remarks>
    /// There is no interpolation to get wrong here, which is the point of the form: a bar is a
    /// statement about its own day and says nothing at all about the day beside it.
    /// </remarks>
    public static IReadOnlyList<TrendBar> Bars(TrendSeries series)
    {
        var ceiling = Ceiling(series);

        return Columns(series)
            .Where(c => c.Day.IsCounted)
            .Select(c =>
            {
                var width = Math.Max(c.Width - BarGap, c.Width * MinimumBarShare);
                var mean = Math.Min(Y(c.Day.Average ?? 0, ceiling), Baseline - MinimumInk);

                return new TrendBar(
                    c.X + ((c.Width - width) / 2),
                    width,
                    Math.Min(Y(c.Day.Max ?? 0, ceiling), mean),
                    mean,
                    c.Day);
            })
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
    /// quietest and busiest probe that day — the same fact the columns put in their cap, and drawn
    /// in the same colour so a reader who switches shape is not shown two encodings of one number.
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
    public static IReadOnlyList<TrendDot> Dots(string tag, TrendSeries series)
    {
        var ceiling = Ceiling(series);

        return Runs(series)
            .Where(run => run.Count == 1)
            .Select(run => new TrendDot(
                X(run[0]),
                Y(run[0].Day.Average ?? 0, ceiling),
                run[0].Day.Label(tag)))
            .ToList();
    }

    /// <summary>Days probed and never counted. Beneath the plot, never on the zero line.</summary>
    /// <remarks>
    /// Below the baseline rather than at it, because a mark <em>on</em> the zero line reads as a
    /// measured zero — which is the collapse of §5.4's middle state into a filled cell, and the
    /// worst bug this codebase can ship. It sits in its own gutter and the legend names it.
    /// </remarks>
    public static IReadOnlyList<TrendTick> Ticks(string tag, TrendSeries series) =>
        Columns(series)
            .Where(c => c.Day.IsUncountable)
            .Select(c => new TrendTick(c.X, c.Width, c.Day.Label(tag)))
            .ToList();

    /// <summary>
    /// Where a month begins, as a rule in the plot and a label under it.
    /// </summary>
    /// <remarks>
    /// The calendar is what a reader orients by, and without it ninety columns are ninety columns of
    /// nothing in particular. Labels are dropped rather than crowded once they come closer than a
    /// ninth of the canvas, so a five-year range says which years it covers instead of overprinting
    /// sixty month names into a grey smear. The exact ends of the range are printed as words above
    /// the chart, so thinning here loses nothing.
    /// </remarks>
    public static IReadOnlyList<TrendMonth> Months(string tag, TrendSeries series)
    {
        var apart = Width / 9;
        var months = new List<TrendMonth>();

        foreach (var column in Columns(series))
        {
            if (column.Day.Date.Day != 1 || column.X > Width * 0.94)
            {
                continue;
            }

            if (months.Count > 0 && column.X - months[^1].X < apart)
            {
                continue;
            }

            months.Add(new TrendMonth(column.X, column.X / Width, Month(tag, column.Day.Date)));
        }

        return months;
    }

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

    /// <summary>
    /// A column as a path: rounded where the data ends, square where it meets zero.
    /// </summary>
    /// <remarks>
    /// A path rather than a <c>rect</c> with a radius, because a radius rounds all four corners and a
    /// bar with a rounded foot floats off its own baseline.
    /// </remarks>
    public static string Column(double x, double width, double top, double bottom)
    {
        var radius = Math.Min(CornerRadius, Math.Min(width / 2, Math.Max(bottom - top, 0) / 2));
        var right = x + width;
        var b = new StringBuilder();

        Move(b, 'M', x, bottom);
        Move(b, 'L', x, top + radius);
        Curve(b, x, top, x + radius, top);
        Move(b, 'L', right - radius, top);
        Curve(b, right, top, right, top + radius);
        Move(b, 'L', right, bottom);
        b.Append('Z');

        return b.ToString();
    }

    /// <summary>
    /// A number as SVG wants it: two decimals, invariant.
    /// </summary>
    /// <remarks>
    /// The culture is not a detail: SVG path data is comma-and-space separated and a machine running
    /// under a locale that writes <c>0,5</c> would emit coordinates a renderer reads as two numbers.
    /// </remarks>
    public static string Round(double value) =>
        Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>The axis top. At least one, so a game measured at nought still has a plot.</summary>
    private static int Ceiling(TrendSeries series)
    {
        ArgumentNullException.ThrowIfNull(series);

        return Math.Max(series.Ceiling, 1);
    }

    /// <summary>Unbroken runs of counted days. A day that was not counted ends the run it touches.</summary>
    /// <remarks>
    /// The line's half of the rule the columns keep by construction: the break is in the path data,
    /// not in a style, because a CSS dash cannot express "no measurement" and a continuous line under
    /// a faint stroke would still be our crawl schedule drawn as their quiet fortnight.
    /// </remarks>
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

    private static string Path(IEnumerable<(double X, double Y)> points)
    {
        var b = new StringBuilder();

        foreach (var (x, y) in points)
        {
            Move(b, b.Length == 0 ? 'M' : 'L', x, y);
        }

        return b.ToString().TrimEnd();
    }

    /// <summary>
    /// A rule's label: the month, and the year as well where the calendar turns over.
    /// </summary>
    /// <remarks>
    /// Through the message pipeline rather than <c>ToString("MMM")</c>, which reads the month name
    /// off whatever culture the thread happens to be under — the request's, in one place, and the
    /// invariant one in the headless renderer and the tests. The pattern carries the date style, so
    /// a locale that writes the year first says so in its own copy instead of being given an
    /// English ordering with translated words in it.
    /// </remarks>
    private static string Month(string tag, DateOnly date) =>
        Messages.For(
            tag,
            date.Month == 1 ? "trend.axis.monthYear" : "trend.axis.month",
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["d"] = date });

    private static double Y(double value, int ceiling) =>
        Baseline - (value / ceiling * (Baseline - TopPad));

    private static void Move(StringBuilder b, char command, double x, double y)
    {
        b.Append(command);
        b.Append(Round(x));
        b.Append(' ');
        b.Append(Round(y));
        b.Append(' ');
    }

    private static void Curve(StringBuilder b, double cx, double cy, double x, double y)
    {
        b.Append('Q');
        b.Append(Round(cx));
        b.Append(' ');
        b.Append(Round(cy));
        b.Append(' ');
        b.Append(Round(x));
        b.Append(' ');
        b.Append(Round(y));
        b.Append(' ');
    }
}
