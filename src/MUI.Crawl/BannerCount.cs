using System.Text.RegularExpressions;

namespace MUI.Crawl;

/// <summary>
/// A player count published in the connect screen itself.
/// </summary>
/// <remarks>
/// <para>
/// The fourth place a count can live, and the one no incumbent crawler appears to read. Aardwolf —
/// among the largest MUDs in the hobby — supports no MSSP and answers no pre-login <c>WHO</c>, so an
/// MSSP-only directory records no count for it at all. Its connect screen says
/// <c>Players Currently Online: 218</c> to every visitor.
/// </para>
/// <para>
/// <b>This is the weakest source and is ranked accordingly.</b> It is pattern-matching a stranger's
/// ASCII art, where a number may be decoration, a high score, an uptime, or last week's figure left
/// in a static file. So it is used only when MSSP and WHO have both failed, it demands an explicit
/// label rather than any bare number, and it refuses anything implausible. When in doubt it returns
/// nothing — an unknown count is honest, and a wrong one is not.
/// </para>
/// </remarks>
public static partial class BannerCount
{
    /// <summary>
    /// A count above this is treated as not-a-player-count. The largest MU* peaks are in the low
    /// thousands, so a five-figure number in a connect screen is a year, a room count, or a record.
    /// </summary>
    public const int Implausible = 10_000;

    /// <summary>
    /// The count a connect screen states about itself, or null when it states none.
    /// </summary>
    public static int? Find(string? banner)
    {
        if (string.IsNullOrWhiteSpace(banner))
        {
            return null;
        }

        var text = AnsiPattern().Replace(banner, string.Empty);
        int? found = null;

        foreach (Match match in LabelledCountPattern().Matches(text))
        {
            if (!int.TryParse(match.Groups["n"].Value, out var value) || value is < 0 or > Implausible)
            {
                continue;
            }

            // Two different labelled counts in one screen means we cannot tell which is the players
            // online — a screen advertising both "online" and a record high, say. Refuse rather than
            // pick, because picking is guessing and this source is weak enough already.
            if (found is not null && found != value)
            {
                return null;
            }

            found = value;
        }

        return found;
    }

    // "Players Currently Online: 218" / "Players online: 42" / "Currently connected: 7"
    // The label is mandatory. A bare number in ASCII art is never a count.
    [GeneratedRegex(
        @"\b(?:players?|users?|characters?|adventurers?)?\s*(?:currently\s+)?(?:online|connected|playing|logged\s*(?:in|on))\s*[:\-]?\s*(?<n>\d{1,5})\b"
        + @"|\b(?:players?|users?)\s*(?:currently\s+)?[:\-]\s*(?<n>\d{1,5})\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex LabelledCountPattern();

    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex AnsiPattern();
}
