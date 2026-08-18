using System.Text.RegularExpressions;

namespace MUI.Crawl;

/// <summary>
/// Whether a connect screen is not a connect screen at all, but the one question a server asks
/// before it paints one.
/// </summary>
/// <remarks>
/// <para>
/// The probe sends a Return to flush telnet residue, and normally throws away whatever comes back —
/// a reaction to bytes we chose to send is neither the game's connect screen nor its <c>WHO</c>
/// answer. This is the one case where that rule reads backwards: when the server has asked a
/// colour/screen-reader question and stopped, the Return is the answer to it, and what follows is
/// the connect screen the game meant to show. Recording the question itself as the connect screen
/// would put a decision of ours into the game's record.
/// </para>
/// <para>
/// The test is deliberately that the banner <em>is</em> the question, not that it contains one —
/// extending a full banner across the flush would sweep in whatever a stray Return produces (a
/// goodbye on a DIKU, a repaint on a TinyMUSH). Length is the cheap half of the test; the question
/// mark is the load-bearing half.
/// </para>
/// <para>
/// Not to be confused with the client-detection pause (e.g. <c>Detecting client, please wait...</c>),
/// which is the opposite fact: the server is not waiting on us and will paint on its own — see
/// <see cref="ProbeOptions.BannerPatience"/>. Those end in a full stop or ellipsis, never a question.
/// </para>
/// </remarks>
public static partial class BannerGate
{
    /// <summary>
    /// The longest a screen can be and still be nothing but a question. Generous enough for a title
    /// line and a rule above the question, far below any screen with art in it.
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

        // A question mark, or a trailing ":"/">" prompt for servers that colour past their own
        // punctuation, signals the server has stopped talking. Anything with neither is a screen
        // still mid-sentence (e.g. "Detecting client, please wait...") — BannerPatience's problem,
        // not this one.
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
    /// Matches the colour/ansi word alone, without requiring "ansi" beside it — some live servers ask
    /// e.g. "Do you see COLOR?" with no other qualifier.
    /// </remarks>
    [GeneratedRegex(
        @"\b(?:do|would|will)\s+you\b.{0,80}?\b(?:ansi|colou?rs?)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex ColourQuestionPattern();

    /// <summary>
    /// The same gate asked as an accessibility question, which is the same fact about the socket.
    /// </summary>
    /// <remarks>
    /// Deliberately does not match a screen-reader statement phrased as an instruction rather than a
    /// question (e.g. one ending in a full stop) — this pattern doesn't get to decide it was one.
    /// </remarks>
    [GeneratedRegex(@"\bscreen\s*-?\s*reader\b", RegexOptions.IgnoreCase)]
    private static partial Regex ScreenReaderQuestionPattern();
}
