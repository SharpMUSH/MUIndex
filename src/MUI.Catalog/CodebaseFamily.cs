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

        var trimmed = codebase.Trim();
        var space = trimmed.LastIndexOf(' ');

        if (space <= 0)
        {
            return trimmed;
        }

        return LooksLikeAVersion(trimmed[(space + 1)..])
            ? trimmed[..space].TrimEnd()
            : trimmed;
    }

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

    private static bool LooksLikeAVersion(string token)
    {
        if (token.Length == 0)
        {
            return false;
        }

        var starts = char.IsAsciiDigit(token[0])
            || (token[0] is 'v' or 'V' && token.Length > 1 && char.IsAsciiDigit(token[1]));

        return starts && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
    }
}
