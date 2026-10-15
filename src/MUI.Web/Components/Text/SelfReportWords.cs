using MUI.Catalog;
using MUI.Catalog.Persistence;

using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>
/// How the game page says what a game reported about itself beyond the generic MSSP set (see
/// <see cref="SelfReport"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The game's count, never ours.</b> The sentence opens with whose count it is, because it sits
/// beside a figure we measured and the two answer different questions: how many connections a probe
/// found at a moment, and how many different people the game says it saw over a day or a week.
/// Neither feeds the other, and the sentence must not read as if it did.
/// </para>
/// <para>
/// Shared by the rendered page and the plain surface so the two cannot word it differently.
/// </para>
/// </remarks>
public static class SelfReportWords
{
    /// <summary>The sentence for a game's own trailing counts.</summary>
    public static string Recent(string tag, RecentPlayers recent)
    {
        ArgumentNullException.ThrowIfNull(recent);

        var args = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["day"] = recent.Day,
            ["week"] = recent.Week,
        };

        return Messages.For(
            tag,
            (recent.Day, recent.Week) switch
            {
                (not null, not null) => "game.recentPlayers.both",
                (not null, null) => "game.recentPlayers.day",
                _ => "game.recentPlayers.week",
            },
            args);
    }

    /// <summary>
    /// A declared value as a reader should see it: a flag's <c>1</c> or <c>0</c> as yes or no, and
    /// everything else exactly as the game sent it.
    /// </summary>
    /// <remarks>
    /// Only a value <see cref="SelfReport.Flag"/> reads changes. A <c>LOGINS</c> of
    /// <c>wizards only</c> stays those words — calling it yes would be our paraphrase of their
    /// answer.
    /// </remarks>
    public static string Value(string tag, string field, string value) =>
        FieldRegistry.Instance.Find(field.ToUpperInvariant()) is { Shape: FieldShape.Flag }
        && SelfReport.Flag(value) is { } flag
            ? Messages.For(tag, flag ? "game.flag.yes" : "game.flag.no")
            : value;

    /// <summary>Whether a declared field is the portal's list of pages.</summary>
    public static bool IsPortal(string field) =>
        string.Equals(field, SelfReport.PortalApplication, StringComparison.OrdinalIgnoreCase);
}
