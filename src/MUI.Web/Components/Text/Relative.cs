using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>
/// Which question an age is answering, which is not always the same question.
/// </summary>
/// <remarks>
/// English says "2w ago" for both and several languages will not. Splitting the id is the only way a
/// translator can tell them apart at all, because the source text cannot.
/// </remarks>
public enum AgeSense
{
    /// <summary>How long ago we last confirmed a value — how fresh this measurement is.</summary>
    Confirmed,

    /// <summary>
    /// How long since the game was last reached — how long it has been dark.
    /// </summary>
    /// <remarks>
    /// Never <em>offline</em>, <em>down</em> or <em>up</em>, in any language. We measured a socket
    /// from one vantage point at intervals; a game with a routing problem to our host is unreachable
    /// and perfectly alive, and saying otherwise files our vantage point as a fact about their game.
    /// </remarks>
    Reached,
}

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
    /// One ladder, everywhere: minutes to ninety, hours to forty-eight, then days, then weeks, then
    /// months and years.
    /// </summary>
    /// <remarks>
    /// A bare duration carries no register — <see cref="Ago"/> is where the verb, and the claim,
    /// live.
    /// </remarks>
    public static string Format(string tag, TimeSpan age) => Say(tag, "age.short", age);

    /// <summary>
    /// The same age, as something that already happened.
    /// </summary>
    /// <remarks>
    /// The freshest rung is the whole phrase "just now" rather than a duration with " ago" appended —
    /// appending it to <see cref="Format"/>'s "now" used to produce "last reached now ago" for the
    /// ninety seconds after every probe.
    /// </remarks>
    public static string Ago(string tag, TimeSpan age, AgeSense sense = AgeSense.Confirmed) =>
        Say(tag, sense is AgeSense.Reached ? "age.dark" : "age.ago", age);

    private static string Say(string tag, string family, TimeSpan age)
    {
        ArgumentNullException.ThrowIfNull(tag);

        var (rung, count) = Rung(age);

        return Messages.For(
            tag,
            family + "." + rung,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["count"] = count });
    }

    /// <summary>Which rung of the ladder an age lands on, and the number that goes on it.</summary>
    /// <remarks>
    /// The ladder is chosen once and the three families all read from it, so a rung cannot mean one
    /// span in a tooltip and another in a column.
    /// </remarks>
    internal static (string Rung, int Count) Rung(TimeSpan age) => age switch
    {
        { TotalSeconds: < 90 } => ("now", 0),
        { TotalMinutes: < 90 } => ("minutes", (int)age.TotalMinutes),
        { TotalHours: < 48 } => ("hours", (int)age.TotalHours),
        { TotalDays: < 14 } => ("days", (int)age.TotalDays),
        { TotalDays: < 70 } => ("weeks", (int)(age.TotalDays / 7)),
        { TotalDays: < 730 } => ("months", (int)(age.TotalDays / 30)),
        _ => ("years", (int)(age.TotalDays / 365)),
    };
}
