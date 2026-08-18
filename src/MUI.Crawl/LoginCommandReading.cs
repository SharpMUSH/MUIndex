using System.Globalization;
using System.Text.RegularExpressions;

namespace MUI.Crawl;

/// <summary>
/// Reads pre-login command replies (<c>INFO</c>, <c>VERSION</c>) conservatively.
/// </summary>
/// <remarks>
/// Free-form and codebase-dependent, so this only returns a value when the text explicitly labels
/// one (<c>Version:</c>, <c>Codebase:</c>) or a <c>VERSION</c> line clearly names a known family.
/// </remarks>
public static partial class LoginCommandReading
{
    private static readonly string[] CodebaseLabels = ["codebase", "server", "engine", "family"];
    private static readonly string[] VersionLabels = ["version", "release"];

    /// <summary>
    /// The labels an <c>INFO</c> block uses for the game's own name (<c>Mudname</c> is MSSP's spelling).
    /// </summary>
    private static readonly string[] NameLabels = ["name", "mudname", "game"];
    private static readonly IReadOnlyDictionary<string, string> FamilyNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["pennmush"] = "PennMUSH",
        ["rhostmush"] = "RhostMUSH",
        ["tinymush"] = "TinyMUSH",
        ["tinymux"] = "TinyMUX",
        ["aresmush"] = "AresMUSH",
        ["cobramush"] = "CobraMUSH",
        ["muck"] = "MUCK",
        ["tinymuck"] = "TinyMUCK",
        ["protomuck"] = "ProtoMUCK",
        ["fuzzball"] = "Fuzzball",
        ["mudos"] = "MudOS",
        ["fluffos"] = "FluffOS",
        ["lpmud"] = "LPMud",
        ["ldmud"] = "LDMud",
        ["moo"] = "MOO",

