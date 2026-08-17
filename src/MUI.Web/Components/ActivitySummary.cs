using MUI.Catalog;

namespace MUI.Web.Components;

/// <summary>
/// The heatmap said in words, before the graphic and instead of it.
/// </summary>
/// <remarks>
/// <para>
/// The sentence is the accessible summary and it arrives first, because the answer a reader wants —
/// <em>when is anyone actually on?</em> — is a sentence and not a grid. The per-day lines are the
/// "read as text" disclosure, which exists so a screen reader is not made to walk 168 cells to learn
/// something one paragraph could have told it.
/// </para>
/// <para>
/// It keeps the three states of spec §5.4 apart in words, which the design's own specimen sentence
/// does not: <em>could not be measured</em> covers both an hour we never reached and an hour we
/// reached and could not count, and those are different facts about a game. Conflating them is the
/// worst bug this codebase can ship, so the sentence names each separately.
/// </para>
/// <para>
/// And an hour with no presence row is said as <em>not measured</em>, never as <em>not reachable</em>.
/// A failed probe writes no presence row at all, so silence in this grid cannot tell an outage of
/// theirs from a gap of ours — and the difference is not cosmetic: this sentence described a game we
/// had measured once, and found perfectly reachable, as unreachable for 167 hours of the week.
/// Reachability is the strip's job and it is derived from intervals, which can tell the two apart.
/// </para>
/// </remarks>
public static class ActivitySummary
{
    private static readonly string[] DayNames =
        ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    private static readonly string[] ShortDayNames = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    private static readonly string[] Numbers =
    [
        "no", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten",
        "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen",
        "Nineteen", "Twenty",
    ];

    /// <summary>
    /// How many days of the week must carry a measurement before the grid is worth drawing.
    /// </summary>
    /// <remarks>
    /// A 7×24 grid with one probe in it is 167 empty cells and one number: to a screen reader that
    /// is the word "not measured" announced 167 times before the fact arrives, and to everybody else
    /// it is a page that looks broken. Below the threshold the sentence is the whole panel — which
    /// is what the grid is for anyway — and the grid appears the week the data does.
    /// </remarks>
    public const int MeasuredDaysForGrid = 7;

    public static string DayName(int dayOfWeek) => DayNames[Wrap(dayOfWeek)];

    public static string ShortDayName(int dayOfWeek) => ShortDayNames[Wrap(dayOfWeek)];

    /// <summary>
    /// What one cell is, as a sentence. The <c>title</c> a mouse reader gets and the text a screen
    /// reader gets are the same string, because they are the same fact.
    /// </summary>
    public static string CellLabel(ActivityCell cell) => cell switch
    {
        { IsGap: true } => $"{ShortDayName(cell.DayOfWeek)} {cell.Hour:00}:00 — no measurement in this hour",
        { IsUnmeasurable: true } => $"{ShortDayName(cell.DayOfWeek)} {cell.Hour:00}:00 — probed, no count could be read",
        { Count: 0 } => $"{ShortDayName(cell.DayOfWeek)} {cell.Hour:00}:00 — 0 players, measured",
        { Count: 1 } => $"{ShortDayName(cell.DayOfWeek)} {cell.Hour:00}:00 — 1 player on average",
        _ => $"{ShortDayName(cell.DayOfWeek)} {cell.Hour:00}:00 — {cell.Count} players on average",
    };

    /// <summary>
    /// What one cell announces when a reader arrows onto it: a value, not a sentence.
    /// </summary>
    /// <remarks>
    /// The row and column headers already say which day and hour it is, so repeating them in the
    /// cell turns 168 cells into 168 paragraphs — which is exactly what the summary sentence and
    /// the per-day disclosure exist to avoid. Still words, though: "not counted" and "not reached"
    /// are different facts and neither is a zero.
    /// </remarks>
    public static string CellValue(ActivityCell cell) => cell switch
    {
        { IsGap: true } => "not measured",
        { IsUnmeasurable: true } => "not counted",
        _ => cell.Count!.Value.ToString(),
    };

