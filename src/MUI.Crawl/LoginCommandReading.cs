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
        FamilyNames.Keys.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static string? FamilyFrom(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            foreach (var (marker, canonical) in FamilyNames)
            {
                if (line.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return canonical;
                }
            }
        }

        return null;
    }
}
