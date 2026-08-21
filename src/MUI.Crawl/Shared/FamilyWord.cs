namespace MUI.Crawl;

/// <summary>
/// Whether a piece of free text <em>names</em> a codebase family, rather than happening to contain
/// its letters.
/// </summary>
/// <remarks>
/// A plain substring search finds <c>rom</c> inside <c>RetroMUX</c> or <c>moo</c> inside
/// <c>smooth</c> — a wrong value is worse than none, since it is what the page shows and nobody
/// thinks to doubt. A letter or digit before the marker disqualifies a match, and a letter after
/// does too, but a digit does not: <c>ROM24</c> and <c>CircleMUD3</c> are how these are written in
/// the wild. Shared by <see cref="LoginCommandReading"/> and <see cref="CodebaseCredits"/> so this
/// rule exists in one place.
/// </remarks>
public static class FamilyWord
{
    /// <summary>Whether <paramref name="value"/> names <paramref name="marker"/> as a word.</summary>
    public static bool Names(string value, string marker)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(marker);

        if (marker.Length == 0)
        {
            return false;
        }

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
