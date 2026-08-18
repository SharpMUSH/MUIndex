namespace MUI.Catalog;

/// <summary>
/// Folding a game's identified codebase to the <em>family</em> it belongs to — <c>PennMUSH</c> from
/// <c>PennMUSH 1.8.8p0</c>.
/// </summary>
/// <remarks>
/// <para>
/// A game's <c>CODEBASE</c> carries a version — <c>PennMUSH 1.8.8p0</c>, <c>TinyMUX 2.12</c> — but
/// "how many games run PennMUSH" has to gather every patchlevel, so the facet counts the family and
/// the exact string is a separate facet.
/// </para>
/// <para>
/// Matching is fold-then-equality only, deliberately with no separate "is this game in family X"
/// test — a looser match could admit a game the fold-based counts never included.
/// </para>
/// <para>
/// The fold is conservative: <c>ROM 2.4</c> folds to <c>ROM</c>, <c>ROMulus 3</c> to
/// <c>ROMulus</c> — a neighbouring family is never absorbed — and a trailing token that doesn't
/// look like a version is left alone rather than truncated mid-phrase.
/// </para>
/// </remarks>
public static class CodebaseFamily
{
    public static string Of(string codebase)
    {
        ArgumentNullException.ThrowIfNull(codebase);

        var words = codebase.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0)
        {
            return string.Empty;
        }

        // The name is everything before the version starts, not just a trailing version token —
        // MUX alone publishes as `MUX`, `MUX 2.12.0.3 Alpha`, `MUX 2.13.0.0-MP MPARK-ST` and more;
        // a trailing-token rule reads each build tag as a different codebase.
        var end = words.Length;

        for (var i = 0; i < words.Length; i++)
        {
            // Never the first word: a game whose whole CODEBASE is `2.12` has published a version
            // and no name, and folding that away would put an empty bar on the dashboard.
            if (i > 0 && LooksLikeAVersion(words[i]))
            {
                end = i;
                break;
            }

            // The version need not arrive at a space — `MorgenGrauen-3.3.5` is one word and two
            // facts. Whatever precedes the join is still name, so the cut falls after this word.
            if (JoinedVersionAt(words[i]) is { } cut)
            {
                words[i] = words[i][..cut];
                end = i + 1;
                break;
            }
        }

        // Nothing on the name's side of the cut that is not part of the name. `AnsalonMUD - 1.7b2`
        // stops before the version and leaves the dash the operator typed to separate the two.
        while (end > 1 && (IsQualifier(words[end - 1]) || IsSeparator(words[end - 1])))
        {
            end--;
        }

        return string.Join(' ', words[..end]);
    }

    /// <summary>
    /// Where a version hyphenated onto the end of a word begins, or null when the word is all name.
    /// </summary>
    /// <remarks>
    /// Requires a dot in the suffix: without it, <c>TMI-2</c> — a live mudlib whose name ends in a
    /// digit — would split into <c>TMI</c> and a version. This costs a codebase that hyphenates a
    /// bare single-number version onto its name, but none exists in the catalogue; it keeps
    /// <c>TD-MUDLIB</c>, <c>LambdaMOO-ToastStunt</c> and <c>PD/NM III</c> whole.
    /// <para>
    /// The leftmost qualifying hyphen wins: <c>NC-7.0.288.cd9c3554</c> is <c>NC</c> and a build,
    /// not <c>NC-7</c> and one.
    /// </para>
    /// </remarks>
    private static int? JoinedVersionAt(string word)
    {
        for (var at = word.IndexOf('-'); at > 0; at = word.IndexOf('-', at + 1))
        {
            var suffix = word[(at + 1)..];

            if (suffix.Contains('.') && LooksLikeAVersion(suffix))
            {
                return at;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a word is punctuation an operator typed between two facts rather than a word of the
    /// name. <c>Original / Loosely Diku</c> is three words of a name and one of them is a slash.
    /// </summary>
    private static bool IsSeparator(string word) => !word.Any(char.IsLetterOrDigit);

    /// <summary>
    /// Whether a trailing word describes the <em>build</em> rather than continuing the name.
    /// </summary>
    /// <remarks>
    /// Two shapes: a bracketed aside (<c>Discworld lib (current)</c>, <c>CobraMUSH v0.73p4
    /// [fspace]</c>) and a release-stage word (<c>RhostMUSH Alpha 4.1.0RL(A).p2</c>, <c>EmpireMUD
    /// 2.0 beta</c>). <c>version</c> covers <c>AresMUSH version</c>, a label whose value went missing.
    /// <para>
    /// Deliberately a closed list, not a heuristic: <c>Alter Aeon</c>, <c>Dead Souls</c>,
    /// <c>Materia Magica</c>, <c>Moral Decay</c>, <c>Midnight Sun</c> and <c>Galaxy Engine</c> are
    /// all real two-word codebase names in this catalogue that a shape-based rule would drop.
    /// </para>
    /// </remarks>
    private static bool IsQualifier(string word)
    {
        if (word.Length > 1 && ((word[0] is '(' && word[^1] is ')') || (word[0] is '[' && word[^1] is ']')))
        {
            return true;
        }

        return Qualifiers.Contains(word.Trim('(', ')', '[', ']'));
    }

    private static readonly HashSet<string> Qualifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "alpha", "beta", "rc", "dev", "development", "snapshot", "nightly", "unstable", "version",
    };

    /// <summary>
    /// The family a game belongs to, or null when we have not identified its codebase.
    /// </summary>
    /// <remarks>
    /// Null rather than an empty string, because the facets spell an absence as its own value: "we
    /// could not identify this game's codebase" is a measurement of our own reach and a real thing to
    /// filter on, and it is not the same question as any family's name.
    /// </remarks>
    public static string? For(string? codebase) =>
        string.IsNullOrWhiteSpace(codebase) ? null : Of(codebase) is { Length: > 0 } family ? family : null;

    /// <summary>
    /// Whether a word is where the version starts.
    /// </summary>
    /// <remarks>
    /// Must begin with a digit, or a <c>v</c> and a digit — the one thing every version in this
    /// catalogue has in common and no codebase name does. What follows is generous, since
    /// <c>RhostMUSH Alpha 4.1.0RL(A).p2</c> and <c>MUX 2.13.0.0-MP</c> both need punctuation a
    /// stricter rule would refuse.
    /// </remarks>
    private static bool LooksLikeAVersion(string token)
    {
        if (token.Length == 0)
        {
            return false;
        }

        var starts = char.IsAsciiDigit(token[0])
            || (token[0] is 'v' or 'V' && token.Length > 1 && char.IsAsciiDigit(token[1]));

        return starts
            && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or '+'
                or '(' or ')' or '[' or ']' or '/');
    }
}
