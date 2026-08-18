using System.Globalization;

using MUI.Catalog;
using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>
/// The heatmap said in words, before the graphic and instead of it.
/// </summary>
/// <remarks>
/// The sentence is the accessible summary and arrives first — a screen reader should not have to walk
/// 168 cells to learn what one paragraph can say. It keeps spec §5.4's three states apart in words:
/// <em>not measured</em> (no presence row — a dial we could not complete or never attempted) and
/// <em>could not be counted</em> (probed but unreadable) are different facts about a game, and
/// conflating them is the worst bug this codebase can ship. An hour with no presence row is always
/// said as <em>not measured</em>, never <em>not reachable</em> — this sentence once described a game
/// measured once, and found perfectly reachable, as unreachable for 167 hours of the week.
/// Reachability is the strip's job, derived from intervals that can tell the two apart.
/// </remarks>
public static class ActivitySummary
{
    /// <summary>
    /// How many days of the week must carry a measurement before the grid is worth drawing.
    /// </summary>
    /// <remarks>
    /// A 7×24 grid with one probe in it is 167 empty cells and one number — "not measured" announced
    /// 167 times before the fact arrives. Below the threshold the sentence is the whole panel.
    /// </remarks>
    public const int MeasuredDaysForGrid = 7;

    /// <summary>
    /// The name of a day, in the reader's language.
    /// </summary>
    /// <remarks>
    /// From CLDR by way of <see cref="Locales.CultureOf"/>, not a compiled-in array — seven day names
    /// hard-coded here are seven strings no translator is ever sent. Monday is 0 in this codebase
    /// (how the store keys a week); .NET's arrays start at Sunday, and <see cref="FromSunday"/> is
    /// the whole of the difference.
    /// </remarks>
    public static string DayName(string tag, int dayOfWeek) =>
        Locales.CultureOf(tag).DateTimeFormat.DayNames[FromSunday(dayOfWeek)];

    /// <summary>The abbreviated day name, for a row header where a column is three characters.</summary>
    public static string ShortDayName(string tag, int dayOfWeek) =>
        Locales.CultureOf(tag).DateTimeFormat.AbbreviatedDayNames[FromSunday(dayOfWeek)];

    /// <summary>
    /// What one cell is, as a sentence. The <c>title</c> a mouse reader gets and the text a screen
    /// reader gets are the same string, because they are the same fact.
    /// </summary>
    public static string CellLabel(string tag, ActivityCell cell) => cell switch
    {
        { IsGap: true } => Messages.Say(tag, "activity.cell.notMeasured", When(tag, cell)),
        { IsUnmeasurable: true } => Messages.Say(tag, "activity.cell.notCounted", When(tag, cell)),

        // One message for all three counted cases: =0 is an exact match in ICU, and a measured zero
        // is still a count.
        _ => Messages.Say(tag, "activity.cell.counted", [.. When(tag, cell), ("count", cell.Count!.Value)]),
    };

    private static (string Key, object? Value)[] When(string tag, ActivityCell cell) =>
        [("day", ShortDayName(tag, cell.DayOfWeek)), ("time", Time(cell.Hour))];

