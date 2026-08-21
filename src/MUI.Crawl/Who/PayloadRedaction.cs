using System.Text;

namespace MUI.Crawl;

/// <summary>
/// Turns a probe payload into something a parser can be replayed against and a person cannot be
/// found in (spec §11).
/// </summary>
/// <remarks>
/// §11 wants raw payloads kept briefly to replay parser improvements against, but redacted of names
/// before they touch disk — and the only payload worth replaying (<c>WHO</c>) is also the only place
/// a name appears, so a name-finding redactor would have to parse it correctly first, which is
/// exactly what failed. The resolution: keep the <em>shape</em> instead of the text — every column
/// position, whitespace run and punctuation mark intact, with letter runs masked to the same length
/// unless they're a word the parser itself reads (see <see cref="WhoParser"/>, which is structural).
/// Digits survive only on a line that also carries a vocabulary word (a count needs its sentence,
/// e.g. "16 players connected"), since a bare number elsewhere is as likely to be a name
/// (<c>12345</c>) as a count. Honest limit: a player named <c>Connected</c> keeps their name, because
/// the mask can't distinguish a vocabulary word from a name that happens to match one.
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

        // Login prompts: a stored shape must replay to the same verdict the live parser reached, or
        // e.g. "Illegal name, try again." masks to unreadable and the login-prompt verdict is lost.
        "by", "that", "found", "enter", "the", "create", "a", "new", "what", "your", "password",
        "type", "welcome", "please", "and", "of", "to",
        "illegal", "reserved", "did", "i", "get", "right",
    };

    /// <summary>The character a masked upper-case letter becomes.</summary>
    private const char Upper = 'A';

    /// <summary>The character a masked lower-case letter becomes.</summary>
    private const char Lower = 'a';

    /// <summary>
    /// The character a masked digit becomes.
    /// </summary>
    /// <remarks>Masked wherever a digit could be part of a name rather than a count.</remarks>
    private const char Digit = '0';

    /// <summary>
    /// The payload with every name-shaped run masked and every structural feature intact.
    /// </summary>
    /// <remarks>
    /// Length-preserving, character for character — a <c>WHO</c> is a column layout, and shifting a
    /// run's length would move every column after it and make a positional parser measure the
    /// redactor instead of the server.
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
