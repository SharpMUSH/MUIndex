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
    /// <para>
    /// The rungs matter because a column of these is read by eye and <c>84m</c> sorts below
    /// <c>1h</c> to anybody scanning it. Two days of hours rather than a day and a half is the
    /// widest a rung can be before the number in it stops meaning anything — and it is where the
    /// site's own probe cadence puts most ages that are not minutes.
    /// </para>
    /// <para>
    /// A bare duration takes no register: it is a length of time and not a claim about anything.
    /// <see cref="Ago"/> is where the register lives, because that is where a verb appears.
    /// </para>
    /// </remarks>
    public static string Format(string tag, TimeSpan age) => Say(tag, "age.short", age);

    /// <summary>
    /// The same age, as something that already happened.
    /// </summary>
    /// <remarks>
    /// The freshest rung is the word "just now" rather than a duration with a suffix glued on. It
    /// used to be exactly that, and a caller appending " ago" to <see cref="Format"/>'s "now" wrote
    /// "last reached now ago" for the ninety seconds after every probe — which, on a listing
    /// rendered while a crawl is running, was most of the rows on the page. Each rung now carries
    /// its own whole phrase, so there is nowhere left to append one.
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
