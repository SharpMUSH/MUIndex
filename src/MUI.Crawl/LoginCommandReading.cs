namespace MUI.Crawl;

/// <summary>
/// Reads pre-login command replies (<c>INFO</c>, <c>VERSION</c>) conservatively.
/// </summary>
/// <remarks>
/// These commands are intentionally free-form and vary by codebase and by game configuration, so this
/// reader only returns a value when the text explicitly labels one (for example <c>Version:</c> or
/// <c>Codebase:</c>) or when a <c>VERSION</c> line clearly names a known family.
/// </remarks>
public static class LoginCommandReading
{
    private static readonly string[] CodebaseLabels = ["codebase", "server", "engine", "family"];
    private static readonly string[] VersionLabels = ["version", "release"];
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
        ["mudos"] = "MudOS",
        ["fluffos"] = "FluffOS",
        ["lpmud"] = "LPMud",
        ["moo"] = "MOO",
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
                    return release;
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

            if (MentionsKnownFamily(value) && ContainsDigit(value))
            {
                return value;
            }
        }

        return null;
    }

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

            // Rhost-style wrappers.
            if (line.StartsWith("### Begin ", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("### End ", StringComparison.OrdinalIgnoreCase))
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

    private static bool ContainsDigit(string value) => value.Any(char.IsDigit);

    private static bool MentionsKnownFamily(string value) =>
        FamilyNames.Keys.Any(marker => NamesFamily(value, marker));

    private static string? FamilyFrom(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            foreach (var (marker, canonical) in FamilyNames)
            {
                if (NamesFamily(line, marker))
                {
                    return canonical;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the text names this family as a word, rather than containing it as a fragment of one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Measured on <c>darcness.net:4201</c>.</b> Its <c>INFO</c> block says <c>Name: RetroMUX</c>
    /// and <c>Version: MUX 2.12.0.10</c>, and a plain substring search finds <c>rom</c> inside
    /// <c>RetroMUX</c> — so the reader published <c>ROM MUX 2.12.0.10</c>, a Diku derivative's name
    /// glued to a TinyMUD derivative's version, for a game that had claimed neither. <c>from</c>,
    /// <c>chrome</c> and <c>Rome</c> are the same bug waiting; so is <c>moo</c> inside <c>smooth</c>.
    /// </para>
    /// <para>
    /// Rule 4 does not stop at "no value where there is no reading". <b>A wrong value is worse than
    /// none</b>, because it is the one the page shows and nobody thinks to doubt — and this one would
    /// have gone out under <c>banner</c>, which is measured, on games with no MSSP to contradict it.
    /// </para>
    /// <para>
    /// A letter or digit before the marker disqualifies it, and a letter after does. A digit after
    /// does not: <c>ROM24</c> and <c>CircleMUD3</c> are how these are written in the wild, while
    /// <c>RetroMUX</c> and <c>MUCKer</c> are caught by the other two edges.
    /// </para>
    /// </remarks>
    private static bool NamesFamily(string value, string marker)
    {
        for (var at = 0; (at = value.IndexOf(marker, at, StringComparison.OrdinalIgnoreCase)) >= 0; at += marker.Length)
        {
            var end = at + marker.Length;

            if ((at == 0 || !char.IsLetterOrDigit(value[at - 1]))
                && (end == value.Length || !char.IsLetter(value[end])))
            {
                return true;
            }
        }

        return false;
    }
}
