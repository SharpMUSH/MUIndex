using System.Globalization;
using System.Text.RegularExpressions;

namespace MUI.Crawl;

/// <summary>
/// Reads a pre-login <c>WHO</c> / <c>DOING</c> response structurally rather than per-codebase.
/// </summary>
/// <remarks>
/// Operators can rewrite the <c>DOING</c> header in softcode (e.g. <c>Player Name  On For  Idle
/// ThereIsNoSpoonButIWantYogurt</c>), so a dialect table keyed on the word "Doing" isn't reliable.
/// Parsing instead finds the summary the server prints for itself, falling back to counting rows.
/// </remarks>
public sealed partial class WhoParser : IWhoParser
{
    /// <summary>
    /// Reads a <c>WHO</c> response. Never returns <see cref="WhoReading.NotAsked"/> — this method is
    /// only ever handed the answer to a question that was put, so deciding nothing was asked belongs
    /// to whoever owns the socket.
    /// </summary>
    public WhoReading Parse(string? response)
    {
        // Silence in the WHO window is still an answer — some servers eat the word at a login prompt
        // and reply with nothing at all.
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

        // A server that did not understand WHO must never be read as an answer to it: DIKU-family
        // games treat the login prompt as a character-name prompt, so "WHO" comes back as "No
        // character by that name found." — one careless regex away from a false zero on a game with
        // hundreds of players online.
        var loginPrompt = meaningful.Any(LooksLikeLoginPrompt);

        // 1. The server's own summary, which is the only statement here it makes deliberately.
        //
        // A positive summary outranks the loginPrompt veto — it must beat a zero but never beat a
        // number. Some games print a count on the connect screen and then re-prompt for a name in
        // the same breath ("There are three people connected to the game." / "Please enter a name:"),
        // and suppressing that reading would lose a count the server volunteered.
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
        //
        // LoginPrompt rather than Unreadable: Unreadable says our parser met a dialect it could not
        // read (a defect with a fix); this says the server never had a WHO to answer at all.
        if (loginPrompt)
        {
            return WhoReading.LoginPrompt;
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
            // A rule drawn straight under the header is part of the header, not the first player —
            // some rules (e.g. `- ------…`) don't match the "starts with ---" terminator, so they'd
            // otherwise count as a row. Skipped rather than terminated on, since a rule at the top of
            // a table opens the rows rather than ending them.
            var first = headerIndex + 1;
            if (first < meaningful.Count && IsRule(meaningful[first]))
            {
                first++;
            }

            var rows = meaningful
                .Skip(first)
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
    /// "There are no players connected." must read as a measured zero, not unparseable — a
    /// number-only pattern would throw that away (rule 2). Public because a connect screen states a
    /// count the same way; sharing this reader with <c>BannerCount</c> keeps the two from drifting.
    /// </remarks>
    public static bool TryStatedCount(string line, out int count)
    {
        ArgumentNullException.ThrowIfNull(line);

        return TrySummary(line, out count);
    }

    private static bool TrySummary(string line, out int count)
    {
        count = 0;

        // "N of M" is checked first, because every pattern below it would otherwise match M: "There
        // are currently 11 out of 200 users playing." would record the 200-user licence ceiling as
        // the population — read backwards, and worse than unreadable because it looks like a real
        // measurement. Both spellings, since the MOO shape "one of three players are active" has the
        // same trap in words.
        var outOfWords = WordedOutOfPattern().Match(line);
        if (outOfWords.Success
            && Words.TryGetValue(outOfWords.Groups["w"].Value.ToLowerInvariant(), out count))
        {
            return true;
        }

        var outOf = NumberedOutOfPattern().Match(line);
        if (outOf.Success && int.TryParse(outOf.Groups["n"].Value, out count))
        {
            return true;
        }

        // Spelled-out counts are real ("There are seven people connected.") and would otherwise be
        // unparseable against a digits-only pattern. Bounded to twenty — past that no server spells
        // it out, and an open-ended word-number parser is a liability.
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

        var wordedOn = WordedOnPattern().Match(line);
        if (wordedOn.Success
            && Words.TryGetValue(wordedOn.Groups["w"].Value.ToLowerInvariant(), out count))
        {
            return true;
        }

        var announced = AnnouncedPattern().Match(line);
        if (announced.Success && int.TryParse(announced.Groups["n"].Value, out count))
        {
            return true;
        }

        var footer = MuckFooterPattern().Match(line);
        if (footer.Success && int.TryParse(footer.Groups["n"].Value, out count))
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
    /// A header naming the list ("Connected players: A, B, C") is the whole licence to count commas —
    /// a bare comma-separated line states nothing, and every item still has to look like a name.
    /// Wrapping is followed only while the text so far ends in a comma; guessing wider risks an
    /// undercount, which is as much a fabricated measurement as an invented zero.
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
            // "Connected players: 15" is a labelled count, not a list of one, and "Connected
            // players: none" is a measured zero — both outrank anything inferred from punctuation.
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

    /// <summary>
    /// Whether this line is the rule a table's rows begin under.
    /// </summary>
    /// <remarks>
    /// The third shape covers non-English tables (e.g. Dutch <c>R Naam … Online Idle Bezig</c>):
    /// <c>Online</c> and <c>Idle</c> are borrowed rather than translated in most codebases, and
    /// requiring both keeps the pair from matching a sentence that merely uses one of them.
    /// </remarks>
    private static bool IsColumnHeader(string line) =>
        line.Contains("Player Name", StringComparison.OrdinalIgnoreCase)
        || (line.Contains("Name", StringComparison.OrdinalIgnoreCase)
            && (line.Contains("Idle", StringComparison.OrdinalIgnoreCase)
                || line.Contains("On For", StringComparison.OrdinalIgnoreCase)))
        || (line.Contains("Online", StringComparison.OrdinalIgnoreCase)
            && line.Contains("Idle", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether this line is nothing but rule — dashes, equals signs and whitespace, no text.
    /// </summary>
    /// <remarks>
    /// Stricter than <see cref="IsTerminator"/>'s <c>---</c> prefix check, since skipping a line that
    /// turned out to be a player would undercount a game — any letter or digit means it's a row.
    /// </remarks>
    private static bool IsRule(string line) =>
        line.Any(ch => ch is '-' or '=' or '+')
        && line.All(ch => ch is '-' or '=' or '+' || char.IsWhiteSpace(ch));

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
    /// Without this qualifier, <c>no\s+characters?</c> matches "No character by that name found."
    /// and a busy DIKU reports a fabricated zero player count. Every shape below is guarded by it.
    /// Bare <c>logged</c> is admitted, but <c>logged out</c>/<c>logged off</c> are refused by name —
    /// "There are no logged players." is a real measured zero, and the opposite claim is one word away.
    /// </remarks>
    /// <remarks>
    /// <c>on-?line</c> is admitted beside <c>online</c>, and <c>realm</c> and <c>world</c> beside
    /// <c>in the game</c> — all measured on live connect screens in the 2026-08-20 sweep
    /// (<c>lusternia.com:5000</c>, <c>primaldarkness.com:5000</c>, <c>atlasmud.com:4445</c>'s
    /// "There are currently 0 players in the world of Atlas.", which was a measured zero being
    /// discarded as unreadable).
    /// </remarks>
    private const string Connectivity =
        @"(?:connected|on-?line|logged(?!\s+(?:out|off))(?:\s*(?:in|on))?|playing|active"
        + @"|in\s+the\s+(?:game|realm|world))";

    /// <summary>Nouns a server uses for the people on it.</summary>
    /// <remarks>
    /// <para>
    /// <c>adventurers</c> and <c>mortals</c> were added from the 2026-08-20 sweep
    /// (<c>merentha.com:10000</c> "18 adventurers playing", <c>zombiemud.org:3000</c> "33 mortals and
    /// 4 wizards online").
    /// </para>
    /// <para>
    /// <b><c>wizards</c>, <c>immortals</c> and <c>staff</c> are deliberately absent.</b> A game that
    /// counts its staff separately is stating two numbers, and the one this project means by "players"
    /// is the mortal one — so leaving the staff noun unknown reads "33 mortals and 4 wizards online" as
    /// 33 rather than as a conflict. Adding them would make the two figures collide and
    /// <see cref="BannerCount.Find"/> would refuse the screen entirely, which is how that sentence read
    /// before this. Summing them is not on offer either: that would be our arithmetic presented as
    /// their statement.
    /// </para>
    /// </remarks>
    private const string People =
        @"(?:players?|users?|characters?|people|persons?|folks?|adventurers?|mortals?)";

    /// <summary>
    /// Words a server may put between the number and the noun it counts (e.g. "39 connected
    /// players" vs. "currently 39 connected players"). Bounded to two, lazily, so a whole clause
    /// can't be swallowed on the way to a noun that means something else.
    /// </summary>
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

    /// <summary>
    /// A zero the screen spells out rather than printing as a digit — and it is a <em>measured</em>
    /// zero, so it must parse rather than fall through to unreadable.
    /// </summary>
    /// <remarks>
    /// The separator after <c>no</c> admits a hyphen as well as a space: <c>arcanetides.net:3000</c>
    /// writes "No-one is playing at the moment.", which a whitespace-only separator read as
    /// unparseable — a live game with nobody in it, filed as a game we could not count. It cannot run
    /// away into <c>non-</c> or <c>none</c>-prefixed words, since at least one space or hyphen must
    /// follow <c>no</c> for it to match at all.
    /// </remarks>
    // "There are no players connected." / "No players are online." / "Nobody is logged in." /
    // "No-one is playing at the moment."
    //
    // A bare "no" must be followed by "one" or by a people-noun. It was previously allowed to stand
    // alone with the noun optional, which let "No wizards online" read as a measured zero — a game
    // whose staff are all present, published as empty. Whoever is absent has to be somebody this
    // parser counts.
    [GeneratedRegex(
        @"\b(?:nobody|no[\s-]+(?:one|" + People + @"))\b[^.\n]{0,40}?\b" + Connectivity + @"\b",
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

    // "There are 11 characters on, of which are visible to you." — the loosest word admitted, and
    // deliberately the narrowest rule: bare `on` counts only directly after a people-noun, since
    // elsewhere it's a preposition ("3 messages on the board").
    [GeneratedRegex(
        @"\b(?<n>\d+)\s+" + Intervening + People + @"\s+on\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex NumberedOnPattern();

    /// <summary>
    /// The same narrow bare-<c>on</c> shape with the number spelled out — <c>vikingmud.org:2001</c>
    /// prints "There are currently nine players on."
    /// </summary>
    [GeneratedRegex(
        @"\b(?<w>one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen"
        + @"|fifteen|sixteen|seventeen|eighteen|nineteen|twenty)\s+" + Intervening + People + @"\s+on\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex WordedOnPattern();

    /// <summary>
    /// A server announcing its own figure with no connectivity word anywhere near it (e.g.
    /// <c>There are 122 local users.</c>).
    /// </summary>
    /// <remarks>
    /// Two anchors replace the connectivity qualifier: <c>There are …</c> must lead straight into the
    /// number (conjunctions excluded, so a range like "between 6 and 12 characters long" can't sneak
    /// in through the adjective slot), and the sentence must end right after the noun — "There are 20
    /// new players registered today." carries on into an unrelated qualifier and is refused. Checked
    /// after <see cref="NumberedOnPattern"/> and the ceiling shapes so a "N out of M" sentence is
    /// read by the pattern that knows which number is the population.
    /// </remarks>
    [GeneratedRegex(
        @"\bthere\s+(?:are|is)\s+(?<n>\d+)\s+(?:(?!and\b|or\b|to\b)[a-z]+\s+)?" + People + @"\s*[.!]",
        RegexOptions.IgnoreCase)]
    private static partial Regex AnnouncedPattern();

    /// <summary>
    /// The rule a Fuzzball MUCK draws under its <c>WHO</c> table, which carries the total (e.g.
    /// <c>--[…]--[0 users; 0d 00h]--</c>). The bracketed <c>N users;</c> shape is an unambiguous
    /// anchor across the whole codebase family, avoiding a row count for a table this parser already
    /// reads a header for.
    /// </summary>
    [GeneratedRegex(@"\[\s*(?<n>\d+)\s+users?\s*[;\]]", RegexOptions.IgnoreCase)]
    private static partial Regex MuckFooterPattern();

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

    /// <summary>
    /// The words that mark the <em>second</em> number as a ceiling rather than a population.
    /// </summary>
    /// <remarks>
    /// A bare <c>of</c> is not enough: "There are currently 11 out of 200 users playing" has a
    /// ceiling (200 licensed, 11 connected), but "one of three players are active" doesn't (three
    /// connected, one active) — identical grammar, opposite meaning. The marker must be explicit:
    /// <c>out of</c>, or an <c>of</c> that names a maximum.
    /// </remarks>
    private const string Ceiling =
        @"\s+(?:out\s+of\s+(?:a\s+)?(?:max(?:imum)?\s+(?:of\s+)?)?|of\s+(?:a\s+)?max(?:imum)?\s+(?:of\s+)?)";

    // "There are currently 11 out of 200 users playing." — the population, not the licence.
    [GeneratedRegex(
        @"\b(?<n>\d+)" + Ceiling + @"\d+\s+" + People + @"\b[^.\n]{0,40}?\b" + Connectivity + @"\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex NumberedOutOfPattern();

    // "one out of twenty players are online." The bare "one of three players are active" is
    // deliberately NOT this shape — see Ceiling.
    [GeneratedRegex(
        @"\b(?<w>one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen"
        + @"|fifteen|sixteen|seventeen|eighteen|nineteen|twenty)" + Ceiling
        + @"(?:\d+|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen"
        + @"|fifteen|sixteen|seventeen|eighteen|nineteen|twenty)\s+" + People
        + @"\b[^.\n]{0,40}?\b" + Connectivity + @"\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex WordedOutOfPattern();

    // Login prompts that mean WHO was eaten as a character name. Vocabulary is wider than one
    // codebase: a game may ask for an *account* name rather than a character's, print nothing but
    // "Name:", refuse a disliked name outright ("Illegal name, try again.", "That name is reserved
    // for a senior member of the mud."), or ask us to confirm the spelling ("Did I get that right,
    // Who (Y/N)?").
    [GeneratedRegex(
        @"no\s+character\s+by\s+that\s+name|enter\s+(?:the\s+|your\s+)?(?:\w+\s+)?name"
        + @"|create\s+a\s+new\s+(?:character|account|player)"
        + @"|what\s+is\s+your\s+name|password\s*:|type\s+'?new'?|\bname\s*:\s*$"
        + @"|\billegal\s+name\b|\bno\s+record\s+found\s+for\b|\bname\s+is\s+reserved\b"
        + @"|\bdid\s+i\s+get\s+that\s+right\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex LoginPromptPattern();

    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex AnsiPattern();
}
