using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>How the trend is drawn.</summary>
/// <remarks>
/// A line reads direction well for densely-probed games but is meaningless for sparse data (a run
/// of one point is a dot); bars show every measured day but turn a dense year into a picket fence.
/// Neither suits every game, so it's user-selectable rather than fixed.
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

    /// <summary>The shape a query asked for, defaulting to the line. Unrecognised values fall back rather than erroring.</summary>
    public static TrendShape Parse(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "bar" or "bars" => TrendShape.Bar,
            _ => TrendShape.Line,
        };

    /// <summary>What the address calls it.</summary>
    public static string Slug(this TrendShape shape) => shape is TrendShape.Bar ? "bar" : "line";

    /// <summary>
    /// What the selector calls it, in the reader's language. Kept separate from <see cref="Slug"/>,
    /// which stays English so a URL copied out of one locale's page keeps working in another.
    /// </summary>
    public static string Label(this TrendShape shape, string tag) =>
        Messages.For(tag, shape is TrendShape.Bar ? "trend.shape.bar" : "trend.shape.line");
}
