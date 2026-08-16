using System.Globalization;
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
    /// <summary>
    /// Reads a <c>WHO</c> response. Every unreadable outcome is
    /// <see cref="WhoReading.Unreadable"/> and never <see cref="WhoReading.NotAsked"/>: this method
    /// is only ever handed the answer to a question that was put, so "we could not read it" is the
    /// most this parser is ever entitled to say. Deciding that nothing was asked belongs to whoever
    /// owns the socket.
    /// </summary>
    public WhoReading Parse(string? response)
    {
        // Silence in the WHO window is still an answer to a WHO that went out — servers that eat the
        // word at a login prompt say nothing at all (alteraeon.com, realms.reichel.net, measured).
        if (string.IsNullOrWhiteSpace(response))
        {
            return WhoReading.Unreadable;
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
            return WhoReading.Unreadable;
        }

        // A server that did not understand WHO must never be read as an answer to it. DIKU-family
        // games treat the login prompt as a character-name prompt, so "WHO" comes back as
        // "No character by that name found." — which is one careless regex away from being reported
        // as a measured zero for a game with hundreds of players online. Observed on alteraeon.com.
        var loginPrompt = meaningful.Any(LooksLikeLoginPrompt);

        // 1. The server's own summary, which is the only statement here it makes deliberately.
        //
        // **A positive summary outranks the veto, and that is the whole of what the veto is for.**
        // The danger it guards is a *fabricated zero* — "No character by that name" read as nobody
        // being on — so it has to beat a zero and must not beat a number. Several games print their
        // count on the connect screen and then re-prompt for a name in the same breath: the-burbs
        // answers "There are three people connected to the game." and then "Please enter a name:",
        // and suppressing that reading loses a count the server volunteered. Observed in production
        // when this veto's vocabulary was widened and a game that had been reporting three players
        // went dark.
        foreach (var line in Enumerable.Reverse(meaningful).Take(6))
        {
            if (TrySummary(line, out var counted) && (counted > 0 || !loginPrompt))
            {
                return new WhoReading(WhoConfidence.Count, counted);
            }
        }

        // Everything below reads structure rather than a sentence — a roster, a header, rows — and
        // none of it is safe on a payload that is really a login prompt: a refusal message split
        // over four lines is four rows to anything counting rows.
        if (loginPrompt)
        {
            return WhoReading.Unreadable;
        }

        // 2. A list of who is on, in place of a number. Several games answer WHO with names and no
        //    total at all, and the header — a People noun beside a connectivity word, then a colon —
        //    is what makes the commas after it countable rather than merely present.
        if (TryNameList(meaningful, out var listed))
        {
            // Count and not PerPlayer: we have names and nothing else, and §11's aggregates are made
            // of idle times. Claiming per-player confidence here would promise a column that is not
            // there.
            return new WhoReading(WhoConfidence.Count, listed);
        }

        // 3. Failing that, count the rows between the header and whatever ends them.
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

        // 4. Nothing legible. Never guess — an invented zero is indistinguishable from an empty
        //    game, and would render a healthy server as dead (spec §5.4).
        return WhoReading.Unreadable;
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

        if (NoPlayersPattern().IsMatch(line) || NoneOfThemPattern().IsMatch(line))
        {
            return true;
        }

        var numbered = NumberedPattern().Match(line);
        if (numbered.Success && int.TryParse(numbered.Groups["n"].Value, out count))
        {
            return true;
        }

        var adjectival = NumberedAdjectivePattern().Match(line);
        if (adjectival.Success && int.TryParse(adjectival.Groups["n"].Value, out count))
        {
            return true;
        }

        var on = NumberedOnPattern().Match(line);
        if (on.Success && int.TryParse(on.Groups["n"].Value, out count))
        {
            return true;
        }

        var labelled = LabelledPattern().Match(line);
        return labelled.Success && int.TryParse(labelled.Groups["n"].Value, out count);
    }

    /// <summary>
    /// A reply that lists who is on and never says how many, read as the count of the list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The header is the whole of the licence to count commas.</b> A line reading
    /// "<c>Connected players: A, B, C</c>" states what its list is; a bare comma-separated line
    /// states nothing, and counting one would be reading a sentence's punctuation as a measurement.
    /// So the same <c>People</c>-beside-<c>Connectivity</c> guard the numeric shapes use has to hold
    /// before the first item is looked at, and every item then has to look like a name — a list of
    /// anything else is something we do not understand wearing a header we do.
    /// </para>
    /// <para>
    /// Wrapping is followed only while the text so far ends in a comma. A server that wrapped its
    /// list mid-item has plainly not finished it; one whose line ends anywhere else has, and the next
    /// line is a footer, a prompt or the rest of the screen. Guessing wider would undercount or
    /// overcount silently, and an undercount is a fabricated measurement exactly as much as an
    /// invented zero is.
    /// </para>
    /// </remarks>
    private static bool TryNameList(IReadOnlyList<string> lines, out int count)
    {
        count = 0;

        for (var at = 0; at < lines.Count; at++)
        {
            var header = NameListHeaderPattern().Match(lines[at]);
            if (!header.Success)
            {
                continue;
            }

            var listed = header.Groups["list"].Value.Trim();

            for (var next = at + 1; next < lines.Count && listed.EndsWith(','); next++)
            {
                listed = $"{listed} {lines[next].Trim()}";
            }

            return TryCountItems(listed, out count);
        }

        return false;
    }

    /// <summary>The items of a delimited list, when every one of them is a name.</summary>
    private static bool TryCountItems(string listed, out int count)
    {
        count = 0;

        var items = listed
            .Split(',')
            .SelectMany(part => AndPattern().Split(part))
            .Select(item => item.Trim().Trim('.', ';', '!'))
            .Where(item => item.Length > 0)
            .ToList();

        if (items.Count == 0)
        {
            return false;
        }

        if (items.Count == 1)
        {
            // "Connected players: 15" is a labelled count wearing a connectivity word rather than a
            // list of one, and "Connected players: none" is a measured zero. Both are the server
            // stating its own figure, which outranks anything we could infer from punctuation.
            if (int.TryParse(items[0], NumberStyles.None, CultureInfo.InvariantCulture, out count))
            {
                return true;
            }

            if (Nobody.Contains(items[0]))
            {
                count = 0;
                return true;
            }
        }

        if (!items.TrueForAll(item => NamePattern().IsMatch(item)))
        {
            return false;
        }

        count = items.Count;
        return true;
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
    /// <para>
    /// The qualifier is the whole defence. Without it, <c>no\s+characters?</c> matches "No character
    /// by that name found." and a busy DIKU reports zero players — a fabricated measurement, which
    /// is worse than admitting we could not tell. <b>Every shape below is guarded by it</b>, on one
    /// side of the noun or the other, and nothing here reads a number that is not next to one.
    /// </para>
    /// <para>
    /// <b>Bare <c>logged</c> is admitted, and <c>logged out</c>/<c>logged off</c> are refused by
    /// name.</b> "There are no logged players." is a real payload and a real measured zero; the
    /// opposite claim is one word away, so it is excluded here rather than left to the shape that
    /// happens to be reading.
    /// </para>
    /// </remarks>
    private const string Connectivity =
        @"(?:connected|online|logged(?!\s+(?:out|off))(?:\s*(?:in|on))?|playing|active|in\s+the\s+game)";

    /// <summary>Nouns a server uses for the people on it.</summary>
    private const string People = @"(?:players?|users?|characters?|people|persons?|folks?)";

    /// <summary>
    /// Words a server may put between the number and the noun it counts.
    /// </summary>
    /// <remarks>
    /// "There are currently 39 connected players." was recorded unmeasurable because the noun was
    /// not where the pattern insisted it be. Two words, lazily, so the shortest reading wins and a
    /// whole clause cannot be swallowed on the way to a noun that means something else.
    /// </remarks>
    private const string Intervening = @"(?:\w+\s+){0,2}?";

    /// <summary>How a listed <c>WHO</c> says the list is empty. A measured zero, not a name.</summary>
    private static readonly HashSet<string> Nobody =
        new(["none", "nobody", "no one", "no-one"], StringComparer.OrdinalIgnoreCase);

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

    // "There are no logged players." — the connectivity word standing where an adjective would.
    [GeneratedRegex(
        @"\bno(?:body)?\s+" + Intervening + Connectivity + @"\s+" + Intervening + People + @"\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex NoneOfThemPattern();

    // "There are 16 players connected." / "16 Players logged in, 41 record"
    [GeneratedRegex(
        @"\b(?<n>\d+)\s+" + Intervening + People + @"\b[^.\n]{0,40}?\b" + Connectivity + @"\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex NumberedPattern();

    // "There are currently 39 connected players." — connectivity in front of the noun rather than
    // after it, which is the same claim written the other way round.
    [GeneratedRegex(
        @"\b(?<n>\d+)\s+" + Intervening + Connectivity + @"\s+" + Intervening + People + @"\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex NumberedAdjectivePattern();

    // "There are 11 characters on, of which are visible to you."
    //
    // The loosest word this parser admits, and deliberately the narrowest rule: bare `on` counts
    // only where it directly follows a noun that already means people. Anywhere else it is a
    // preposition — "3 messages on the board" — and reading one as a player count would be the
    // fabrication every other guard here exists to prevent.
    [GeneratedRegex(
        @"\b(?<n>\d+)\s+" + Intervening + People + @"\s+on\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex NumberedOnPattern();

    // "Connected players: A, B, C" / "The following people are logged on: A, B, C" — a header that
    // says what its list is, which is the only thing that makes the commas after it countable.
    [GeneratedRegex(
        @"^.*?\b(?:" + Connectivity + @"\s+" + People
        + @"|" + People + @"\b[^:\n]{0,30}?\b" + Connectivity + @")\b[^:\n]{0,12}:\s*(?<list>\S.*)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex NameListHeaderPattern();

    // A name, and not a phrase. MU* names carry apostrophes and hyphens and never spaces.
    [GeneratedRegex(@"^\p{L}[\p{L}\p{N}'\-]*$")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"\s+and\s+", RegexOptions.IgnoreCase)]
    private static partial Regex AndPattern();

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

    // Login prompts that mean WHO was eaten as a character name. Roughly ninety of the 122 payloads
    // stored `who_unparseable` in the first months of real crawling are one of these, and the
    // vocabulary is wider than one codebase: a game may ask for an *account* name rather than a
    // character's, and may print nothing but "Name:" after refusing the one it was given.
    [GeneratedRegex(
        @"no\s+character\s+by\s+that\s+name|enter\s+(?:the\s+|your\s+)?(?:\w+\s+)?name"
        + @"|create\s+a\s+new\s+(?:character|account|player)"
        + @"|what\s+is\s+your\s+name|password\s*:|type\s+'?new'?|\bname\s*:\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex LoginPromptPattern();

    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex AnsiPattern();
}
