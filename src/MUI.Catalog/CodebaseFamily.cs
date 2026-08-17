namespace MUI.Catalog;

/// <summary>
/// Folding a game's identified codebase to the <em>family</em> it belongs to — <c>PennMUSH</c> from
/// <c>PennMUSH 1.8.8p0</c>.
/// </summary>
/// <remarks>
/// <para>
/// A game's <c>CODEBASE</c> carries a version — <c>PennMUSH 1.8.8p0</c>, <c>TinyMUX 2.12</c> — and
/// the question a reader asks is never version-shaped. "How many games run PennMUSH" has to gather
/// every patchlevel, so the facet counts the family and the exact string is a facet of its own.
/// </para>
/// <para>
/// <b>The fold is the whole of the matching rule, and that is deliberate.</b> This class used to
/// carry a second method — a bounded-prefix <c>Matches</c> — so that a page could ask "is this game
/// in the PennMUSH family" without folding first. Two definitions of one word is exactly the shape
/// the class docs warned about: the panel counts what <see cref="Of"/> returns, so a looser test
/// could admit a game no count included, and a facet that returns games it did not promise is the
/// one thing the panel may not do. Fold, then compare for equality. Nothing else.
/// </para>
/// <para>
/// The fold is conservative in the direction that matters. <c>ROM 2.4</c> folds to <c>ROM</c> and
/// <c>ROMulus 3</c> to <c>ROMulus</c>, so a neighbouring family is not absorbed; a trailing token
/// that does not look like a version is left alone, because truncating <c>Rhost 4.0.4 (patchlevel
/// 1)</c> mid-phrase would put a codebase on a public page under a name nobody uses.
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

        // The name is everything up to where the version starts, and a version is everything from
        // there on. Taking only a trailing version token instead was the first cut of this and it
        // reads a catalogue full of build tags as a catalogue full of codebases: MUX published four
        // ways (`MUX`, `MUX 2.12.0.3 Alpha`, `MUX 2.13.0.0-MP MPARK-ST`, `MUX 2.13.0.0-MP
        // MPARK-BB-ST`), RhostMUSH four, LDMud three. Whatever a game appends after its version is
        // a fact about its build and never the name of a different codebase.
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
    /// <b>Dotted, and that is the whole of the safety margin.</b> A hyphen followed by anything
    /// version-shaped would take <c>TMI-2</c> — a live mudlib whose name ends in a digit — and
    /// publish two games under <c>TMI</c>, which is the mid-phrase truncation this fold refuses
    /// everywhere else. Requiring a dot in the suffix costs a codebase that hyphenates a
    /// single-number version onto its name, and no such codebase is in the catalogue; it keeps
    /// <c>TD-MUDLIB</c>, <c>LambdaMOO-ToastStunt</c> and <c>PD/NM III</c> whole, which are.
    /// <para>
    /// The leftmost qualifying hyphen wins, because the version starts where it starts:
    /// <c>NC-7.0.288.cd9c3554</c> is <c>NC</c> and a build, not <c>NC-7</c> and one.
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
    /// Two shapes, both observed live. A bracketed aside — <c>Discworld lib (current)</c>,
    /// <c>CobraMUSH v0.73p4 [fspace]</c> — and a release-stage word, as in <c>RhostMUSH Alpha
    /// 4.1.0RL(A).p2</c> and <c>EmpireMUD 2.0 beta</c>. <c>version</c> is here for
    /// <c>AresMUSH version</c>, which is a label whose value went missing rather than a name.
    /// <para>
    /// Deliberately a short closed list rather than a heuristic. <c>Alter Aeon</c>, <c>Dead
    /// Souls</c>, <c>Materia Magica</c>, <c>Moral Decay</c>, <c>Midnight Sun</c> and <c>Galaxy
    /// Engine</c> are all real two-word codebase names in this catalogue, and a rule clever enough
    /// to drop <c>Alpha</c> by shape would drop one of those by accident.
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
    /// It has to begin with a digit, or a <c>v</c> and a digit, because that is the one thing every
    /// version in this catalogue has in common and the one thing no codebase name does. What may
    /// follow is generous — <c>RhostMUSH Alpha 4.1.0RL(A).p2</c> and <c>MUX 2.13.0.0-MP</c> both put
    /// punctuation a stricter rule refused inside the version, and refusing it there does not leave
    /// the version out of the name, it leaves the whole tail in.
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
