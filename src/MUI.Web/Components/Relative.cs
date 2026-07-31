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
