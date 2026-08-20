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
        foreach (var line in text.Split('\n'))
        {
            // A line that names a staff role and no player noun is counting somebody this project
            // does not count, so it offers no candidate at all. Judged on the whole line rather than
            // as a guard in front of the connectivity word: "Wizards online: 4" and "Wizards
            // currently online: 4" are the same claim, and any fixed-width lookbehind walks off the
            // end of the second — as one did, reporting four players for a line about four wizards.
            if (!CountsOnlyStaff(line))
            {
                foreach (Match match in LabelledCountPattern().Matches(line))
                {
                    if (int.TryParse(match.Groups["n"].Value, out var labelled))
                    {
                        yield return labelled;
                    }
                }
            }

            if (WhoParser.TryStatedCount(line, out var stated))
            {
                yield return stated;
            }
        }
    }

    /// <summary>
    /// Whether a line is about staff and nobody this project counts as a player.
    /// </summary>
    /// <remarks>
    /// The people-noun in <see cref="LabelledCountPattern"/> is optional, because
    /// <c>lusternia.com:5000</c> writes a bare "Currently On-Line: 12" with no noun at all — so the
    /// pattern must be able to start at the connectivity word, and that same licence is what lets
    /// "Wizards online: 4" read as four players. The fix is to disqualify the line, not to guard the
    /// word: distance between the role and the connectivity word is unbounded in practice
    /// ("Wizards currently online", "Wizards&#160;&#160;&#160;&#160;&#160;&#160;online"), and a
    /// lookbehind that has to name a width is a hole with a number in it.
    /// <para>
    /// A line naming both — "33 mortals and 4 wizards online" — is not staff-only and is left to the
    /// sentence reader, which counts the mortals and does not know the word "wizards". Staff roles are
    /// kept out of <see cref="WhoParser"/>'s People list for exactly that reason.
    /// </para>
    /// </remarks>
    private static bool CountsOnlyStaff(string line) =>
        StaffRolePattern().IsMatch(line) && !PlayerNounPattern().IsMatch(line);

    [GeneratedRegex(
        @"\b(?:wizards?|imm(?:ortal)?s?|immortals?|gods?|admins?|administrators?|staff"
        + @"|developers?|devs?|builders?|creators?|coders?)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex StaffRolePattern();

    [GeneratedRegex(
        @"\b(?:players?|users?|characters?|people|persons?|folks?|adventurers?|mortals?)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex PlayerNounPattern();

    // "Players Currently Online: 218" / "Players online: 42" / "Currently connected: 7" /
    // "Currently On-Line: 12" / "Number of players on: 8"
    // The label is mandatory. A bare number in ASCII art is never a count. Whose count it is, is
    // CountsOnlyStaff's question, asked over the whole line before this pattern is run at all.
    //
    // The third alternative is the one shape where a bare "on" is admitted, and it is narrow on
    // purpose: the people-noun must be present, immediately followed by "on", immediately followed by
    // a colon or dash and the number (nannymud's "Number of players on:   8"). Without all three
    // anchors "on" is a preposition and would read "3 messages on the board" as a population.
    [GeneratedRegex(
        @"\b(?:players?|users?|characters?|adventurers?)?\s*(?:currently\s+)?"
        + @"(?:on-?line|connected|playing|logged\s*(?:in|on))\s*[:\-]?\s*(?<n>\d{1,5})\b"
        + @"|\b(?:players?|users?)\s*(?:currently\s+)?[:\-]\s*(?<n>\d{1,5})\b"
        + @"|\b(?:players?|users?|characters?|adventurers?)\s+on\s*[:\-]\s*(?<n>\d{1,5})\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex LabelledCountPattern();

    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex AnsiPattern();
}
