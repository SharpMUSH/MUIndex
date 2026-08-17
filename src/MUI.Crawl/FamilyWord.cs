namespace MUI.Crawl;

/// <summary>
/// Whether a piece of free text <em>names</em> a codebase family, rather than happening to contain
/// its letters.
/// </summary>
/// <remarks>
/// <para>
/// <b>Measured on <c>darcness.net:4201</c>.</b> Its <c>INFO</c> block says <c>Name: RetroMUX</c> and
/// <c>Version: MUX 2.12.0.10</c>, and a plain substring search finds <c>rom</c> inside
/// <c>RetroMUX</c> — so the reader published <c>ROM MUX 2.12.0.10</c>, a Diku derivative's name glued
/// to a TinyMUD derivative's version, for a game that had claimed neither. <c>from</c>, <c>chrome</c>
/// and <c>Rome</c> are the same bug waiting; so is <c>moo</c> inside <c>smooth</c>.
/// </para>
/// <para>
/// Rule 4 does not stop at "no value where there is no reading". <b>A wrong value is worse than
/// none</b>, because it is the one the page shows and nobody thinks to doubt — and this one would
/// have gone out under <c>banner</c>, which is measured, on games with no MSSP to contradict it.
/// </para>
/// <para>
/// A letter or digit before the marker disqualifies it, and a letter after does. A digit after does
/// not: <c>ROM24</c> and <c>CircleMUD3</c> are how these are written in the wild, while
/// <c>RetroMUX</c> and <c>MUCKer</c> are caught by the other two edges.
/// </para>
/// <para>
/// It lives here rather than beside either caller because both <see cref="LoginCommandReading"/> and
/// <see cref="CodebaseCredits"/> decide the same question about the same names, and a second copy of
/// this rule is a second place for the <c>RetroMUX</c> bug to come back.
/// </para>
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
