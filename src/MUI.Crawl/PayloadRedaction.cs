using System.Text;

namespace MUI.Crawl;

/// <summary>
/// Turns a probe payload into something a parser can be replayed against and a person cannot be
/// found in (spec §11).
/// </summary>
/// <remarks>
/// <para>
/// <b>§11 asks for two things that cannot both be had, and this is the resolution.</b> It wants raw
/// payloads kept briefly so parser improvements can be replayed, and it wants them redacted of names
/// before they touch disk. The payload worth replaying is the <c>WHO</c> — that is where the parser
/// fails — and it is also the only place a player name appears. Redacting names out of it means
/// locating them, which means parsing it correctly, which is exactly what failed. <b>The payloads
/// most worth keeping are the ones a name-finding redactor cannot clean.</b>
/// </para>
/// <para>
/// So the text is not kept at all. What is kept is its <em>shape</em>: every column position, every
/// run of whitespace, every digit and every punctuation mark exactly where it was, with each run of
/// letters replaced by a mask of the same length unless it is a word the parser itself reads. The
/// parser is structural (see <see cref="WhoParser"/>) — it finds a summary sentence, a column header
/// and the rows beneath — so structure is the whole of what a replay needs, and a name is never part
/// of it.
/// </para>
/// <para>
/// <b>Digits survive verbatim, and that is deliberate.</b> "There are 16 players connected" is the
/// statement the parser most wants to re-read, and a redaction that masked the 16 would keep the
/// sentence and destroy the measurement in it. A number of players is not a person.
/// </para>
/// <para>
/// <b>The honest limit:</b> a player calling themselves <c>Connected</c> keeps their name, because
/// the word is in the vocabulary and the mask cannot tell the two apart. What survives is one
/// dictionary word in a column, attributable to nobody, and the alternative — dropping the
/// vocabulary — costs every summary sentence the parser exists to read. It is stated here rather
/// than papered over.
/// </para>
/// </remarks>
public static class PayloadRedaction
{
    /// <summary>
    /// The words the parser reads, which are the only letters allowed through.
    /// </summary>
    /// <remarks>
    /// Every entry earns its place by appearing in one of <see cref="WhoParser"/>'s patterns: the
    /// nouns it counts people with, the connectivity words that qualify a count, the column headings
    /// it finds a table by, the number words it spells out, and the login-prompt words that stop a
    /// DIKU "no character by that name" being read as a measured zero. A word not on this list is
    /// one no parser decision turns on, so masking it costs a replay nothing.
    /// </remarks>
    public static readonly IReadOnlySet<string> Vocabulary = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // What a server calls the people on it.
        "player", "players", "user", "users", "character", "characters", "people", "person",
        "persons", "folk", "folks", "nobody", "no", "none", "zero", "one", "two", "three", "four",
        "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen",
        "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty",

        // The connectivity qualifier, which is the whole defence against reading "no character by
        // that name found" as an empty game.
        "connected", "online", "logged", "in", "on", "active", "playing", "visible", "there", "are",
        "is", "was", "were", "currently", "now", "total", "record",

        // Column headings, which is how a table is found at all.
        "name", "idle", "for", "doing", "room", "level", "class", "race", "since", "location",
        "status", "port", "host", "flags",

        // Login prompts. A payload that is one of these must go on being recognisable as one.
        "by", "that", "found", "enter", "the", "create", "a", "new", "what", "your", "password",
        "type", "welcome", "please", "and", "of", "to",
    };

    /// <summary>The character a masked upper-case letter becomes.</summary>
    private const char Upper = 'A';

    /// <summary>The character a masked lower-case letter becomes.</summary>
    private const char Lower = 'a';

    /// <summary>
    /// The payload with every name-shaped run masked and every structural feature intact.
    /// </summary>
    /// <remarks>
    /// Length-preserving, character for character, because a MU* <c>WHO</c> is a column layout and a
    /// redaction that changed a run's length would move every column after it — replaying a
    /// positional parser against that would measure the redactor.
    /// </remarks>
    public static string Structural(string? payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return string.Empty;
        }

        var masked = new StringBuilder(payload.Length);
        var run = new StringBuilder();

        foreach (var character in payload)
        {
            if (char.IsLetter(character))
            {
                run.Append(character);
                continue;
            }

            Flush(masked, run);
            masked.Append(character);
        }

        Flush(masked, run);

        return masked.ToString();
    }

    private static void Flush(StringBuilder masked, StringBuilder run)
    {
        if (run.Length == 0)
        {
            return;
        }

        var word = run.ToString();

        if (Vocabulary.Contains(word))
        {
            masked.Append(word);
        }
        else
        {
            foreach (var character in word)
            {
                masked.Append(char.IsUpper(character) ? Upper : Lower);
            }
        }

        run.Clear();
    }
}
