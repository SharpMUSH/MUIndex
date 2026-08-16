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
        ["protomuck"] = "ProtoMUCK",
        ["fuzzball"] = "Fuzzball",
        ["mudos"] = "MudOS",
        ["fluffos"] = "FluffOS",
        ["lpmud"] = "LPMud",
        ["ldmud"] = "LDMud",
        ["moo"] = "MOO",

        // The specific spellings matter more here than anywhere else in this map, because "MOO" is
        // an MsspDefaults placeholder: a line naming LambdaMOO that we read as "MOO" is not merely
        // vaguer, it is discarded. Four LambdaMOOs were being stored under a whole sentence each for
        // exactly this reason — the sentence won because the only alternative was a non-answer.
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
    /// <para>
    /// <b>This exists because the whole line used to be the answer.</b> A line that merely
    /// <em>mentions</em> a codebase was returned intact and stored as the game's <c>CODEBASE</c>,
    /// which put nine sentences on the ecosystem dashboard's codebase chart as if they were
    /// codebases — "The MOO is currently running version 1.8.3+47 of the LambdaMOO server code.",
    /// "Tapestries MUCK Copyright 1991-2020 by tapestries.fur.com. All rights reserved.", "This MUCK
    /// is rated NC-17…", and a 697-character Pueblo banner from a PennMUSH whose connect screen
    /// begins with an <c>IMG</c> tag. Four separate LambdaMOOs each got a bar of their own, named
    /// after a sentence.
    /// </para>
    /// <para>
    /// A codebase is a name and at most a version. Everything else on the line is the game talking,
    /// and a parser that cannot tell the difference should keep the part it recognises rather than
    /// the part it does not — rule 4, one level up from a count.
    /// </para>
    /// <para>
    /// <b>The longest marker wins.</b> "…of the LambdaMOO server code" contains both <c>MOO</c> and
    /// <c>LambdaMOO</c>, and the shorter one is not merely less useful: <c>MOO</c> is an
    /// <see cref="MsspDefaults"/> placeholder, so preferring it would throw away an identification we
    /// actually made.
    /// </para>
    /// </remarks>
    /// <summary>
    /// How long a line can be and still be a codebase rather than prose about one.
    /// </summary>
    /// <remarks>
    /// A bound rather than a judgement, and generous: the longest real value in the catalogue is
    /// <c>RhostMUSH Alpha 4.1.0RL(A).p2</c> at twenty-nine characters, and the shortest sentence this
    /// rule has to refuse is sixty-six. It exists so that a line beginning with a codebase name — a
    /// connect screen whose first words are "PennMUSH 1.8.8p0 running at…" — cannot take the
    /// whole-line path on the strength of its first word alone.
    /// </remarks>
    private const int LongestPlausibleCodebase = 48;

    private static string? Extract(string line)
    {
        var marker = FamilyNames
            // NamesFamily rather than Contains, or this reintroduces exactly what the boundary check
            // was added to stop: "smooth 1.0" carries `moo`, "mucked about 2.1" carries `muck`, and
            // a wrong codebase is worse than none on precisely the games with no MSSP to contradict
            // it. The marker has to be a word here for the same reason it does one rung up.
            .Where(pair => NamesFamily(line, pair.Key))
            .OrderByDescending(pair => pair.Key.Length)
            .Select(pair => (Key: pair.Key, Canonical: pair.Value))
            .FirstOrDefault();

        if (marker.Key is null)
        {
            return null;
        }

        // A line that *begins* with the codebase is the codebase, and whatever follows is the game's
        // own way of writing its build — `TinyMUX 2.14.0.4 #22` is one value and not a name with a
        // sentence stuck to it. Extraction is for lines that merely mention a codebase somewhere
        // inside them, and taking it here would quietly file the build number off every honest
        // answer to pay for the dishonest ones.
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
    /// Whether the line opens with this codebase's name, give or take the punctuation around it.
    /// </summary>
    /// <remarks>
    /// A game writes its version as <c>(CircleMUD 3.1)</c> and as <c>ROM24 b6</c>, and both are the
    /// whole of what that line says. Requiring the very first character to be the name would send the
    /// parenthesised form down the extraction path and quietly file its brackets off — a value the
    /// game published, altered by us, for no reason a reader could see.
    /// </remarks>
    private static bool OpensWith(string line, string marker) =>
        line.TrimStart('(', '[', '<', '{', '"', '\'', '*', ' ')
            .StartsWith(marker, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The version a line offers for the codebase it names, or null when it offers none we can pin.
    /// </summary>
    /// <remarks>
    /// Two shapes and no more. Fused to the name — <c>Currently running TinyMUCK2.3b2!</c> — or
    /// following the word <c>version</c> or <c>release</c>, which is how the MOOs write it. Anything
    /// looser reads a copyright year as a release: "Tapestries MUCK Copyright 1991-2020" would
    /// become <c>MUCK 1991-2020</c>, which is a worse answer than admitting we have only the name.
    /// </remarks>
    private static string? VersionBeside(string line, string marker)
    {
        var at = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

        // TrimStart because "PennMUSH 1.7.1" and "TinyMUCK2.3b2" are the same claim written two
        // ways, and only one of them was being read. That cost the whole of a 697-character Pueblo
        // banner: the version was one space past the name, the space stopped the scan, and with no
        // version to pin the line was kept entire.
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
