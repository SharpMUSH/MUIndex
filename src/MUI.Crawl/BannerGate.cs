using System.Text.RegularExpressions;

namespace MUI.Crawl;

/// <summary>
/// Whether a connect screen is not a connect screen at all, but the one question a server asks
/// before it paints one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Measured, and the numbers are the argument.</b> Of the active games in the catalogue on
/// 2026-08-16, 66 had a stored connect screen under 130 characters and 17 of those were a single
/// question about colour or a screen reader. Three were dialled by hand to see what was behind it:
/// </para>
/// <code>
/// tharel.net:5005            stored 23 chars  "Do you want ANSI? (Y/n)"          → 3033 after one Return
/// mdhoria.net:6996           stored 101 chars "Do you want ANSI color (Y/N)?"    → 2826 after one Return
/// cleftofdimension.com:9000  stored 29 chars  "Do you want ANSI color? [Y/n]"    → 2197 after one Return
/// </code>
/// <para>
/// The probe already sends that Return — it is the flush that clears telnet residue out of the
/// server's line buffer — and <c>TelnetProbe</c> throws everything it produces away, on the correct
/// general rule that a reaction to bytes we chose to send is neither the game's connect screen nor
/// its answer to <c>WHO</c>. <b>This is the one case where that rule reads the situation backwards.</b>
/// The server asked a question and stopped; the Return is the answer to it, and the screen that
/// follows is the connect screen the game meant to show, painted at the moment it always paints it.
/// Recording the question as the game's connect screen is the thing that puts a decision of ours into
/// their record — a game whose screen names its codebase and states its player count, filed as 23
/// characters of prompt.
/// </para>
/// <para>
/// <b>So the test is deliberately that the banner <em>is</em> the question, not that it contains
/// one.</b> A game that paints a full screen and then asks about colour has already told us
/// everything; extending its banner across the flush would sweep in whatever it says to a stray
/// Return, which on a DIKU is a goodbye and on a TinyMUSH is the screen repainted. Length is the
/// cheap half of that test and the question mark is the load-bearing half: a server that has asked
/// is a server that is waiting, and one that is waiting has not painted yet.
/// </para>
/// <para>
/// <b>Not to be confused with the client-detection pause</b>, which looks similar and is the
/// opposite fact. <c>Detecting client, please wait...</c> (the-7th-plane),
/// <c>Attempting to detect client, please wait...</c> (chaosmud) and
/// <c>Identificazione del client in corso...</c> (clessidra) are servers telling us they are *not*
/// waiting for us — they will paint on their own, and <see cref="ProbeOptions.BannerPatience"/>
/// already waits for them. Those end in a full stop or an ellipsis and never in a question, which is
/// what keeps the two apart here.
/// </para>
/// </remarks>
public static partial class BannerGate
{
    /// <summary>
    /// The longest a screen can be and still be nothing but a question. Generous enough for the
    /// measured cases that carry a title line and a rule above the question — Cities of M'Dhoria
    /// spends 99 characters getting to it — and far below any screen with art in it.
    /// </summary>
    public const int LongestQuestion = 220;

    /// <summary>
    /// Whether this screen is a question the server is waiting on, so that the terminator the probe
    /// is about to send is an answer to it rather than a stray line.
    /// </summary>
    public static bool IsAnsweredByReturn(string? banner)
    {
        var text = BannerText.Flatten(banner ?? string.Empty);

        if (text.Length is 0 or > LongestQuestion)
        {
            return false;
        }

        // A server that has asked has stopped talking, and a question mark is how it says so. The
        // trailing-prompt alternative is for the servers that colour past their own punctuation:
        // dark-shattered-lands ends "Do you want color? (Y/N) ->", and legends-of-krynn paints the
        // word COLOR after the question mark one letter per escape sequence.
        //
        // Anything with neither is a screen still mid-sentence — "Detecting client, please wait..."
        // — which is BannerPatience's problem and not this one, and which is why the ellipsis and the
        // full stop are both absent from this test.
        if (!text.Contains('?', StringComparison.Ordinal) && text[^1] is not (':' or '>'))
        {
            return false;
        }

        return ColourQuestionPattern().IsMatch(text) || ScreenReaderQuestionPattern().IsMatch(text);
    }

    /// <summary>
    /// The question, in the spellings live servers ask it.
    /// </summary>
    /// <remarks>
    /// Every branch is measured. <c>Do you want ANSI? (Y/n)</c> (tharel.net, pendulum-s-calm),
    /// <c>Do you want Colour? (Y/n)</c> (emperia), <c>Would you like ansi color?</c>
    /// (escape-from-destiny), <c>Do you want color? (Y/N) -&gt;</c> (dark-shattered-lands),
    /// <c>Do you wish to use ANSI colors? (Y/n):</c> (necromium),
    /// <c>Do you want text color (yes/no) ?</c> (nilgiri),
    /// <c>do you want full ANSI color text?</c> (act-of-war), and
    /// <c>Do you see COLOR?</c> (legends-of-krynn) — which is why the colour word alone, with no
    /// <c>ansi</c> beside it, has to be enough.
    /// </remarks>
    [GeneratedRegex(
        @"\b(?:do|would|will)\s+you\b.{0,80}?\b(?:ansi|colou?rs?)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex ColourQuestionPattern();

    /// <summary>
    /// The same gate asked as an accessibility question, which is the same fact about the socket.
    /// </summary>
    /// <remarks>
    /// <c>Screen reader user? Yes or No</c> (harshlands),
    /// <c>View End of Time in full (Y) or screen reader (N) mode?</c> (end-of-time), and
    /// <c>Welcome to Medieval Times MUD. If you are using a screen reader please type yes, else,
    /// enter no.</c> — the last of which ends in a full stop and so is deliberately not matched: it
    /// is not phrased as a question and this reader does not get to decide it was one.
    /// </remarks>
    [GeneratedRegex(@"\bscreen\s*-?\s*reader\b", RegexOptions.IgnoreCase)]
    private static partial Regex ScreenReaderQuestionPattern();
}
