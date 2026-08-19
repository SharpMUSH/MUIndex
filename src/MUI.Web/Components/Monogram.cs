namespace MUI.Web.Components;

/// <summary>
/// The two letters a game gets when it has published no icon (spec §11).
/// </summary>
/// <remarks>
/// <para>
/// A monogram, never a generated picture — an identicon or hashed colour would be a decoration we
/// invented and attached to somebody else's game, with no provenance to show. The letters are the
/// game's own name, set in the same recessed plate an icon would have filled.
/// </para>
/// <para>
/// Lives here rather than on either surface so the game page and the listing draw identically —
/// two spellings of "the first letters of the name" is how a game becomes <c>MU</c> on one page and
/// <c>M*</c> on the next.
/// </para>
/// </remarks>
public static class Monogram
{
    /// <summary>
    /// The separators a MU* name is built out of.
    /// </summary>
    /// <remarks>
    /// The star is here for <c>M*U*S*H</c> — one word to a reader, four to a splitter that only
    /// knows spaces, which would otherwise monogram as <c>M</c> alone. The hyphen for the same reason.
    /// </remarks>
    private static readonly char[] Separators = [' ', '*', '-'];

    /// <summary>
    /// The first letter of each of the first two words, upper-cased.
    /// </summary>
    /// <remarks>
    /// <b>Never blank.</b> A name that's entirely punctuation gets a question mark instead — an
    /// empty plate reads as a failed image load, a statement about our code rather than the game.
    /// </remarks>
    public static string Of(string? name)
    {
        var letters = string.Concat((name ?? string.Empty)
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Take(2)
            .Select(word => char.ToUpperInvariant(word[0])));

        return letters.Length == 0 ? "?" : letters;
    }
}
