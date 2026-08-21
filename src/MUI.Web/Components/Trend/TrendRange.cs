using System.Globalization;

namespace MUI.Web.Components;

/// <summary>
/// The calendar range a trend chart is drawn over, and the addresses of the ranges beside it.
/// </summary>
/// <remarks>
/// The range lives in the URL rather than in control state, so seeking needs no script and is
/// shareable. Everything is clamped rather than refused — a bad or back-to-front range resolves to
/// the nearest sensible window instead of erroring — but a clamped range must never be drawn silently:
/// the resolved range is always what the caption prints.
/// </remarks>
public sealed record TrendRange(DateOnly From, DateOnly To)
{
    /// <summary>How many days the range covers, both ends included.</summary>
    public int Days => To.DayNumber - From.DayNumber + 1;

    /// <summary>The default: the last <see cref="TrendSeries.DefaultDays"/> days ending today.</summary>
    public static TrendRange Default(DateOnly today) =>
        new(today.AddDays(-(TrendSeries.DefaultDays - 1)), today);

    /// <summary>
    /// The range a pair of query values names, clamped to something drawable.
    /// </summary>
    /// <remarks>
    /// One end given and not the other is a real request — "everything since the first of March" —
    /// so the missing end is filled from the default width rather than the whole thing being
    /// discarded.
    /// </remarks>
    public static TrendRange Parse(string? from, string? to, DateOnly today)
    {
        var start = Day(from);
        var end = Day(to);

        if (start is null && end is null)
        {
            return Default(today);
        }

        // A range may end in the future — a reader who asks for "this month" on the 3rd wants the
        // month — but there is nothing beyond today to draw, so it is trimmed to today.
        var resolved = (start, end) switch
        {
            ({ } s, { } e) => (From: s, To: e),
            ({ } s, null) => (From: s, To: Min(s.AddDays(TrendSeries.DefaultDays - 1), today)),
            (null, { } e) => (From: e.AddDays(-(TrendSeries.DefaultDays - 1)), To: e),
            _ => (Default(today).From, Default(today).To),
        };

        if (resolved.To < resolved.From)
        {
            (resolved.From, resolved.To) = (resolved.To, resolved.From);
        }

        if (resolved.To > today)
        {
            resolved.To = today;
        }

        if (resolved.To < resolved.From)
        {
            resolved.From = resolved.To;
        }

        // The ceiling is the same one the API answers at this grain, so a range the page draws is
        // always one a reader could ask the route for.
        if (resolved.To.DayNumber - resolved.From.DayNumber + 1 > TrendSeries.MaximumDays)
        {
            resolved.From = resolved.To.AddDays(-(TrendSeries.MaximumDays - 1));
        }

        return new TrendRange(resolved.From, resolved.To);
    }

    /// <summary>The same width, one width earlier. Never past the first day there could be data.</summary>
    public TrendRange Previous() => new(From.AddDays(-Days), To.AddDays(-Days));

    /// <summary>The same width, one width later, trimmed at today. Null when already current.</summary>
    public TrendRange? Next(DateOnly today) =>
        To >= today ? null : new TrendRange(From.AddDays(Days), Min(To.AddDays(Days), today));

    /// <summary>A preset width ending today.</summary>
    public static TrendRange Preset(int days, DateOnly today) =>
        new(today.AddDays(-(days - 1)), today);

    /// <summary>This range as query values.</summary>
    public string Query => $"from={Text(From)}&to={Text(To)}";

    public bool Covers(int days, DateOnly today) => To == today && Days == days;

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    private static string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly? Day(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var day)
            ? day
            : null;
}
