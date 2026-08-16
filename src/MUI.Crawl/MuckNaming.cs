namespace MUI.Crawl;

/// <summary>
/// A game that calls itself a MUCK is one, unless something it published says otherwise.
/// </summary>
/// <remarks>
/// <para>
/// This is the one place where a codebase is <em>assumed</em> rather than read, and the licence is
/// narrow on purpose. MUCKs are the family least likely to tell us what they run — Fuzzball offers
/// no MSSP, answers no <c>INFO</c>, and its <c>VERSION</c> is not a reply
/// <see cref="LoginCommandReading"/> can label — so of the 23 games in the catalogue whose own text
/// says <c>MUCK</c>, 21 carried no codebase at all. What they do have is the word itself, in the
/// name they wear on their connect screen: <c>FurryMuck</c>, <c>Tapestries MUCK</c>,
/// <c>tinymuck</c>, <c>Tiny-Muck *2.2* fb5.60</c>.
/// </para>
/// <para>
/// <b>It is an assumption and is treated as the weakest thing on the ladder.</b> The value lands on
/// the <c>banner</c> source — the bottom rung of both ladders — and the caller
/// only asks at all when nothing better exists: no <c>CODEBASE</c> or <c>FAMILY</c> declared over
/// MSSP, and no version banner <see cref="LoginCommandReading.MeaningfulCodebase"/> could read. A
/// game that says what it runs is never guessed over.
/// </para>
/// <para>
/// <b>The contradiction guard is the whole safety of it, and it was measured.</b>
/// <c>mud.stick.org:9000</c> is listed as <em>Stick in the Mud</em>: a ROM/Merc/Diku derivative
/// whose ASCII art reads <c>"Don't get Stuck... in da Muck"</c>. The word is there; so are
/// <c>Original DikuMUD</c>, <c>Based on MERC 2.1</c> and <c>Based on ROM II</c>, three lines lower.
/// Any other known family named anywhere in the same text withdraws the assumption entirely, which
/// is what keeps that game a MUD. Of the 23 games whose text says <c>MUCK</c> it is the only one the
/// guard rejects, and the only one that is not a MUCK.
/// </para>
/// <para>
/// <b>What is read is the game's own words, never ours.</b> The connect screen, the pre-login
/// command replies, the MSSP <c>NAME</c> and the address it answers at are all theirs and all read
/// by us on this probe, so the result is a measurement in the sense §6.2 means. A name a member of
/// staff typed is not — deriving a codebase from a name we chose and publishing it as measured is
/// rule 5 exactly, and a game whose only claim to being a MUCK is our own listing name is a staff
/// field write by a person, not a crawler inference wearing the same coat.
/// </para>
/// </remarks>
public static class MuckNaming
{
    /// <summary>The codebase assumed, and the family <c>content/reference/codebase-muck.md</c> is about.</summary>
    public const string Codebase = "MUCK";

    private const string Marker = "muck";

    /// <summary>
    /// Words that end in the marker without a game being named. English is short of these, which is
    /// what makes matching a suffix safe at all: <c>FurryMUCK</c> and <c>run amuck</c> are told apart
    /// by a list of two, where insisting on a word boundary before the marker would lose
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
    /// <b>Fuzzball is the reason this is not an <c>EndsWith</c> and nothing else.</b>
    /// <c>feathermuck.avians.net</c> tells its own history on its connect screen — "Originally we ran
    /// Tiny-Muck 2.2 fb5.60, now Fuzzball on UNIX box" — and a guard that read <c>Fuzzball</c> as a
    /// competing family withdrew the assumption from the most MUCK-shaped banner in the catalogue.
    /// Measured, on the whole of it: this was the one game the rule lost that way.
    /// </remarks>
    private static readonly string[] AlsoTheMuckLine = ["Fuzzball"];

    /// <summary>
    /// Whether the text names a known family that is not the MUCK line.
    /// </summary>
    /// <remarks>
    /// <c>TinyMUCK</c> and <c>ProtoMUCK</c> agree with the assumption rather than rejecting it, and
    /// so does any spelling of the word a later survey adds — which is what the suffix test is for,
    /// because a family added to <see cref="LoginCommandReading"/> must not silently become a reason
    /// to disbelieve the family it belongs to.
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
    /// A letter after the marker disqualifies it — <c>MUCKer</c> and <c>mucking about</c> are other
    /// words — while a letter before it does not, because that is how these games are written:
    /// <c>FurryMUCK</c>, <c>fluffmuck.org</c>, <c>PokemonFusionMUCK@…</c>. A digit after is kept, for
    /// <c>TinyMUCK2.3b2</c> as <c>mud.pegasus…</c> sends it.
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
