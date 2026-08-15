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

    public static string Format(TimeSpan age) => age switch
    {
        { TotalSeconds: < 90 } => "now",
        { TotalMinutes: < 90 } => $"{(int)age.TotalMinutes}m",
        { TotalHours: < 36 } => $"{(int)age.TotalHours}h",
        { TotalDays: < 14 } => $"{(int)age.TotalDays}d",
        { TotalDays: < 70 } => $"{(int)(age.TotalDays / 7)}w",
        { TotalDays: < 730 } => $"{(int)(age.TotalDays / 30)}mo",
        _ => $"{(int)(age.TotalDays / 365)}y",
    };
}
