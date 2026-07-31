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
    private static readonly string[] FamilyMarkers =
    [
        "pennmush", "rhostmush", "tinymush", "tinymux", "aresmush", "cobramush",
        "muck", "tinymuck", "mudos", "fluffos", "lpmud", "moo", "evennia",
        "coffeemud", "smaug", "dikumud", "circlemud", "tbamud", "rom"
    ];

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
        foreach (var line in Lines(text))
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
                if (release is not null && (MentionsKnownFamily(release) || ContainsDigit(release)))
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
        FamilyMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
