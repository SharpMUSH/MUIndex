namespace MUI.Crawl;

/// <summary>
/// A game that calls itself a MUCK is one, unless something it published says otherwise.
/// </summary>
/// <remarks>
/// <para>
/// The one place a codebase is <em>assumed</em> rather than read. MUCKs are the family least likely
/// to declare what they run — Fuzzball offers no MSSP, no <c>INFO</c>, and its <c>VERSION</c> reply
/// isn't one <see cref="LoginCommandReading"/> can label — so the word in the game's own connect
/// screen name (<c>FurryMuck</c>, <c>tinymuck</c>) is often all there is.
/// </para>
/// <para>
/// It is the weakest source on the ladder: lands on <c>banner</c>, the bottom rung, and is only
/// consulted when no <c>CODEBASE</c>/<c>FAMILY</c> was declared over MSSP and no version banner
/// <see cref="LoginCommandReading.MeaningfulCodebase"/> could read. Any other known family named
/// anywhere in the same text withdraws the assumption entirely — a game whose banner says "Muck" but
/// also credits ROM/Merc/Diku stays a MUD.
/// </para>
/// <para>
/// Only the game's own words are read, never ours — deriving a codebase from a name we chose and
/// publishing it as measured would be rule 5 exactly.
/// </para>
/// </remarks>
public static class MuckNaming
{
    /// <summary>The codebase assumed, and the family <c>content/reference/codebase-muck.md</c> is about.</summary>
    public const string Codebase = "MUCK";

    private const string Marker = "muck";

    /// <summary>
    /// Words that end in the marker without a game being named. English is short of these, which is
    /// what makes matching a suffix safe: a strict word-boundary check before the marker would lose
    /// <c>FluffMUCK</c>, <c>KitsuMUCK</c> and every <c>…muck.org</c> in the catalogue.
    /// </summary>
    private static readonly string[] NotAGame = ["amuck", "schmuck"];

    /// <summary>
    /// <see cref="Codebase"/> when one of these texts names a MUCK and none of them names anything
    /// else, or null.
    /// </summary>
    /// <param name="texts">
    /// The game's own text, in any order — a connect screen, a command reply, a declared name, a
    /// hostname. Nulls and blanks are skipped, so a caller may pass whatever it happens to have.
    /// Every text is read for a contradiction, including the ones that came after the match.
    /// </param>
    public static string? Assumed(params string?[] texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var said = false;

        foreach (var raw in texts)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            // Flattened because a connect screen is colour and layout around its words, and a family
            // name split by an SGR run is a contradiction we would not see.
            var text = BannerText.Flatten(raw);

            if (NamesAnotherFamily(text))
            {
                return null;
            }

            said |= SaysMuck(text);
        }

        return said ? Codebase : null;
    }

    /// <summary>
    /// Members of the MUCK line whose names do not contain the word.
    /// </summary>
    /// <remarks>
    /// A MUCK's connect screen sometimes narrates switching codebases (e.g. "Originally we ran
    /// Tiny-Muck 2.2 fb5.60, now Fuzzball") — without this, the family-contradiction guard below would
    /// read <c>Fuzzball</c> as a competing codebase and withdraw a correct MUCK assumption.
    /// </remarks>
    private static readonly string[] AlsoTheMuckLine = ["Fuzzball"];

    /// <summary>
    /// Whether the text names a known family that is not the MUCK line.
    /// </summary>
    /// <remarks>
    /// <c>TinyMUCK</c> and <c>ProtoMUCK</c> agree with the assumption rather than rejecting it, so a
    /// spelling added later to <see cref="LoginCommandReading"/> doesn't silently become a reason to
    /// disbelieve the family it belongs to.
    /// </remarks>
    private static bool NamesAnotherFamily(string text) =>
        LoginCommandReading.FamiliesNamedIn(text).Any(family =>
            !family.EndsWith(Codebase, StringComparison.OrdinalIgnoreCase)
            && !AlsoTheMuckLine.Contains(family, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Whether the text names a MUCK: the marker as a word or as the tail of one, never as a prefix
    /// of a longer word.
    /// </summary>
    /// <remarks>
    /// A letter after the marker disqualifies it (<c>MUCKer</c>, "mucking about" are other words); a
    /// letter before does not, since that's how these games are named (<c>FurryMUCK</c>,
    /// <c>fluffmuck.org</c>). A digit after is kept, for names like <c>TinyMUCK2.3b2</c>.
    /// </remarks>
    private static bool SaysMuck(string text)
    {
        for (var at = 0; (at = text.IndexOf(Marker, at, StringComparison.OrdinalIgnoreCase)) >= 0; at += Marker.Length)
        {
            var end = at + Marker.Length;

            if (end < text.Length && char.IsLetter(text[end]))
            {
                continue;
            }

            var start = at;
            while (start > 0 && char.IsLetter(text[start - 1]))
            {
                start--;
            }

            if (!NotAGame.Contains(text[start..end], StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
