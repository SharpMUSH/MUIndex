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

        // An imperative, not a question — checked unconditionally (within the wider LongestMenu
        // ceiling) because the phrase itself is specific enough ("press" immediately before
        // "enter"/"return", or the Korean idiom) not to need the question-mark guard the categories
        // below do, and because a long, fully-painted splash screen that ends in a press-enter cue is
        // exactly the case this exists to get past.
        if (PressEnterPattern().IsMatch(flat))
        {
            return new LoginPromptAnswer(string.Empty, LoginPromptCategory.PressEnter);
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

                if (AgeGatePattern().IsMatch(flat))
                {
                    return new LoginPromptAnswer("no", LoginPromptCategory.AgeGate);
                }
            }
        }

        if (ClassifyCharsetMenu(bannerSoFar ?? string.Empty) is { } charset)
        {
            return charset;
        }

        if (ClassifyWhoMenu(bannerSoFar ?? string.Empty) is { } who)
        {
            return who;
        }

        return null;
    }

    /// <summary>
    /// A menu option whose label is unmistakably "see who is online", found the same
    /// one-line-at-a-time way <see cref="ClassifyCharsetMenu"/> does.
    /// </summary>
    /// <remarks>
    /// Distinct from the ordinary <c>WHO</c> command this probe already sends for free at Phase 3
    /// (<c>TelnetProbe.WhoCommand</c>) — this is a *menu letter* a server prints on its permanent
    /// connect screen (BatMUD, ZombieMUD), not the command itself.
    /// </remarks>
    private static LoginPromptAnswer? ClassifyWhoMenu(string bannerSoFar)
    {
        foreach (var (token, label) in MenuOptions(bannerSoFar))
        {
            if (WhoLabelPattern().IsMatch(label))
            {
                return new LoginPromptAnswer(token, LoginPromptCategory.WhoMenu);
            }
        }

        return null;
    }

    /// <summary>
    /// A numbered or lettered menu with a legibly UTF-8 option, or null when none of its options is
    /// one — never a guess between two non-UTF-8 encodings (rule 5): a game offering only GB/BIG5 or
    /// only Cyrillic codepages stays on the existing staff <c>CHARSET</c> override, unchanged.
    /// </summary>
    /// <remarks>
    /// Read one option per <em>line</em>, unlike every other category here, which is why this does not
    /// go through the whole-blob <see cref="BannerText.Flatten"/> — that collapses newlines into
    /// spaces, which would destroy the menu's structure. Each line is flattened individually instead,
    /// which still strips ANSI/whitespace but keeps the lines apart.
    /// </remarks>
    private static LoginPromptAnswer? ClassifyCharsetMenu(string bannerSoFar)
    {
        foreach (var (token, label) in MenuOptions(bannerSoFar))
        {
            if (Utf8LabelPattern().IsMatch(label))
            {
                return new LoginPromptAnswer(token, LoginPromptCategory.Charset);
            }
        }

        return null;
    }

    /// <summary>
    /// Every <c>(token, label)</c> pair a menu offers, read one line at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A label runs from the end of its own token to the start of the next one, which is the whole
    /// point: several real menus put every option on one line — <c>batmud.bat.org:23</c> prints
    /// <c>2 - visit the game    w - who is playing at the moment</c> and <c>zombiemud.org:3000</c>
    /// prints <c>[C]reate a new character     [W]ho is playing</c>. Reading the line as one string and
    /// searching it for a keyword picks the *first* token on it, so both games were sent the token for
    /// the option before the one meant — measured live, and the reason this segments rather than scans.
    /// </para>
    /// <para>
    /// Lines are flattened individually rather than as one blob, unlike every other category here:
    /// <see cref="BannerText.Flatten"/> collapses newlines into spaces, which would run every option
    /// of a one-per-line menu together into a single label.
    /// </para>
    /// </remarks>
    private static IEnumerable<(string Token, string Label)> MenuOptions(string bannerSoFar)
    {
        var lines = bannerSoFar
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(BannerText.Flatten)
            .Where(line => line.Length > 0);

        foreach (var line in lines)
        {
            var tokens = MenuTokenPattern().Matches(line);

            for (var at = 0; at < tokens.Count; at++)
            {
                var token = tokens[at].Groups["token"].Value;
                var labelFrom = tokens[at].Index + tokens[at].Length;
                var labelTo = at + 1 < tokens.Count ? tokens[at + 1].Index : line.Length;

                if (labelTo <= labelFrom)
                {
                    continue;
                }

                var label = line[labelFrom..labelTo];

                // "[W]ho is playing" — the bracketed letter is the token *and* the first letter of the
                // label's own first word, so on its own the label reads "ho is playing" and matches
                // nothing. Nothing separates them, which is what says to put the word back together.
                yield return (
                    token,
                    tokens[at].Groups["gap"].Length == 0 ? token + label : label);
            }
        }
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

    /// <summary>
    /// A server telling us to press a key, in the shapes live servers ask it — English ("press" right
    /// before "enter"/"return", so "Please enter a name" never matches) and the Korean idiom (either
    /// the Hangul "엔터" or a literal Latin "Enter" followed within a few characters by "누르", "press").
    /// </summary>
    /// <remarks>
    /// Known gap, deliberately not covered: the "[엔터]를 입력하세요" phrasing (3-third-eye-harmony) uses
    /// "입력" ("input") rather than "누르" ("press") and is not matched — see the survey doc note this
    /// plan adds in its final task.
    /// </remarks>
    [GeneratedRegex(
        @"\bpress\s+(?:the\s+)?\[?(?:enter|return)\]?\b|\benter\b.{0,15}누르|엔터.{0,15}누르",
        RegexOptions.IgnoreCase)]
    private static partial Regex PressEnterPattern();

    /// <summary>
    /// An age check whose honest answer is <em>no</em>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Narrow by design: the live phrasing measured (menghui-xiyou/xianlv-qingyuan's "are you a
    /// primary/secondary school student or younger?") and the generic English shape of the same
    /// question. Every phrasing here asks whether the connecting user is <em>under</em> age, so a
    /// single fixed "no" is truthful for all of them.
    /// </para>
    /// <para>
    /// <b>The inverse question is deliberately not matched.</b> "Are you of legal age?" and "Are you
    /// 18 or over?" ask the opposite, and this category has one answer — "no" would decline the game
    /// rather than reach its connect screen, which is a worse outcome than not recognising the prompt
    /// at all. Matching those needs a per-phrasing answer rather than a per-category one, and no
    /// surveyed game asks it; <see cref="LoginPromptGate"/> does not get to guess which way round a
    /// question is.
    /// </para>
    /// </remarks>
    [GeneratedRegex(
        @"学生.{0,10}年龄|年龄更小|are\s+you\s+(?:a\s+)?(?:minor|under\s+\d+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex AgeGatePattern();

    /// <summary>
    /// How a menu introduces one option: a token — a single letter, or one or two digits — set off by
    /// a bracket, a full stop, a colon, or a spaced dash.
    /// </summary>
    /// <remarks>
    /// Deliberately not anchored to the start of a line, so the options of a one-line menu are all
    /// found; the lookbehind (no letter or digit immediately before the token) is what keeps it from
    /// matching inside a word instead. Digits are admitted because both menu kinds use them —
    /// charset menus almost always, and <c>eternitymud.com:23</c>/<c>taurosmud.com:5005</c> number
    /// their who's-online option rather than lettering it.
    /// </remarks>
    [GeneratedRegex(
        @"(?<![\p{L}\p{N}])[\[\(]?(?<token>[0-9]{1,2}|[A-Za-z])(?:[\]\).:]|\s+-)(?<gap>[\s.]*)")]
    private static partial Regex MenuTokenPattern();

    [GeneratedRegex(@"\bUTF-?8\b", RegexOptions.IgnoreCase)]
    private static partial Regex Utf8LabelPattern();

    /// <summary>
    /// A menu label that offers to show who is connected — "who is playing at the moment", "See who is
    /// online", "Short list of who is on-line", "List immortals online who can help".
    /// </summary>
    /// <remarks>
    /// Both halves are required and their order is not: the label must name <c>who</c> <em>and</em> a
    /// connectivity word, because either alone is an ordinary English word a non-menu label uses
    /// freely. <c>legendmud.org:9999</c> puts them the other way round ("...online who can help"),
    /// which is why this is two independent lookaheads rather than one sequence.
    /// </remarks>
    [GeneratedRegex(
        @"^(?=.*\bwho\b)(?=.*\b(?:online|playing|on[- ]?line|logged\s*(?:in|on))\b)",
        RegexOptions.IgnoreCase)]
    private static partial Regex WhoLabelPattern();
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
