using System.Globalization;

using MUI.Catalog;
using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>The shared sort, timestamp and locale used to present a set of listing rows.</summary>
public sealed record GameRowPresentation(
    GameSort Sort, DateTimeOffset Now, HttpContext? Http, GameSummary? FirstUnranked)
{
    public const int ProtocolsShown = 3;
    public string Tag => Http.LocaleOf().Tag;

    /// <summary>What goes in the count column, whichever measurement the switch is showing.</summary>
    /// <remarks>
    /// Never a zero standing in for an unknown: an unreadable count says so in words. That word is
    /// deliberately generic — a null here can mean an unreadable count, an unmeasured window, or no
    /// rows at all, and naming a specific cause would record our crawl schedule as a fact about the
    /// game (rule 5).
    /// </remarks>
    public string CountText(GameSummary game)
    {
        if (SortWindows.Of(Sort) is not null)
        {
            return game.PlayersOverWindow is { } window
                ? (SortWindows.IsMedian(Sort) ? window.Median : window.Peak)
                    .ToString(CultureInfo.InvariantCulture)
                : Messages.For(Tag, "listing.count.none");
        }

        return game.PlayersNow is { } now
            ? now.ToString(CultureInfo.InvariantCulture)
            : Messages.For(Tag, "listing.count.none");
    }

    /// <summary>
    /// The count's register: accent where somebody is on, plain for a measured zero, faint for a
    /// number we do not have.
    /// </summary>
    /// <remarks>
    /// A measured zero is dimmed rather than accented and is never faint: we got in and nobody was
    /// there is a measurement, and it is not the same fact as a count we could not read.
    /// </remarks>
    public string CountState(GameSummary game)
    {
        var value = SortWindows.Of(Sort) is not null
            ? game.PlayersOverWindow is { } w ? (SortWindows.IsMedian(Sort) ? w.Median : w.Peak) : (int?)null
            : game.PlayersNow;

        return value switch
        {
            null => "unknown",
            0 => "empty",
            _ => "live",
        };
    }

    /// <summary>The span and the sample tally behind a statistic, in the row's own machine voice.</summary>
    public string Sampled(PresenceWindow window) =>
        $"{window.Window.TotalDays:0}d · {window.Samples} count{(window.Samples == 1 ? string.Empty : "s")}";

    /// <summary>
    /// The growth arrow's shape. A triangle rather than an ASCII arrow, so it reads at the row's own
    /// small size without anti-aliasing into a smudge the way ↑/↓ can at 13px.
    /// </summary>
    public string TrendGlyph(GrowthDirection direction) => direction switch
    {
        GrowthDirection.Up => "▲",
        GrowthDirection.Down => "▼",
        _ => "●",
    };

    public string TrendState(GrowthDirection direction) => direction switch
    {
        GrowthDirection.Up => "up",
        GrowthDirection.Down => "down",
        _ => "steady",
    };

    /// <summary>
    /// The figure the trend cell prints, or null where it prints the glyph alone.
    /// </summary>
    /// <remarks>
    /// Withheld on a steady row on purpose. The direction is decided on the fitted line's percentage
    /// and the figure is in players, so the two can part company on a large game — fifty-odd players
    /// moving five is inside <see cref="GrowthTrend.SteadyBand"/> and steady, and "steady, +5" would
    /// have the row assert a change in the same breath it declined to call one.
    /// </remarks>
    public int? TrendFigure(GameSummary g) =>
        g.Growth is GrowthDirection.Up or GrowthDirection.Down ? g.GrowthPlayers : null;

    /// <summary>
    /// The fitted line's own reading in players, signed rather than only coloured — a screen reader
    /// has no colour, and the sign is the whole of what the glyph says in text.
    /// </summary>
    public string TrendPlayers(int players) => players > 0 ? $"+{players}" : $"{players}";

    /// <summary>
    /// The accessible name for the trend cell — the direction word alone said "trending up" and left
    /// the number a sighted reader sees right beside it unannounced.
    /// </summary>
    public string TrendLabel(GrowthDirection growth, int? players) =>
        players is { } value
            ? $"{FacetWords.TrendingWord(Tag, growth)}, {TrendPlayers(value)}"
            : FacetWords.TrendingWord(Tag, growth);

}
