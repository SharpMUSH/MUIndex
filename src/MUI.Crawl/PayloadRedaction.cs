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
/// <b>Digits survive only where a count can be.</b> "There are 16 players connected" is the
/// statement the parser most wants to re-read, and masking the 16 would keep the sentence and
/// destroy the measurement in it — a number of players is not a person. But a digit is not
/// automatically a count: <c>12345</c> is a perfectly ordinary MU* name, and <c>A1ice</c> is a name
/// with a digit in it. So a run of characters containing any letter is masked whole, digits
/// included, and a run of bare digits survives only on a line that also carries a word the parser
/// reads. A summary sentence has such a word; a row of players does not.
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
    /// The character a masked digit becomes.
    /// </summary>
    /// <remarks>
    /// Digits are masked wherever they could be part of a name — inside a run that has letters in
    /// it, and on any line that names no countable thing. <c>12345</c> is an ordinary MU* name.
    /// </remarks>
    private const char Digit = '0';

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

        var lines = payload.Split('\n');

        return string.Join('\n', lines.Select(Line));
    }

    /// <summary>
    /// A redaction the parser has been checked against, or null where it would read differently.
    /// </summary>
    /// <remarks>
    /// The replay guarantee, enforced at the moment of writing rather than only asserted in a test.
    /// A shape exists to be re-parsed; one that parses to a different answer from the payload it
    /// came from is not evidence about anything, and keeping it would let a later vocabulary change
    /// quietly fill the window with rows that lie about what the server said.
    /// </remarks>
    public static string? Replayable(string? payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return null;
        }

        var shape = Structural(payload);
        var parser = new WhoParser();
        var original = parser.Parse(payload);
        var replayed = parser.Parse(shape);

        return replayed.Confidence == original.Confidence && replayed.Count == original.Count
            ? shape
            : null;
    }

    /// <summary>
    /// One line, masked.
    /// </summary>
    /// <remarks>
    /// Line by line because whether a bare number may survive is a property of the sentence it is
    /// in: a count appears beside a word like <c>players</c> or <c>connected</c>, and a line with no
    /// such word is a row of people, where a number is as likely to be somebody's name as anything.
    /// </remarks>
    private static string Line(string line)
    {
        var runs = Split(line);
        var carriesVocabulary = runs.Any(r => Vocabulary.Contains(r));

        var masked = new StringBuilder(line.Length);

        foreach (var run in runs)
        {
            masked.Append(Mask(run, carriesVocabulary));
        }

        return masked.ToString();
    }

    /// <summary>The line as runs of word characters and runs of everything else, in order.</summary>
    private static List<string> Split(string line)
    {
        List<string> runs = [];
        var run = new StringBuilder();
        bool? wordish = null;

        foreach (var character in line)
        {
            var isWord = char.IsLetterOrDigit(character);

            if (wordish is { } previous && previous != isWord)
            {
                runs.Add(run.ToString());
                run.Clear();
            }

            wordish = isWord;
            run.Append(character);
        }

        if (run.Length > 0)
        {
            runs.Add(run.ToString());
        }

        return runs;
    }

    private static string Mask(string run, bool carriesVocabulary)
    {
        if (run.Length == 0 || !char.IsLetterOrDigit(run[0]))
        {
            // Whitespace and punctuation, exactly as they were: the column layout is the structure.
            return run;
        }

        if (Vocabulary.Contains(run))
        {
            return run;
        }

        // A bare number, on a line that says what it is a number of.
        if (carriesVocabulary && run.All(char.IsDigit))
        {
            return run;
        }

        var masked = new StringBuilder(run.Length);

        foreach (var character in run)
        {
            masked.Append(char.IsDigit(character) ? Digit : char.IsUpper(character) ? Upper : Lower);
        }

        return masked.ToString();
    }
}