    /// <summary>The sentence that sits above the grid.</summary>
    public static string Sentence(IReadOnlyList<ActivityCell> cells)
    {
        if (cells.Count == 0)
        {
            return "We have not measured this game's activity yet.";
        }

        var parts = new List<string>(4);
        var counted = cells.Where(c => c.IsCounted).ToList();

        if (counted.Count == 0)
        {
            parts.Add("No hour of the week has produced a player count.");
        }
        else if (counted.All(c => c.Count == 0))
        {
            // A measured zero everywhere is a measurement, and a strong one. It must not read as
            // absence of data.
            parts.Add("Measured every hour and nobody has been on in any of them.");
        }
        else
        {
            parts.Add(Busiest(counted));
            var quiet = Quietest(counted);
            if (quiet is not null)
            {
                parts.Add(quiet);
            }
        }

        var gaps = cells.Count(c => c.IsGap);
        if (gaps > 0)
        {
            // "Not measured", never "not reachable" — see ActivityCell.IsGap. The strip beside this
            // grid is where reachability is stated, from intervals that can tell an outage of theirs
            // from a gap of ours.
            parts.Add($"{Spell(gaps)} {Hours(gaps)} {WhereFrom(cells.Where(c => c.IsGap))} "
                + $"{(gaps == 1 ? "has" : "have")} no measurement yet.");
        }

        var unmeasurable = cells.Count(c => c.IsUnmeasurable);
        if (unmeasurable > 0)
        {
            parts.Add($"{Spell(unmeasurable)} {Hours(unmeasurable)} {WhereFrom(cells.Where(c => c.IsUnmeasurable))} "
                + $"answered but produced no count.");
        }

        return string.Join(' ', parts);
    }

    /// <summary>
    /// How many days of the week carry a measurement of any kind — counted or merely probed.
    /// </summary>
    public static int MeasuredDays(IReadOnlyList<ActivityCell> cells) =>
        cells.Where(c => !c.IsGap).Select(c => c.DayOfWeek).Distinct().Count();

    /// <summary>
    /// What there is, when there is not yet enough of it to draw. Two sentences and no grid.
    /// </summary>
    /// <remarks>
    /// It states what exists rather than what is missing, and it never says the game was unreachable:
    /// an hour with no presence row covers an hour we could not reach and an hour we never dialled
    /// alike, and naming one of them would file our crawl schedule as a fact about somebody's game.
    /// </remarks>
    public static string Sparse(IReadOnlyList<ActivityCell> cells)
    {
        var measured = cells.Where(c => !c.IsGap).ToList();
        var counted = measured.Where(c => c.IsCounted).ToList();
        var days = MeasuredDays(cells);

        var what = counted.Count switch
        {
            0 when measured.Count == 0 => "No hour of the week has a measurement yet.",
            0 => $"{Spell(measured.Count)} {Hours(measured.Count)} answered and produced no count.",
            _ => Sample(counted, measured.Count - counted.Count),
        };

        var still = days == 0
            ? "The grid appears once every day of the week has one."
            : $"Measured on {Spell(days).ToLowerInvariant()} of the seven days so far; "
                + "the grid appears once every day has an hour in it.";

        return $"{what} {still}";
    }

    /// <summary>The busiest hour we have, said as a fact about that one hour rather than a pattern.</summary>
    private static string Sample(IReadOnlyList<ActivityCell> counted, int uncountable)
    {
        var peak = counted.MaxBy(c => c.Count)!;
        var hours = $"{Spell(counted.Count)} {Hours(counted.Count)}";

        var sentence = peak.Count == 0
            ? $"{hours} measured, all of them at nobody on."
            : $"{hours} measured, the busiest {peak.Count} on {DayName(peak.DayOfWeek)} "
                + $"at {peak.Hour:00}:00 UTC.";

        return uncountable == 0
            ? sentence
            : $"{sentence} {Spell(uncountable)} more {Hours(uncountable)} answered and produced no count.";
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
        public string Day => ShortDayName(DayOfWeek);
    }

