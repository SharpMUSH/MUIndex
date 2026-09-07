using System.Globalization;

using MUI.Catalog;
using MUI.Web.Localization;

namespace MUI.Web.Components;

public sealed record GameRowPresentation(
    GameSort Sort, DateTimeOffset Now, HttpContext? Http, GameSummary? FirstUnranked)
{
    public static GameRowPresentation For(
        GameListing listing, GameSort sort, DateTimeOffset now, HttpContext? http = null)
        => new(sort, now, http, listing.Games.FirstOrDefault(game => GameSorting.IsUnranked(game, sort)));

    public const int ProtocolsShown = 3;
    public string Tag => Http.LocaleOf().Tag;

    // Missing counts have several causes; do not infer one from a null statistic.
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

    public string Sampled(PresenceWindow window) =>
        $"{window.Window.TotalDays:0}d · {window.Samples} count{(window.Samples == 1 ? string.Empty : "s")}";

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

    // A steady trend can have a nonzero player delta; showing it would contradict the classification.
    public int? TrendFigure(GameSummary g) =>
        g.Growth is GrowthDirection.Up or GrowthDirection.Down ? g.GrowthPlayers : null;

    public string TrendPlayers(int players) => players > 0 ? $"+{players}" : $"{players}";

    public string TrendLabel(GrowthDirection growth, int? players) =>
        players is { } value
            ? $"{FacetWords.TrendingWord(Tag, growth)}, {TrendPlayers(value)}"
            : FacetWords.TrendingWord(Tag, growth);

}
