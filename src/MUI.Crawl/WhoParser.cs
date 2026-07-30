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

        return false;
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

    private static string StripAnsi(string text) => AnsiPattern().Replace(text, string.Empty);

    // "There are no players connected." / "No players are connected."
    [GeneratedRegex(@"\b(?:there\s+(?:are|is)\s+)?no\s+(?:players?|users?|characters?)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex NoPlayersPattern();

    // "There are 16 players connected." / "16 Players logged in, 41 record" / "Players: 5"
    [GeneratedRegex(
        @"(?:(?<n>\d+)\s+(?:players?|users?|characters?)\b)|(?:(?:players?|users?)\s*[:=]\s*(?<n>\d+))",
        RegexOptions.IgnoreCase)]
    private static partial Regex NumberedPattern();

    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex AnsiPattern();
}