    /// <summary>
    /// One row per day — seven rows, not a hundred and sixty-eight cells.
    /// </summary>
    /// <remarks>
    /// This is the grid's text alternative and the source of the plain-text lines both, so the table
    /// under the drawing and the <c>?plain=1</c> mirror cannot come to say different things about one
    /// week. The three states of spec §5.4 each get a column of their own: a count, an hour that
    /// answered without one, and an hour nobody has measured are three facts and never one.
    /// </remarks>
    public static IReadOnlyList<DayLine> Days(IReadOnlyList<ActivityCell> cells)
    {
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
                quiet is { } q ? $"{q.From:00}:00–{q.To:00}:59" : null,
                forDay.Count(c => c.IsGap),
                forDay.Count(c => c.IsUnmeasurable)));
        }

        return lines;
    }

    /// <summary>One line per day, for plain mode — the same rows as <see cref="Days"/>, in prose.</summary>
    public static IReadOnlyList<string> PerDay(IReadOnlyList<ActivityCell> cells)
    {
        var lines = new List<string>(7);

        foreach (var day in Days(cells))
        {
            var clauses = new List<string>(4);

            if (day.Busiest is { } busiest)
            {
                clauses.Add(busiest == 0
                    ? "measured at zero all day"
                    : $"peak {busiest} at {day.PeakHour:00}:00");

                if (day.NobodyOn is { } quiet)
                {
                    clauses.Add($"nobody on {quiet}");
                }
            }
            else
            {
                clauses.Add("no count in any hour");
            }

            if (day.NotMeasured > 0)
            {
                clauses.Add($"{day.NotMeasured} {Hours(day.NotMeasured)} not measured");
            }

            if (day.NotCounted > 0)
            {
                clauses.Add($"{day.NotCounted} {Hours(day.NotCounted)} probed but uncountable");
            }

            lines.Add($"{day.Day} — {string.Join(", ", clauses)}");
        }

        return lines;
    }

    private static string Busiest(IReadOnlyList<ActivityCell> counted)
    {
        var byHour = counted
            .GroupBy(c => c.Hour)
            .ToDictionary(g => g.Key, g => g.Average(c => c.Count!.Value));
        var top = byHour.Values.Max();

        // The busy band is every hour within a quarter of the best hour, taken as the longest
        // contiguous run so the sentence names a window rather than a scatter of hours.
        var busyHours = byHour.Where(kv => kv.Value >= top * 0.75).Select(kv => kv.Key).ToHashSet();
        var band = LongestRun(Enumerable.Range(0, 24), busyHours.Contains) ?? new Run(0, 23);

        var inBand = counted.Where(c => c.Hour >= band.From && c.Hour <= band.To).ToList();
        var byDay = inBand
            .GroupBy(c => c.DayOfWeek)
            .ToDictionary(g => g.Key, g => g.Average(c => c.Count!.Value));
        var bestDay = byDay.Values.Max();
        var busyDays = byDay.Where(kv => kv.Value >= bestDay * 0.9).Select(kv => kv.Key).Order().ToList();

        var part = PartOfDay(band);
        var when = part is null
            ? $"{band.From:00}:00–{band.To:00}:59"
            : $"{Plural(part)}, {band.From:00}:00–{band.To:00}:59";

        return busyDays.Count == 7
            ? $"Busiest every day, {when}."
            : $"Busiest {JoinDays(busyDays)} {when}.";
    }

    private static string? Quietest(IReadOnlyList<ActivityCell> counted)
    {
        var byHour = counted
            .GroupBy(c => c.Hour)
            .ToDictionary(g => g.Key, g => g.Average(c => c.Count!.Value));
        var top = byHour.Values.Max();
        if (top <= 0)
        {
            return null;
        }

        // "Quiet" has to mean *nobody, or all but nobody* rather than "a lot less than the peak".
        // On a game whose evenings run to fifteen, a fifth of that is three people in the room, and
        // calling three people quiet is a claim the measurement does not support. So the strongest
        // available reading is tried first — a run of hours measured at exactly zero — and only
        // then a near-zero floor.
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

        var window = $"{quiet.From:00}:00–{quiet.To:00}:59";
        var part = PartOfDay(quiet);
        var weekdays = quietDays.Count == 5 && quietDays.All(d => Wrap(d) < 5);

        // "Every day" would be a claim about a day whose hours in this band were never measured.
        var who = quietDays.Count == daysWithData
            ? daysWithData < totalDays ? "every day we could measure" : "every day"
            : weekdays
                ? "on weekdays"
                : $"on {JoinDays(quietDays)}";

        return part is null
            ? $"Reliably quiet {who}, {window}."
            : $"Reliably quiet {who} in the {part}, {window}.";
    }

    /// <summary>Whether a band of hours has a name a person would use for it.</summary>
    private static string? PartOfDay(Run band) => (band.From, band.To) switch
    {
        ( >= 5 and <= 8, <= 11) => "morning",
        ( >= 11 and <= 13, >= 12 and <= 17) => "afternoon",
        ( >= 17 and <= 19, >= 19) => "evening",
        (0, <= 5) => "small hours",
        _ => null,
    };

    private static string JoinDays(IReadOnlyList<int> days)
    {
        var names = days.Select(DayName).ToList();
        return names.Count switch
        {
            1 => names[0],
            2 => $"{names[0]} and {names[1]}",
            _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}",
        };
    }

    /// <summary>Names the day when a run of odd hours is all in one, so the sentence can point at it.</summary>
    private static string WhereFrom(IEnumerable<ActivityCell> cells)
    {
        var days = cells.Select(c => c.DayOfWeek).Distinct().Order().ToList();
        return days.Count == 1 ? $"on {DayName(days[0])}" : "across the week";
    }

    /// <summary>"evening" becomes "evenings"; "small hours" is already plural and stays.</summary>
    private static string Plural(string part) =>
        part.EndsWith('s') ? part : part + "s";

    private static string Spell(int n) => n < Numbers.Length ? Numbers[n] : n.ToString();

    private static string Hours(int n) => n == 1 ? "hour" : "hours";

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

    private readonly record struct Run(int From, int To)
    {
        public int Length => To - From + 1;
    }
}
