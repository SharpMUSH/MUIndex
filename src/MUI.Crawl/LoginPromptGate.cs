using System.Text.RegularExpressions;

namespace MUI.Crawl;

/// <summary>
/// What to send back when a connect screen turns out to be one or more questions a server asks
/// before it paints one, rather than the screen itself.
/// </summary>
/// <remarks>
/// <para>
/// Supersedes the old boolean <c>BannerGate.IsAnsweredByReturn</c> — a blind Return answers a colour
/// prompt only on servers that treat blank input as "yes"; several measured in
/// <c>docs/login-prompt-scan/pre_login_prompts_report.md</c> do not, and re-print the same question,
/// which is why a probe run before this fix stored the raw prompt as the game's connect screen instead
/// of the screen behind it. <see cref="Classify"/> says which bytes answer the question this screen
/// actually is, so <c>TelnetProbe</c>'s Phase 1 can send the right one rather than always a blank line.
/// </para>
/// <para>
/// The test is deliberately that the banner-so-far <em>is</em> the question, not that it contains
/// one — extending a full banner across an answer would sweep in whatever a stray reply produces (a
/// goodbye on a DIKU, a repaint on a TinyMUSH). Length is the cheap half of the test; the question
/// mark (or a trailing prompt punctuation) is the load-bearing half — except for
/// <see cref="LoginPromptCategory.PressEnter"/>, which is an imperative rather than a question and is
/// checked on its own, specific enough wording that it does not need that guard.
/// </para>
/// <para>
/// Not to be confused with the client-detection pause (e.g. <c>Detecting client, please wait...</c>),
/// which is the opposite fact: the server is not waiting on us and will paint on its own — see
/// <see cref="ProbeOptions.BannerPatience"/>. Those end in a full stop or ellipsis, never a question.
/// </para>
/// </remarks>
public static partial class LoginPromptGate
{
    /// <summary>
    /// The longest a screen can be and still be nothing but a single colour/screen-reader/age-gate
    /// question. Generous enough for a title line and a rule above the question, far below any screen
    /// with art in it.
    /// </summary>
    public const int LongestQuestion = 220;

    /// <summary>
    /// The longest a screen can be and still be a press-enter gate or a menu (charset, who's-online).
    /// Menus legitimately run to several option lines, so they get their own, more generous ceiling
    /// rather than sharing <see cref="LongestQuestion"/>'s.
    /// </summary>
    public const int LongestMenu = 800;

    /// <summary>
    /// Whether this screen-so-far is a question or menu the server is waiting on, and if so what
    /// answers it.
    /// </summary>
    public static LoginPromptAnswer? Classify(string? bannerSoFar)
    {
        var flat = BannerText.Flatten(bannerSoFar ?? string.Empty);

        if (flat.Length is 0 or > LongestMenu)
        {
            return null;
        }

        if (flat.Length <= LongestQuestion)
        {
            // A question mark (ASCII or full-width, for CJK phrasings), or a trailing ":"/">" prompt
            // for servers that colour past their own punctuation, signals the server has stopped
            // talking. Anything with neither is a screen still mid-sentence — BannerPatience's problem,
            // not this one.
            var looksLikeAQuestion = flat.Contains('?', StringComparison.Ordinal)
                || flat.Contains('？', StringComparison.Ordinal)
                || flat[^1] is ':' or '>';

            if (looksLikeAQuestion)
            {
                if (ColourQuestionPattern().IsMatch(flat))
                {
                    return new LoginPromptAnswer("y", LoginPromptCategory.Colour);
                }

                if (ScreenReaderQuestionPattern().IsMatch(flat))
                {
                    return new LoginPromptAnswer(string.Empty, LoginPromptCategory.ScreenReader);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The colour question, in the spellings live servers ask it.
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

/// <summary>Which kind of pre-login question or menu a screen turned out to be.</summary>
public enum LoginPromptCategory
{
    Colour,
    ScreenReader,
    PressEnter,
    AgeGate,
    Charset,
    WhoMenu,
}

/// <summary>The bytes that answer a classified prompt, and which category it was.</summary>
/// <param name="Answer">
/// Sent through <c>TelnetInterpreter.SendAsync</c>, which appends the line ending itself — this is
/// handed over bare, the same convention <c>TelnetProbe.AskAsync</c> already uses for WHO/INFO/VERSION.
/// </param>
public sealed record LoginPromptAnswer(string Answer, LoginPromptCategory Category);
