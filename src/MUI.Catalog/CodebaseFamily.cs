namespace MUI.Catalog;

/// <summary>
/// Matching a game's identified codebase against the <em>family</em> a reference page is about.
/// </summary>
/// <remarks>
/// <para>
/// A game's <c>CODEBASE</c> carries a version — <c>PennMUSH 1.8.8p0</c>, <c>TinyMUX 2.12</c> — and
/// the question a reader asks is never version-shaped. "How many games run PennMUSH" has to gather
/// every patchlevel, so the facet is a family name matched as a prefix rather than an equality.
/// </para>
/// <para>
/// The prefix is bounded on the right, which is not fussiness: <c>MOO</c> against <c>LambdaMOO</c>
/// is already handled by anchoring at the start, but <c>ROM</c> against <c>ROMulus</c> is not, and
/// a facet that silently absorbs a neighbouring family produces a count that is wrong in the
/// direction nobody checks. A match therefore ends at the string's end or at a character that is
/// not a letter or a digit.
/// </para>
/// <para>
/// It lives here rather than in the web tier because two implementations of
/// <see cref="IGameQueries"/> and the reference pages all have to agree about it, and the count a
/// page prints has to be the count the listing behind its link contains.
/// </para>
/// <para>
/// <b>Two methods because two surfaces need this, and they must agree.</b> The ecosystem
/// dashboard groups by <see cref="Of"/> and a reference page filters by
/// <see cref="Matches"/>; if those were separate implementations, a codebase page's own
/// count and the listing it links to would be free to disagree about what a family is.
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

    /// <summary>
    /// Whether <paramref name="codebase"/> — a game's identified codebase, version and all — belongs
    /// to the family named by <paramref name="family"/>. A game with no identified codebase belongs
    /// to no family: not identifying it is a measurement, and it is not a measurement of this.
    /// </summary>
    public static bool Matches(string? codebase, string? family)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(codebase))
        {
            return false;
        }

        var name = codebase.AsSpan().Trim();
        var wanted = family.AsSpan().Trim();

        if (!name.StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return name.Length == wanted.Length || !char.IsLetterOrDigit(name[wanted.Length]);
    }
}
