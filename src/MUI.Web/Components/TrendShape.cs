using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>How the trend is drawn.</summary>
/// <remarks>
/// Two answers to the same numbers, because the two failure modes are opposite. A line reads the
/// direction of a game probed most days and says nothing at all about a game counted three times in
/// a quarter — two of those three days have no neighbour, and a run of one is a dot. Columns say
/// every measured day plainly and turn a densely-probed year into a picket fence where the shape of
/// the year used to be. Neither is the right default for every game on the site, so it is a link.
/// </remarks>
public enum TrendShape
{
    /// <summary>A broken line and a min–max band. The default.</summary>
    Line,

    /// <summary>A column per counted day, from zero, capped at the day's busiest probe.</summary>
    Bar,
}

/// <summary>Reading and writing <see cref="TrendShape"/> as the address carries it.</summary>
public static class TrendShapes
{
    /// <summary>The query key, spelled once.</summary>
    public const string Query = "chart";

    /// <summary>
    /// The shape a query asked for, defaulting to the line.
    /// </summary>
    /// <remarks>
    /// Anything unrecognised falls back rather than erroring, which is the same choice the ranking
    /// window makes: a mistyped shape on a browsable URL is a reader's typo and not a fault, and the
    /// selector under the graphic then shows which shape they actually got.
    /// </remarks>
    public static TrendShape Parse(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "bar" or "bars" => TrendShape.Bar,
            _ => TrendShape.Line,
        };

    /// <summary>What the address calls it.</summary>
    public static string Slug(this TrendShape shape) => shape is TrendShape.Bar ? "bar" : "line";

    /// <summary>
    /// What the selector calls it, in the reader's language.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Slug"/>, which is what the <em>address</em> calls it and stays
    /// English for ever: a URL a reader copies out of a German page has to keep working when it is
    /// pasted into an English one, so the query value is machine voice and only the label is not.
    /// </remarks>
    public static string Label(this TrendShape shape, string tag) =>
        Messages.For(tag, shape is TrendShape.Bar ? "trend.shape.bar" : "trend.shape.line");
}
