using System.Text.RegularExpressions;

namespace MUI.Crawl;

/// <summary>
/// Reads a pre-login <c>WHO</c> / <c>DOING</c> response structurally rather than per-codebase.
/// </summary>
/// <remarks>
/// <para>
/// Penn, MUX, Rhost and the TinyMUD family all let operators rewrite the <c>DOING</c> header in
/// softcode. A real example, from the first server this crawler probed:
/// </para>
/// <code>
/// Player Name          On For   Idle  ThereIsNoSpoonButIWantYogurt
/// </code>
/// <para>
/// A dialect table keyed on the word "Doing" reads nothing there, which is why parsing is
/// structural: find the summary the server prints for itself, and only fall back to counting rows.
/// </para>
/// </remarks>
public sealed partial class WhoParser : IWhoParser
{
    public WhoReading Parse(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return WhoReading.Unread;
        }

        var lines = StripAnsi(response)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .Select(l => l.TrimEnd())
            .ToList();

        var meaningful = lines.Where(l => l.Trim().Length > 0).ToList();
        if (meaningful.Count == 0)
        {
            return WhoReading.Unread;
        }

        // A server that did not understand WHO must never be read as an answer to it. DIKU-family
        // games treat the login prompt as a character-name prompt, so "WHO" comes back as
        // "No character by that name found." — which is one careless regex away from being reported
        // as a measured zero for a game with hundreds of players online. Observed on alteraeon.com.
        if (meaningful.Any(LooksLikeLoginPrompt))
        {
            return WhoReading.Unread;
        }

        // 1. The server's own summary, which is the only statement here it makes deliberately.
        foreach (var line in Enumerable.Reverse(meaningful).Take(6))
        {
            if (TrySummary(line, out var counted))
            {
                return new WhoReading(WhoConfidence.Count, counted);
            }
        }

        // 2. Failing that, count the rows between the header and whatever ends them.
        var headerIndex = meaningful.FindIndex(IsColumnHeader);
        if (headerIndex >= 0)
        {
            var rows = meaningful
                .Skip(headerIndex + 1)
                .TakeWhile(l => !IsTerminator(l))
                .Count();

            // The name column is positionally identifiable once a header is found, which is what
            // unlocks §11's anonymised aggregates. Below this, only a bare count is honest.
            return new WhoReading(WhoConfidence.PerPlayer, rows, rows);
        }

        // 3. Nothing legible. Never guess — an invented zero is indistinguishable from an empty
        //    game, and would render a healthy server as dead (spec §5.4).
        return WhoReading.Unread;
    }

    /// <summary>
    /// A server's own count, in the shapes real servers actually print it.
    /// </summary>
    /// <remarks>
    /// <b><c>no players</c> means zero, not unparseable.</b> Observed on eldertaleonline.com:7705,
    /// which prints "There are no players connected." A number-only pattern reads that as a failure
    /// and stores "we could not tell" — throwing away a genuine measured zero, which is precisely
    /// the distinction rule 2 exists to protect.
    /// </remarks>
    private static bool TrySummary(string line, out int count)
    {
        count = 0;

        // Spelled-out counts are real. resort.org:2323 says "There are seven people connected." and
        // a MOO says "one of three players are active." — both would read as unparseable against a
        // digits-only pattern, losing a count we could have had. Bounded to twenty because past that
        // no server spells it out, and an open-ended word-number parser is a liability.
        var worded = WordedPattern().Match(line);
        if (worded.Success && Words.TryGetValue(worded.Groups["w"].Value.ToLowerInvariant(), out count))
        {
            return true;
        }

        var none = NoPlayersPattern().Match(line);
        if (none.Success)
        {
            return true;
        }

        var numbered = NumberedPattern().Match(line);
        if (numbered.Success && int.TryParse(numbered.Groups["n"].Value, out count))
        {
            return true;
        }

        var labelled = LabelledPattern().Match(line);
        return labelled.Success && int.TryParse(labelled.Groups["n"].Value, out count);
    }

    private static bool IsColumnHeader(string line) =>
        line.Contains("Player Name", StringComparison.OrdinalIgnoreCase)
        || (line.Contains("Name", StringComparison.OrdinalIgnoreCase)
            && (line.Contains("Idle", StringComparison.OrdinalIgnoreCase)
                || line.Contains("On For", StringComparison.OrdinalIgnoreCase)));

    private static bool IsTerminator(string line) =>
        TrySummary(line, out _)
        || line.TrimStart().StartsWith("---", StringComparison.Ordinal)
        || line.TrimStart().StartsWith("===", StringComparison.Ordinal);

    /// <summary>
    /// Whether the server answered the login prompt rather than the question.
    /// </summary>
    private static bool LooksLikeLoginPrompt(string line) => LoginPromptPattern().IsMatch(line);

    private static string StripAnsi(string text) => AnsiPattern().Replace(text, string.Empty);

    /// <summary>
    /// A count only counts when the sentence is about people being <em>connected</em>.
    /// </summary>
    /// <remarks>
    /// The qualifier is the whole defence. Without it, <c>no\s+characters?</c> matches "No character
    /// by that name found." and a busy DIKU reports zero players — a fabricated measurement, which
    /// is worse than admitting we could not tell.
    /// </remarks>
    private const string Connectivity = @"(?:connected|online|logged\s*(?:in|on)|playing|active|in\s+the\s+game)";

    /// <summary>Nouns a server uses for the people on it.</summary>
    private const string People = @"(?:players?|users?|characters?|people|persons?|folks?)";

    private static readonly Dictionary<string, int> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["no"] = 0, ["zero"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4,
        ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10,
        ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13, ["fourteen"] = 14, ["fifteen"] = 15,
        ["sixteen"] = 16, ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19, ["twenty"] = 20,
    };

    // "There are no players connected." / "No players are online." / "Nobody is logged in."
    [GeneratedRegex(
        @"\bno(?:body)?\s+(?:" + People + @"|one)?\b[^.\n]{0,40}?\b" + Connectivity + @"\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex NoPlayersPattern();

    // "There are 16 players connected." / "16 Players logged in, 41 record"
    [GeneratedRegex(
        @"\b(?<n>\d+)\s+" + People + @"\b[^.\n]{0,40}?\b" + Connectivity + @"\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex NumberedPattern();

    // "Players: 5" — a labelled field, unambiguous without a connectivity word.
    [GeneratedRegex(@"^\s*(?:players?|users?)\s*[:=]\s*(?<n>\d+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex LabelledPattern();

    // "There are seven people connected." / "one of three players are active."
    [GeneratedRegex(
        @"\b(?<w>one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen"
        + @"|fifteen|sixteen|seventeen|eighteen|nineteen|twenty)\s+" + People
        + @"\b[^.\n]{0,40}?\b" + Connectivity + @"\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex WordedPattern();

    // Login prompts that mean WHO was eaten as a character name.
    [GeneratedRegex(
        @"no\s+character\s+by\s+that\s+name|enter\s+(?:the\s+)?name|create\s+a\s+new\s+character"
        + @"|what\s+is\s+your\s+name|password\s*:|type\s+'?new'?",
        RegexOptions.IgnoreCase)]
    private static partial Regex LoginPromptPattern();

    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex AnsiPattern();
}
