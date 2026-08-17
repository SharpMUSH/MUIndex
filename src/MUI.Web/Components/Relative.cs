namespace MUI.Web.Components;

/// <summary>
/// Ages render as relative time, never as a decay bar.
/// </summary>
/// <remarks>
/// A bar invents precision the reader cannot use — it implies we know how far through a lifetime a
/// value is, when all we know is when we last confirmed it. Decay is expressed by the glyph and the
/// age turning amber once a field passes its own expected-refresh window.
/// </remarks>
public static class Relative
{
    /// <summary>
    /// The same age, as something that already happened.
    /// </summary>
    /// <remarks>
    /// <see cref="Format"/>'s freshest bucket is the word "now", so a caller appending " ago" to it
    /// wrote "last reached now ago" for the ninety seconds after every probe — which, on a listing
    /// rendered while a crawl is running, was most of the rows on the page. The suffix belongs to
    /// whoever knows whether the bucket came back a duration or a word, and that is here.
    /// </remarks>
    public static string Ago(TimeSpan age) => Format(age) is "now" ? "just now" : Format(age) + " ago";

    /// <summary>
    /// One ladder, everywhere: minutes to ninety, hours to forty-eight, then days, then weeks, then
    /// months and years.
    /// </summary>
    /// <remarks>
    /// The rungs matter because a column of these is read by eye and <c>84m</c> sorts below
    /// <c>1h</c> to anybody scanning it. Two days of hours rather than a day and a half is the
    /// widest a rung can be before the number in it stops meaning anything — and it is where the
    /// site's own probe cadence puts most ages that are not minutes.
    /// </remarks>
    public static string Format(TimeSpan age) => age switch
    {
        { TotalSeconds: < 90 } => "now",
        { TotalMinutes: < 90 } => $"{(int)age.TotalMinutes}m",
        { TotalHours: < 48 } => $"{(int)age.TotalHours}h",
        { TotalDays: < 14 } => $"{(int)age.TotalDays}d",
        { TotalDays: < 70 } => $"{(int)(age.TotalDays / 7)}w",
        { TotalDays: < 730 } => $"{(int)(age.TotalDays / 30)}mo",
        _ => $"{(int)(age.TotalDays / 365)}y",
    };
}