        // "MOO" is an MsspDefaults placeholder, so a LambdaMOO line matched only as "moo" would be
        // discarded rather than merely under-identified — lambdamoo must be checked as its own key.
        ["lambdamoo"] = "LambdaMOO",
        ["toaststunt"] = "ToastStunt",
        ["evennia"] = "Evennia",
        ["coffeemud"] = "CoffeeMUD",
        ["smaug"] = "SMAUG",
        ["dikumud"] = "DikuMUD",
        ["diku"] = "DikuMUD",
        ["circlemud"] = "CircleMUD",
        ["tbamud"] = "tbaMUD",
        ["rom"] = "ROM",
        ["merc"] = "Merc",
    };

    /// <summary>
    /// The best codebase/version hint from login-screen command replies, or null.
    /// </summary>
    public static string? MeaningfulCodebase(string? info, string? version)
    {
        return FromLabelledValue(info)
            ?? FromLabelledValue(version)
            ?? FromUnlabelledVersion(version);
    }

    /// <summary>
    /// The game's own name as its <c>INFO</c> block gives it, or null.
    /// </summary>
    /// <remarks>
    /// Counterpart to <c>MsspDefaults.MeaningfulName</c> for codebases (RhostMUSH among them) that
    /// answer <c>INFO</c> but offer no MSSP. Same filter, same reason: <c>Name: PennMUSH</c> merely
    /// restates the codebase and is not an identification.
    /// </remarks>
    public static string? MeaningfulName(string? info, string? version)
    {
        var codebase = MeaningfulCodebase(info, version);

        foreach (var line in Lines(info))
        {
            if (!TrySplitLabelled(line, out var label, out var value)
                || !NameLabels.Contains(label, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Clean(value) is { } named && MsspDefaults.MeaningfulName(named, codebase) is { } meaningful)
            {
                return meaningful;
            }
        }

        return null;
    }

    /// <summary>
    /// The player count an <c>INFO</c> block states about itself, or null when it stated none.
    /// </summary>
    /// <remarks>
    /// Reads PennMUSH/RhostMUSH/TinyMUX's <c>### Begin INFO 1.1</c> ... <c>### End INFO</c> block and
    /// Evennia's <c>## BEGIN INFO</c>/<c>END INFO</c> variant. Only counts a value found strictly
    /// inside a block that both opened and closed — an unterminated block is not trusted, since a bare
    /// "Connected:" outside one could mean anything. The <c>INFO_VERSION</c> on the <c>Begin</c> line
    /// is never matched against, so a future version bump doesn't silently stop this from reading.
    /// <c>Uptime:</c> is never read numerically — PennMUSH writes it as a ctime string, not a number.
    /// </remarks>
    public static int? ConnectedPlayers(string? info)
    {
        if (string.IsNullOrWhiteSpace(info))
        {
            return null;
        }

        var inside = false;
        int? connected = null;

        foreach (var raw in info.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.Trim();

            if (!inside)
            {
                inside = InfoBlockStart().IsMatch(line);
                continue;
            }

            if (InfoBlockEnd().IsMatch(line))
            {
                return connected;
            }

            if (connected is null
                && TrySplitLabelled(line, out var label, out var value)
                && label.Equals(ConnectedLabel, StringComparison.OrdinalIgnoreCase)
                // NumberStyles.None refuses a sign — a negative count means we misread the value, not
                // a small number.
                && int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var players))
            {
                connected = players;
            }
        }

        return null;
    }

    private static string? FromLabelledValue(string? text)
    {
        var lines = Lines(text).ToArray();
        var family = FamilyFrom(lines);

        foreach (var line in lines)
        {
            if (!TrySplitLabelled(line, out var label, out var value))
            {
                continue;
            }

            if (CodebaseLabels.Contains(label, StringComparer.OrdinalIgnoreCase))
            {
                var named = Clean(value);
                if (named is not null)
                {
                    return named;
                }
            }

            if (VersionLabels.Contains(label, StringComparer.OrdinalIgnoreCase))
            {
                var release = Clean(value);
                if (release is null)
                {
                    continue;
                }

                if (MentionsKnownFamily(release))
                {
                    return Extract(release) ?? release;
                }

                if (family is not null && ContainsDigit(release))
                {
                    return $"{family} {release}";
                }

                if (ContainsDigit(release))
                {
                    return release;
                }
            }
        }

        return null;
    }

    private static string? FromUnlabelledVersion(string? version)
    {
        foreach (var line in Lines(version))
        {
            var value = Clean(line);
            if (value is null)
            {
                continue;
            }

            if (MentionsKnownFamily(value) && Extract(value) is { } named)
            {
                return named;
            }
        }

        return null;
    }

    /// <summary>
    /// The codebase named inside a line of prose, and nothing else from that line.
    /// </summary>
    /// <remarks>
    /// A line that merely mentions a codebase must not be returned intact as the codebase (a parser
    /// that can't tell the codebase from the surrounding prose should keep only the part it
    /// recognises — rule 4). The longest matching marker wins: "…of the LambdaMOO server code"
    /// contains both <c>MOO</c> and <c>LambdaMOO</c>, and <c>MOO</c> alone is an
    /// <see cref="MsspDefaults"/> placeholder that would throw away a real identification.
    /// </remarks>
    /// <summary>
    /// How long a line can be and still be a codebase rather than prose about one.
    /// </summary>
    /// <remarks>
    /// Generous bound so a line that opens with a codebase name (e.g. "PennMUSH 1.8.8p0 running
    /// at…") can't take the whole-line path purely on its first word.
    /// </remarks>
    private const int LongestPlausibleCodebase = 48;

    private static string? Extract(string line)
    {
        var marker = FamilyNames
            // Must be NamesFamily (word boundary), not Contains — "smooth 1.0" contains `moo`,
            // "mucked about 2.1" contains `muck`, and a wrong codebase is worse than none.
            .Where(pair => NamesFamily(line, pair.Key))
            .OrderByDescending(pair => pair.Key.Length)
            .Select(pair => (Key: pair.Key, Canonical: pair.Value))
            .FirstOrDefault();

        if (marker.Key is null)
        {
            return null;
        }

        // A line that begins with the codebase is the codebase, build number included (e.g.
        // `TinyMUX 2.14.0.4 #22`) — extraction below is only for lines that merely mention one.
        var trimmed = line.Trim();

        if (trimmed.Length <= LongestPlausibleCodebase && OpensWith(trimmed, marker.Key))
        {
            return MsspDefaults.IsPlaceholder(trimmed) ? null : trimmed;
        }

        var release = VersionBeside(line, marker.Key);
        var named = release is null ? marker.Canonical : $"{marker.Canonical} {release}";

        return MsspDefaults.IsPlaceholder(named) ? null : named;
    }

    /// <summary>
    /// Whether the line opens with this codebase's name, give or take surrounding punctuation
    /// (e.g. <c>(CircleMUD 3.1)</c>).
    /// </summary>
    private static bool OpensWith(string line, string marker) =>
        line.TrimStart('(', '[', '<', '{', '"', '\'', '*', ' ')
            .StartsWith(marker, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The version a line offers for the codebase it names, or null when it offers none we can pin.
    /// </summary>
    /// <remarks>
    /// Only two shapes are accepted: fused to the name (<c>TinyMUCK2.3b2</c>) or following the word
    /// <c>version</c>/<c>release</c>. Anything looser reads a copyright year as a release.
    /// </remarks>
    private static string? VersionBeside(string line, string marker)
    {
        var at = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

        // TrimStart: "PennMUSH 1.7.1" and "TinyMUCK2.3b2" are the same claim written two ways.
        var fused = Token(line[(at + marker.Length)..].TrimStart());

        if (fused.Length > 0 && char.IsAsciiDigit(fused[0]))
        {
            return fused;
        }

        foreach (var label in VersionLabels)
        {
            var said = line.IndexOf(label, StringComparison.OrdinalIgnoreCase);
            if (said < 0)
            {
                continue;
            }

            var next = Token(line[(said + label.Length)..].TrimStart());
            if (next.Length > 0 && char.IsAsciiDigit(next[0]))
            {
                return next;
            }
        }

        return null;
    }

    private static bool ContainsDigit(string value) => value.Any(char.IsDigit);

    /// <summary>One run of version-shaped characters, stopped by anything a version cannot contain.</summary>
    private static string Token(string rest) =>
        new([.. rest.TakeWhile(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or '+' or '/')]);

    private static IEnumerable<string> Lines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            // Block wrappers, in both spellings the family uses.
            if (BlockMarker().IsMatch(line))
            {
                continue;
            }

            yield return line;
        }
    }

    private static bool TrySplitLabelled(string line, out string label, out string value)
    {
        var at = line.IndexOf(':');
        if (at <= 0 || at == line.Length - 1)
        {
            label = string.Empty;
            value = string.Empty;
            return false;
        }

        label = line[..at].Trim();
        value = line[(at + 1)..].Trim();
        return label.Length > 0 && value.Length > 0;
    }

    private static string? Clean(string value)
    {
        var trimmed = value.Trim();
        return MsspDefaults.IsPlaceholder(trimmed) ? null : trimmed;
    }

    private static bool MentionsKnownFamily(string value) =>
        FamilyNames.Keys.Any(marker => NamesFamily(value, marker));

    /// <summary>
    /// Every known family the text names as a word, in canonical spelling and without repeats.
    /// </summary>
    /// <remarks>
    /// Exposes this reader's own vocabulary so <see cref="MuckNaming"/> can ask "does this text say
    /// anything else" against the same list rather than a second one that could drift from it.
    /// </remarks>
    public static IReadOnlyList<string> FamiliesNamedIn(string? text) =>
        [.. FamiliesIn(Lines(text)).Distinct(StringComparer.OrdinalIgnoreCase)];

    private static string? FamilyFrom(IEnumerable<string> lines) => FamiliesIn(lines).FirstOrDefault();

    private static IEnumerable<string> FamiliesIn(IEnumerable<string> lines) =>
        lines.SelectMany(line => FamilyNames
            .Where(family => NamesFamily(line, family.Key))
            .Select(family => family.Value));

    /// <summary>
    /// Whether the text names this family as a word, rather than containing it as a fragment of one
    /// (e.g. <c>ROM</c> inside <c>RetroMUX</c>).
    /// </summary>
    /// <remarks>
    /// Delegates to <see cref="FamilyWord"/> rather than reimplementing, since <see cref="CodebaseCredits"/>
    /// needs the identical rule and a second copy is a second place for that bug to reappear.
    /// </remarks>
    private static bool NamesFamily(string value, string marker) => FamilyWord.Names(value, marker);

    /// <summary>The label a MUSH-family <c>INFO</c> block states its player count under.</summary>
    private const string ConnectedLabel = "Connected";

    // `### Begin INFO 1.1` (PennMUSH, RhostMUSH, TinyMUX) and `## BEGIN INFO 1.1` (Evennia).
    [GeneratedRegex(@"^#{2,}\s*begin\s+info\b", RegexOptions.IgnoreCase)]
    private static partial Regex InfoBlockStart();

    [GeneratedRegex(@"^#{2,}\s*end\s+info\b", RegexOptions.IgnoreCase)]
    private static partial Regex InfoBlockEnd();

    /// <summary>Any block wrapper, whatever the block is called. Never a labelled value.</summary>
    [GeneratedRegex(@"^#{2,}\s*(?:begin|end)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BlockMarker();
}
