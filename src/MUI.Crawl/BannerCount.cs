using System.Text.RegularExpressions;

namespace MUI.Crawl;

/// <summary>
/// A player count published in the connect screen itself.
/// </summary>
/// <remarks>
/// The weakest of the count sources — pattern-matching a stranger's ASCII art, where a number may be
/// decoration, a high score, or a stale figure. Used only when MSSP and WHO have both failed; demands
/// an explicit label rather than any bare number, and refuses anything implausible. When in doubt it
/// returns nothing — an unknown count is honest, a wrong one is not.
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

        foreach (var value in Candidates(text))
        {
            if (value is < 0 or > Implausible)
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

    /// <summary>
    /// Every count this screen states about itself, in either of the two ways a screen states one.
    /// </summary>
    /// <remarks>
    /// A connect screen states a count either as a label (<c>Players Currently Online: 218</c>) or as
    /// a sentence (<c>There are 41 players and 3 immortals online.</c>). The sentence form reuses
    /// <see cref="WhoParser.TryStatedCount"/> rather than reimplementing it, since a server writes the
    /// same sentence wherever it prints it — including its ceiling rule that keeps <c>11 out of 200</c>
    /// from reading as 200.
    /// </remarks>
    private static IEnumerable<int> Candidates(string text)
    {
        foreach (Match match in LabelledCountPattern().Matches(text))
        {
            if (int.TryParse(match.Groups["n"].Value, out var labelled))
            {
                yield return labelled;
            }
        }

        foreach (var line in text.Split('\n'))
        {
            if (WhoParser.TryStatedCount(line, out var stated))
            {
                yield return stated;
            }
        }
    }

    // "Players Currently Online: 218" / "Players online: 42" / "Currently connected: 7" /
    // "Currently On-Line: 12" / "Number of players on: 8"
    // The label is mandatory. A bare number in ASCII art is never a count.
    //
    // The third alternative is the one shape where a bare "on" is admitted, and it is narrow on
    // purpose: the people-noun must be present, immediately followed by "on", immediately followed by
    // a colon or dash and the number (nannymud's "Number of players on:   8"). Without all three
    // anchors "on" is a preposition and would read "3 messages on the board" as a population.
    [GeneratedRegex(
        @"\b(?:players?|users?|characters?|adventurers?)?\s*(?:currently\s+)?(?:on-?line|connected|playing|logged\s*(?:in|on))\s*[:\-]?\s*(?<n>\d{1,5})\b"
        + @"|\b(?:players?|users?)\s*(?:currently\s+)?[:\-]\s*(?<n>\d{1,5})\b"
        + @"|\b(?:players?|users?|characters?|adventurers?)\s+on\s*[:\-]\s*(?<n>\d{1,5})\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex LabelledCountPattern();

    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex AnsiPattern();
}