    /// <summary>
    /// What one cell announces when a reader arrows onto it: a value, not a sentence.
    /// </summary>
    /// <remarks>
    /// Row and column headers already say which day and hour it is, so repeating them here would turn
    /// 168 cells into 168 paragraphs. "Not counted" and "not measured" are different facts and neither
    /// is a zero — locked glossary ids, so no locale can quietly render both as *unavailable*.
    /// </remarks>
    public static string CellValue(string tag, ActivityCell cell) => cell switch
    {
        { IsGap: true } => Messages.For(tag, "state.notMeasured"),
        { IsUnmeasurable: true } => Messages.For(tag, "state.notCounted"),

        // Machine output stays in Western digits, like every numeric column on this site — that's
        // the only reason it aligns as a column.
        _ => cell.Count!.Value.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>The sentence that sits above the grid.</summary>
    public static string Sentence(string tag, IReadOnlyList<ActivityCell> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        if (cells.Count == 0)
        {
            return Messages.For(tag, "activity.none");
        }

        var parts = new List<string>(4);
        var counted = cells.Where(c => c.IsCounted).ToList();

        if (counted.Count == 0)
        {
            parts.Add(Messages.For(tag, "activity.noCount"));
        }
        else if (counted.All(c => c.Count == 0))
        {
            // A measured zero everywhere is still a measurement; it must not read as absence of data.
            parts.Add(Messages.For(tag, "activity.allZero"));
        }
        else
        {
            parts.Add(Busiest(tag, counted));
            var quiet = Quietest(tag, counted);
            if (quiet is not null)
            {
                parts.Add(quiet);
            }
        }

        var gaps = cells.Where(c => c.IsGap).ToList();
        if (gaps.Count > 0)
        {
            // "Not measured", never "not reachable" — see ActivityCell.IsGap. Reachability is stated
            // by the strip beside this grid.
            parts.Add(WhereFrom(tag, gaps, "activity.gap.day", "activity.gap.week"));
        }

        var unmeasurable = cells.Where(c => c.IsUnmeasurable).ToList();
        if (unmeasurable.Count > 0)
        {
            parts.Add(WhereFrom(tag, unmeasurable, "activity.uncounted.day", "activity.uncounted.week"));
        }

        return string.Join(' ', parts);
    }

    /// <summary>
    /// How many days of the week carry a measurement of any kind — counted or merely probed.
    /// </summary>
    public static int MeasuredDays(IReadOnlyList<ActivityCell> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        return cells.Where(c => !c.IsGap).Select(c => c.DayOfWeek).Distinct().Count();
    }

    /// <summary>
    /// What there is, when there is not yet enough of it to draw. Two sentences and no grid.
    /// </summary>
    /// <remarks>
    /// States what exists rather than what is missing, and never says the game was unreachable — an
    /// hour with no presence row covers a failed dial and one we never attempted alike, and naming
    /// either would file our crawl schedule as a fact about somebody's game.
    /// </remarks>
    public static string Sparse(string tag, IReadOnlyList<ActivityCell> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        var measured = cells.Where(c => !c.IsGap).ToList();
        var counted = measured.Where(c => c.IsCounted).ToList();
        var days = MeasuredDays(cells);

        var what = counted.Count switch
        {
            0 when measured.Count == 0 => Messages.For(tag, "activity.sparse.none"),
            0 => Messages.Say(tag, "activity.sparse.uncounted", ("count", measured.Count)),
            _ => Sample(tag, counted, measured.Count - counted.Count),
        };

        var still = days == 0
            ? Messages.For(tag, "activity.sparse.wait")
            : Messages.Say(tag, "activity.sparse.days", ("days", days));

        return $"{what} {still}";
    }

    /// <summary>The busiest hour we have, said as a fact about that one hour rather than a pattern.</summary>
    private static string Sample(string tag, IReadOnlyList<ActivityCell> counted, int uncountable)
    {
        var peak = counted.MaxBy(c => c.Count)!;

        var sentence = peak.Count == 0
            ? Messages.Say(tag, "activity.sample.zero", ("count", counted.Count))
            : Messages.Say(
                tag,
                "activity.sample.peak",
                ("count", counted.Count),
                ("peak", peak.Count!.Value),
                ("day", DayName(tag, peak.DayOfWeek)),
                ("time", Time(peak.Hour)));

        return uncountable == 0
            ? sentence
            : $"{sentence} {Messages.Say(tag, "activity.sample.more", ("count", uncountable))}";
    }

    /// <summary>
    /// One day of the week, as facts rather than as a sentence.
    /// </summary>
    /// <param name="DayOfWeek">Monday is 0, as the store keys it.</param>
    /// <param name="Quietest">The lowest count measured that day, or null where none was.</param>
    /// <param name="Busiest">The highest count measured that day, or null where none was.</param>
    /// <param name="PeakHour">The hour <paramref name="Busiest"/> was measured in.</param>
    /// <param name="NobodyOn">The longest run of hours measured at zero, as <c>03:00–09:59</c>.</param>
    /// <param name="NotMeasured">Hours with no measurement at all.</param>
    /// <param name="NotCounted">Hours that answered and produced no count.</param>
    public sealed record DayLine(
        int DayOfWeek,
        int? Quietest,
        int? Busiest,
        int? PeakHour,
        string? NobodyOn,
        int NotMeasured,
        int NotCounted)
    {
        /// <summary>
        /// The row's own header, in the reader's language.
        /// </summary>
        /// <remarks>A method taking the locale, not a property — the record itself holds numbers,
        /// not language.</remarks>
        public string Name(string tag) => ShortDayName(tag, DayOfWeek);
    }

    /// <summary>
    /// One row per day — seven rows, not a hundred and sixty-eight cells.
    /// </summary>
    /// <remarks>
    /// The source of both the table under the drawing and the <c>?plain=1</c> mirror, so they cannot
    /// disagree. Spec §5.4's three states each get a column of their own.
    /// </remarks>
    public static IReadOnlyList<DayLine> Days(IReadOnlyList<ActivityCell> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        var lines = new List<DayLine>(7);

        foreach (var day in cells.Select(c => c.DayOfWeek).Distinct().Order())
        {
            var forDay = cells.Where(c => c.DayOfWeek == day).OrderBy(c => c.Hour).ToList();
            var counted = forDay.Where(c => c.IsCounted).ToList();
            var peak = counted.MaxBy(c => c.Count);
            var quiet = LongestRun(forDay, c => c.IsCounted && c.Count == 0);

            lines.Add(new DayLine(
                day,
                counted.Count > 0 ? counted.Min(c => c.Count) : null,
                peak?.Count,
                peak?.Hour,
                quiet is { } q ? Window(q) : null,
                forDay.Count(c => c.IsGap),
                forDay.Count(c => c.IsUnmeasurable)));
        }

        return lines;
    }

    /// <summary>One line per day, for plain mode — the same rows as <see cref="Days"/>, in prose.</summary>
    public static IReadOnlyList<string> PerDay(string tag, IReadOnlyList<ActivityCell> cells)
    {
        var lines = new List<string>(7);

        foreach (var day in Days(cells))
        {
            var clauses = new List<string>(4);

            if (day.Busiest is { } busiest)
            {
                clauses.Add(busiest == 0
                    ? Messages.For(tag, "activity.day.allZero")
                    : Messages.Say(tag, "activity.day.peak", ("count", busiest), ("time", Time(day.PeakHour ?? 0))));

                if (day.NobodyOn is { } quiet)
                {
                    clauses.Add(Messages.Say(tag, "activity.day.nobodyOn", ("window", quiet)));
                }
            }
            else
            {
                clauses.Add(Messages.For(tag, "activity.day.noCount"));
            }

            if (day.NotMeasured > 0)
            {
                clauses.Add(Messages.Say(tag, "activity.day.notMeasured", ("count", day.NotMeasured)));
            }

            if (day.NotCounted > 0)
            {
                clauses.Add(Messages.Say(tag, "activity.day.notCounted", ("count", day.NotCounted)));
            }

            lines.Add(Messages.Say(
                tag,
                "activity.day.line",
                ("day", day.Name(tag)),
                ("facts", Fold(tag, "activity.day.facts", "first", "second", clauses))));
        }

        return lines;
    }

    private static string Busiest(string tag, IReadOnlyList<ActivityCell> counted)
    {
        var byHour = counted
            .GroupBy(c => c.Hour)
            .ToDictionary(g => g.Key, g => g.Average(c => c.Count!.Value));
        var top = byHour.Values.Max();

        // The longest contiguous run of hours within a quarter of the best hour, so the sentence
        // names a window rather than a scatter of hours.
        var busyHours = byHour.Where(kv => kv.Value >= top * 0.75).Select(kv => kv.Key).ToHashSet();
        var band = LongestRun(Enumerable.Range(0, 24), busyHours.Contains) ?? new Run(0, 23);

        var inBand = counted.Where(c => c.Hour >= band.From && c.Hour <= band.To).ToList();
        var byDay = inBand
            .GroupBy(c => c.DayOfWeek)
            .ToDictionary(g => g.Key, g => g.Average(c => c.Count!.Value));
        var bestDay = byDay.Values.Max();
        var busyDays = byDay.Where(kv => kv.Value >= bestDay * 0.9).Select(kv => kv.Key).Order().ToList();

        // Plural here ("evenings") vs. singular in the quiet sentence below — two ids rather than an
        // English pluralisation rule applied to one.
        var part = PartOfDay(band) is { } named ? Part(tag, named, plural: true) : null;
        var window = Window(band);

        if (busyDays.Count == 7)
        {
            return part is null
                ? Messages.Say(tag, "activity.busiest.everyDay", ("window", window))
                : Messages.Say(tag, "activity.busiest.everyDay.part", ("part", part), ("window", window));
        }

        var days = JoinDays(tag, busyDays);

        return part is null
            ? Messages.Say(tag, "activity.busiest.days", ("days", days), ("window", window))
            : Messages.Say(tag, "activity.busiest.days.part", ("days", days), ("part", part), ("window", window));
    }

    private static string? Quietest(string tag, IReadOnlyList<ActivityCell> counted)
    {
        var byHour = counted
            .GroupBy(c => c.Hour)
            .ToDictionary(g => g.Key, g => g.Average(c => c.Count!.Value));
        var top = byHour.Values.Max();
        if (top <= 0)
        {
            return null;
        }

        // "Quiet" means *nobody, or all but nobody*, not "a lot less than the peak" — three people in
        // a busy room is not quiet. Try the strongest reading first (a run measured at exactly zero),
        // then fall back to a near-zero floor.
        var floor = Math.Max(0.5, top * 0.1);
        var band = LongestRun(Enumerable.Range(0, 24), h => byHour.TryGetValue(h, out var v) && v == 0)
            ?? LongestRun(Enumerable.Range(0, 24), h => byHour.TryGetValue(h, out var v) && v < floor);

        if (band is not { } quiet || quiet.Length < 2)
        {
            return null;
        }

        var inBand = counted.Where(c => c.Hour >= quiet.From && c.Hour <= quiet.To).ToList();
        var daysWithData = inBand.Select(c => c.DayOfWeek).Distinct().Count();
        var totalDays = counted.Select(c => c.DayOfWeek).Distinct().Count();

        var quietDays = inBand
            .GroupBy(c => c.DayOfWeek)
            .Where(g => g.Average(c => c.Count!.Value) < floor)
            .Select(g => g.Key)
            .Order()
            .ToList();

        if (quietDays.Count == 0)
        {
            return null;
        }

        var window = Window(quiet);
        var part = PartOfDay(quiet) is { } named ? Part(tag, named, plural: false) : null;
        var weekdays = quietDays.Count == 5 && quietDays.All(d => Wrap(d) < 5);

        // "Every day" would be a claim about a day whose hours in this band were never measured.
        var who = quietDays.Count == daysWithData
            ? Messages.For(tag, daysWithData < totalDays
                ? "activity.quiet.everyMeasuredDay"
                : "activity.quiet.everyDay")
            : weekdays
                ? Messages.For(tag, "activity.quiet.weekdays")
                : Messages.Say(tag, "activity.quiet.onDays", ("days", JoinDays(tag, quietDays)));

        return part is null
            ? Messages.Say(tag, "activity.quiet", ("who", who), ("window", window))
            : Messages.Say(tag, "activity.quiet.part", ("who", who), ("part", part), ("window", window));
    }

    /// <summary>Whether a band of hours has a name a person would use for it.</summary>
    /// <remarks>Returns an id's last segment, not a word — the word depends on the reader.</remarks>
    private static string? PartOfDay(Run band) => (band.From, band.To) switch
    {
        ( >= 5 and <= 8, <= 11) => "morning",
        ( >= 11 and <= 13, >= 12 and <= 17) => "afternoon",
        ( >= 17 and <= 19, >= 19) => "evening",
        (0, <= 5) => "smallHours",
        _ => null,
    };

    private static string Part(string tag, string part, bool plural) =>
        Messages.For(tag, (plural ? "activity.parts." : "activity.part.") + part);

    /// <summary>
    /// A run of days, in the reader's own punctuation.
    /// </summary>
    /// <remarks>
    /// Folded through messages rather than joined on ", " and " and " — the separator and
    /// conjunction belong to a language (Chinese lists on 、 and joins on 和).
    /// </remarks>
    private static string JoinDays(string tag, IReadOnlyList<int> days)
    {
        var names = days.Select(d => DayName(tag, d)).ToList();

        if (names.Count == 1)
        {
            return names[0];
        }

        var head = Fold(tag, "activity.days.list", "list", "next", names.Take(names.Count - 1));

        return Messages.Say(tag, "activity.days.pair", ("first", head), ("second", names[^1]));
    }

    /// <summary>Reduces a list to one string, two at a time, through a two-argument message.</summary>
    private static string Fold(
        string tag, string id, string first, string second, IEnumerable<string> items) =>
        items.Aggregate((a, b) => Messages.Say(tag, id, (first, a), (second, b)));

    /// <summary>
    /// Names the day when a run of odd hours is all in one, so the sentence can point at it.
    /// </summary>
    /// <remarks>
    /// Two whole sentences rather than a fragment substituted into one — "on Monday" and "across the
    /// week" sit in different places across languages.
    /// </remarks>
    private static string WhereFrom(
        string tag, IReadOnlyList<ActivityCell> cells, string onOneDay, string acrossTheWeek)
    {
        var days = cells.Select(c => c.DayOfWeek).Distinct().ToList();

        return days.Count == 1
            ? Messages.Say(tag, onOneDay, ("count", cells.Count), ("day", DayName(tag, days[0])))
            : Messages.Say(tag, acrossTheWeek, ("count", cells.Count));
    }

    /// <summary>An hour of the day, as a clock reading. Machine voice, in Western digits.</summary>
    private static string Time(int hour) => hour.ToString("00", CultureInfo.InvariantCulture) + ":00";

    /// <summary>A run of hours, as <c>03:00–09:59</c>.</summary>
    private static string Window(Run run) =>
        Time(run.From) + "–" + run.To.ToString("00", CultureInfo.InvariantCulture) + ":59";


    private static Run? LongestRun(IEnumerable<ActivityCell> cells, Func<ActivityCell, bool> predicate)
    {
        var matching = cells.Where(predicate).Select(c => c.Hour).ToHashSet();
        return LongestRun(Enumerable.Range(0, 24), matching.Contains);
    }

    /// <summary>The longest contiguous stretch of hours satisfying a predicate, or null if none do.</summary>
    private static Run? LongestRun(IEnumerable<int> hours, Func<int, bool> predicate)
    {
        Run? best = null;
        int? start = null;
        var previous = int.MinValue;

        foreach (var hour in hours)
        {
            if (predicate(hour))
            {
                start ??= hour;
                previous = hour;
                var run = new Run(start.Value, previous);
                if (best is null || run.Length > best.Value.Length)
                {
                    best = run;
                }
            }
            else
            {
                start = null;
            }
        }

        return best;
    }

    private static int Wrap(int dayOfWeek) => ((dayOfWeek % 7) + 7) % 7;

    /// <summary>Monday-is-0, as the store keys a week, in the Sunday-is-0 order .NET's arrays use.</summary>
    private static int FromSunday(int dayOfWeek) => (Wrap(dayOfWeek) + 1) % 7;

    private readonly record struct Run(int From, int To)
    {
        public int Length => To - From + 1;
    }
}
