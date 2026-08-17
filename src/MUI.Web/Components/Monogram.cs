namespace MUI.Web.Components;

/// <summary>
/// The two letters a game gets when it has published no icon (spec §11).
/// </summary>
/// <remarks>
/// <para>
/// A monogram and never a generated picture. An identicon, a hashed colour or an initial on a tinted
/// square are all decorations invented by us and attached to somebody else's game — and on a site
/// whose whole claim is that a displayed thing was measured, a graphic nobody published is the one
/// kind of content that has no provenance to show. The letters are the game's own name, set in the
/// same recessed plate an icon would have filled, so the row has a consistent left edge either way
/// and nothing on it is asserting anything.
/// </para>
/// <para>
/// It lives here rather than on either surface because the game page and the listing draw the same
/// plate: two spellings of "the first letters of the name" is how a game comes to be <c>MU</c> on one
/// page and <c>M*</c> on the next.
/// </para>
/// </remarks>
public static class Monogram
{
    /// <summary>
    /// The separators a MU* name is built out of.
    /// </summary>
    /// <remarks>
    /// The star is in here for <c>M*U*S*H</c>, which is one word to a reader and four to a splitter
    /// that only knows spaces — and would otherwise monogram as <c>M</c> alone. The hyphen is for the
    /// same reason on the other side of the hobby's naming.
    /// </remarks>
    private static readonly char[] Separators = [' ', '*', '-'];

    /// <summary>
    /// The first letter of each of the first two words, upper-cased.
    /// </summary>
    /// <remarks>
    /// <b>Never blank.</b> A name that is entirely punctuation, or an empty string a caller did not
    /// expect to be empty, gets a question mark rather than an empty plate — a plate with nothing in
    /// it reads as an image that failed to load, which is a statement about our afternoon rather than
    /// about the game.
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
