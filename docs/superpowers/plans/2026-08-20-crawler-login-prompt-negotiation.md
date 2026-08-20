# Crawler Login-Prompt Negotiation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Teach `TelnetProbe` to recognise and answer the pre-login prompts real games print before
their connect screen — colour/ANSI questions, press-enter gates, age gates, numbered charset menus
that offer UTF-8, and a "who's online" pre-login menu option — so the connect screen it stores is the
game's actual screen rather than a still-unanswered question, and so a game that only reveals its
player count through a menu gets measured at all.

**Architecture:** One new classifier, `LoginPromptGate` (replacing today's `BannerGate`), decides
*what bytes answer this screen* rather than today's yes/no "does a blind Return answer it" — several
real games only accept an explicit letter or menu digit and re-print the same question at a blind
Return, which is why their stored `connect_screen` is the raw prompt rather than the game's actual
banner. `TelnetProbe.ProbeAsync`'s Phase 1 gains a small bounded loop that classifies the
newly-arrived text after each settle and sends the classified answer, replacing the single
blind-flush-then-reinterpret trick it does today. A who's-online menu option is handled separately,
once, after the loop: selecting it doesn't reveal a second screen behind this one (the menu itself is
several of these games' actual, permanent connect screen), so its reply is parsed through the existing
`WhoParser`/`PresenceChoice` pipeline exactly as an ordinary `WHO` answer would be, and the later
literal `WHO` phase is skipped when it already fired.

**Tech Stack:** .NET 10, C# latest, TUnit on Microsoft.Testing.Platform, `TelnetNegotiationCore`
(client-mode `TelnetInterpreter`, already a dependency).

**Spec:** `docs/login-prompt-scan/pre_login_prompts_report.md` — a survey run 2026-08-20 against all
900 `game_field(field='connect_screen', source='banner')` rows in the production catalogue, quoting
the exact prompt text of ~95 real games across the categories this plan answers. **This file is
local-only** (excluded via `.git/info/exclude`, never committed — see the branch's setup) — read it
on this machine before writing test fixtures; every fixture below is either quoted or adapted directly
from it, cited by game slug.

## Global Constraints

- **Never guess an encoding and write it down as a fact about the game.** `WireEncoding.Read` already
  proves UTF-8 from bytes or accepts a staff override; nothing in this plan may add a third way to
  decide a charset. The charset-menu category below only ever *sends a keystroke that requests* an
  encoding — `WireEncoding` still independently proves what the server actually did with it.
- **Never pick a charset-menu option when no option is legibly UTF-8.** Roughly half the encoding-menu
  games surveyed offer only GB/BIG5 or only Cyrillic codepages with no UTF-8 option at all; picking one
  of those blind would corrupt the connect screen with mojibake worse than today's undetermined
  fallback. These stay on the existing staff `CHARSET`-override path, unchanged by this plan.
- **A misclassified prompt must not be able to spin a probe.** Every new send-then-settle round is
  bounded by `ProbeOptions.MaxPromptRounds` (Task 3) and gated on `Live(client)`, the same guard the
  existing `WHO`/`INFO`/`VERSION` phases already use.
- **Stage by explicit path. Never `git add -A` or `git commit -a`** (CLAUDE.md) — this branch's
  worktree also has an untracked `docs/login-prompt-scan/` directory sitting beside tracked files;
  every commit step below names its paths explicitly.
- **Tests are TUnit on Microsoft.Testing.Platform.** `dotnet test` does not work. Run:
  `dotnet build MUIndex.slnx -c Release` then
  `dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null`. There is no
  supported single-test filter flag confirmed for this runner — every "run the test" step below runs
  the whole `MUI.Crawl.Tests` suite, exactly as CLAUDE.md's own example does.

---

## Task 1: `LoginPromptGate` replaces `BannerGate` — Colour and ScreenReader, with a real answer each

**Files:**
- Create: `src/MUI.Crawl/LoginPromptGate.cs`
- Delete: `src/MUI.Crawl/BannerGate.cs`
- Create: `tests/MUI.Crawl.Tests/LoginPromptGateTests.cs`
- Delete: `tests/MUI.Crawl.Tests/BannerGateTests.cs`

**Interfaces:**
- Produces: `LoginPromptCategory` enum (`Colour`, `ScreenReader`, `PressEnter`, `AgeGate`, `Charset`,
  `WhoMenu`); `LoginPromptAnswer(string Answer, LoginPromptCategory Category)` record; static
  `LoginPromptGate.Classify(string? bannerSoFar) : LoginPromptAnswer?`. Task 3 (`TelnetProbe`) consumes
  this signature directly.

Today's `BannerGate.IsAnsweredByReturn` only ever answers "yes, a blind Return resolves this" — the
probe always sends the same blank line regardless. `LoginPromptGate.Classify` instead says *which*
bytes resolve it, because several real games (see `docs/login-prompt-scan/pre_login_prompts_report.md`
under "Color / ANSI prompts") only accept an explicit `y`, not a blank default, and re-print the same
question at a blind Return — which is what a probe run before this fix stored as the game's connect
screen. `y` is safe everywhere a blind Return was safe: every `(Y/n)` implementation measured reads
the first non-whitespace character, so an explicit `y` is strictly more compatible, never less. The
screen-reader question is `y`/`n` too, but the two live phrasings map the letters to *opposite*
meanings ("Screen reader user? Yes or No" — "no" is the crawler's answer; "View End of Time in full
(Y) or screen reader (N) mode?" — "no" would be *wrong*, selecting reader mode) — with no reliable way
to tell which phrasing this is from the regex alone, the only answer that is never wrong is a blank
Return, so this category keeps exactly today's behaviour.

- [x] **Step 1: Write the failing test**

```csharp
// tests/MUI.Crawl.Tests/LoginPromptGateTests.cs
using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// Telling a connect screen from the one or more questions a server asks before it paints one, and
/// what answers each.
/// </summary>
/// <remarks>
/// Every fixture is a stored connect screen from the live catalogue (see
/// docs/login-prompt-scan/pre_login_prompts_report.md), most under thirty characters.
/// </remarks>
public class LoginPromptGateTests
{
    [Test]
    [Arguments("Do you want ANSI? (Y/n)")]
    [Arguments("Do you want Colour? (Y/n)")]
    [Arguments("Would you like ansi color?")]
    [Arguments("Do you want color? (Y/N) -> ")]
    [Arguments("Would you like colour? (Y/n)")]
    [Arguments("Do you want ANSI color? [Y/n]")]
    [Arguments("Would you like ANSI color (Y/n/?)?")]
    [Arguments("Do you want ANSI colour? [Y/N/Return]")]
    [Arguments("Do you wish to use ANSI colors? (Y/n): ")]
    [Arguments("Do you want text color (yes/no) ?")]
    [Arguments("Would you like to use ANSI color [Y/n]?")]
    [Arguments("Welcome to Cities of M'Dhoria\n-----------------\n\nDo you want ANSI color (Y/N)?")]
    [Arguments("Greetings traveller!  Welcome to Tempora Heroica!\nWould you like to use ANSI color [Y/n]?")]
    public async Task AColourQuestionIsAnsweredWithAnExplicitYes(string banner)
    {
        var answer = LoginPromptGate.Classify(banner);

        await Assert.That(answer).IsNotNull();
        await Assert.That(answer!.Category).IsEqualTo(LoginPromptCategory.Colour);
        await Assert.That(answer.Answer).IsEqualTo("y");
    }

    [Test]
    [Arguments("Screen reader user? Yes or No")]
    [Arguments("View End of Time in full (Y) or screen reader (N) mode?")]
    public async Task AScreenReaderQuestionIsAnsweredWithABlankReturn(string banner)
    {
        // The two live phrasings map Y/N to opposite meanings — see the class-level remarks on
        // LoginPromptGate for why guessing a letter here is unsafe and blank is the only sure answer.
        var answer = LoginPromptGate.Classify(banner);

        await Assert.That(answer).IsNotNull();
        await Assert.That(answer!.Category).IsEqualTo(LoginPromptCategory.ScreenReader);
        await Assert.That(answer.Answer).IsEqualTo(string.Empty);
    }

    /// <summary>
    /// A server saying "please wait" is <em>not</em> waiting for us — it paints on its own, and
    /// <c>ProbeOptions.BannerPatience</c> already covers that. Classifying it as a gate would send a
    /// stray answer to a server that never asked anything.
    /// </summary>
    [Test]
    [Arguments("Detecting client, please wait...")]
    [Arguments("Attempting to detect client, please wait...")]
    [Arguments("Identifying client, please wait...")]
    [Arguments("Please Wait, while we attempt to detect your client...")]
    [Arguments("Identificazione del client in corso...")]
    [Arguments("Welcome to Medieval Times MUD. If you are using a screen reader\nplease type yes, else, enter no.")]
    public async Task AServerThatIsNotWaitingForUsIsNotAGate(string banner)
    {
        await Assert.That(LoginPromptGate.Classify(banner)).IsNull();
    }

    [Test]
    public async Task AScreenThatHasAlreadyPaintedIsNeverAColourGateHoweverItEnds()
    {
        var painted = new string('=', 300) + "\nDo you want ANSI colour? (Y/n)";

        await Assert.That(LoginPromptGate.Classify(painted)).IsNull();
    }

    [Test]
    [Arguments("Welcome to Nowhere MUD.")]
    [Arguments("By what name do you wish to be known?")]
    [Arguments("Please enter a character name >")]
    [Arguments("What is your name: ")]
    public async Task AnOrdinaryPromptIsNotAGate(string banner)
    {
        await Assert.That(LoginPromptGate.Classify(banner)).IsNull();
    }

    [Test]
    public async Task NothingIsAGate()
    {
        await Assert.That(LoginPromptGate.Classify(null)).IsNull();
        await Assert.That(LoginPromptGate.Classify("")).IsNull();
        await Assert.That(LoginPromptGate.Classify("   \n\n  ")).IsNull();
    }

    [Test]
    public async Task ColourPaintedPastTheQuestionMarkStillReadsAsOne()
    {
        // legends-of-krynn writes "Do you see COLOR?" with the question mark in the middle of the
        // escape sequences, so the flattened text ends on the coloured word rather than on the
        // punctuation. The question mark is looked for anywhere for exactly this.
        var krynn = "\e[1;37mDo you see\e[0m? \e[1;31mC\e[1;32mO\e[1;33mL\e[1;34mO\e[1;35mR\e[1;37m \e[0m";

        var answer = LoginPromptGate.Classify(krynn);

        await Assert.That(answer).IsNotNull();
        await Assert.That(answer!.Category).IsEqualTo(LoginPromptCategory.Colour);
    }
}
```

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release` (expected: build error, `LoginPromptGate` does not exist yet
— `BannerGateTests.cs` still references the old type at this point, so also delete it now, before the
build, per the Files list above).

- [x] **Step 3: Write `LoginPromptGate`**

```csharp
// src/MUI.Crawl/LoginPromptGate.cs
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
```

- [x] **Step 4: Run test to verify it passes**

Run: `dotnet build MUIndex.slnx -c Release && dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null`
Expected: build succeeds, all `LoginPromptGateTests` pass. `TelnetProbe.cs` will fail to build at this
point (it still references `BannerGate`) — that's expected and fixed in Task 3; for this task, verify
the new tests pass by temporarily commenting out the `BannerGate.IsAnsweredByReturn(...)` call site at
`TelnetProbe.cs:114` (do **not** commit that comment-out — Task 3 replaces the whole surrounding block
properly).

- [x] **Step 5: Commit**

```bash
git add src/MUI.Crawl/LoginPromptGate.cs tests/MUI.Crawl.Tests/LoginPromptGateTests.cs
git rm src/MUI.Crawl/BannerGate.cs tests/MUI.Crawl.Tests/BannerGateTests.cs
git commit -m "feat: LoginPromptGate answers colour/screen-reader prompts with a real keystroke"
```

---

## Task 2: `LoginPromptGate` — PressEnter and AgeGate categories

**Files:**
- Modify: `src/MUI.Crawl/LoginPromptGate.cs`
- Modify: `tests/MUI.Crawl.Tests/LoginPromptGateTests.cs`

**Interfaces:**
- Consumes: `LoginPromptGate.Classify`, `LoginPromptCategory`, `LoginPromptAnswer` from Task 1.
- Produces: same `Classify` signature, now also returning `PressEnter`/`AgeGate` answers.

These are the categories today's `BannerGate` never attempted at all — not because a blind Return
fails against them (it's usually the right answer), but because the old classifier didn't recognise
the shape, so the *real* screen that arrived after the blind Return was thrown away as flush residue
and the raw "Press Enter..."/age-gate text was stored as the connect screen instead. Recognising the
shape is the entire fix; see `docs/login-prompt-scan/pre_login_prompts_report.md` under "Other
pre-login gates".

- [x] **Step 1: Write the failing test**

```csharp
// Append to tests/MUI.Crawl.Tests/LoginPromptGateTests.cs, inside class LoginPromptGateTests

    [Test]
    [Arguments("Press Enter to log in...")]
    [Arguments("[Press Return to continue]")]
    [Arguments("Please wait while your computer's DNS name is being resolved. Press RETURN for more information.")]
    [Arguments("Press [Enter] to login...")]
    [Arguments("[엔터]를 누르십시요.")]
    [Arguments("엔터를 누르십시오.")]
    [Arguments("[Enter]를 누르세요.")]
    public async Task APressEnterGateIsAnsweredWithABlankReturn(string banner)
    {
        var answer = LoginPromptGate.Classify(banner);

        await Assert.That(answer).IsNotNull();
        await Assert.That(answer!.Category).IsEqualTo(LoginPromptCategory.PressEnter);
        await Assert.That(answer.Answer).IsEqualTo(string.Empty);
    }

    [Test]
    // menghui-xiyou and xianlv-qingyuan both ask this exact question — "Are you a primary/secondary
    // school student or younger? (yes/no)" — using the full-width Chinese question mark.
    [Arguments("您是否是中小学学生或年龄更小？(yes/no)")]
    [Arguments("Are you a minor?")]
    [Arguments("Are you under 18?")]
    public async Task AnAgeGateIsAnsweredNo(string banner)
    {
        var answer = LoginPromptGate.Classify(banner);

        await Assert.That(answer).IsNotNull();
        await Assert.That(answer!.Category).IsEqualTo(LoginPromptCategory.AgeGate);
        await Assert.That(answer.Answer).IsEqualTo("no");
    }

    [Test]
    // "Please enter a character name" must never trip the press-enter gate — it is an ordinary login
    // prompt that happens to contain the word "enter" as a verb, not an instruction to press a key.
    [Arguments("Please enter a character name >")]
    [Arguments("Enter your name: ")]
    public async Task AnOrdinaryEnterPromptIsNotAPressEnterGate(string banner)
    {
        await Assert.That(LoginPromptGate.Classify(banner)).IsNull();
    }
```

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null`
Expected: `APressEnterGateIsAnsweredWithABlankReturn` and `AnAgeGateIsAnsweredNo` FAIL (`Classify`
returns null); `AnOrdinaryEnterPromptIsNotAPressEnterGate` passes trivially.

- [x] **Step 3: Extend `Classify`**

```csharp
// In src/MUI.Crawl/LoginPromptGate.cs, replace the body of Classify with:

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

        return null;
    }

// Add beside the other [GeneratedRegex] members:

    /// <summary>
    /// A server telling us to press a key, in the shapes live servers ask it — English ("press" right
    /// before "enter"/"return", so "Please enter a name" never matches) and the Korean idiom (either
    /// the Hangul "엔터" or a literal Latin "Enter" followed within a few characters by "누르", "press").
    /// </summary>
    /// <remarks>
    /// Known gap, deliberately not covered: the "[엔터]를 입력하세요" phrasing (3-third-eye-harmony) uses
    /// "입력" ("input") rather than "누르" ("press") and is not matched — see the survey doc note this
    /// plan adds in the final task.
    /// </remarks>
    [GeneratedRegex(
        @"\bpress\s+(?:the\s+)?\[?(?:enter|return)\]?\b|\benter\b.{0,15}누르|엔터.{0,15}누르",
        RegexOptions.IgnoreCase)]
    private static partial Regex PressEnterPattern();

    /// <summary>
    /// An age check. Narrow by design — only the exact live phrasing measured
    /// (menghui-xiyou/xianlv-qingyuan's "are you a primary/secondary school student or younger?") plus
    /// the generic English shape a game might use for the same check. "No" is the crawler's honest
    /// answer to every phrasing surveyed; none asks the inverse ("are you an adult?").
    /// </remarks>
    [GeneratedRegex(
        @"学生.{0,10}年龄|年龄更小|are\s+you\s+(?:a\s+)?(?:minor|under\s+\d+|of\s+legal\s+age)",
        RegexOptions.IgnoreCase)]
    private static partial Regex AgeGatePattern();
```

- [x] **Step 4: Run test to verify it passes**

Run: `dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null`
Expected: PASS, all `LoginPromptGateTests`.

- [x] **Step 5: Commit**

```bash
git add src/MUI.Crawl/LoginPromptGate.cs tests/MUI.Crawl.Tests/LoginPromptGateTests.cs
git commit -m "feat: LoginPromptGate recognises press-enter gates and age checks"
```

---

## Task 3: Wire the bounded prompt-answering loop into `TelnetProbe.ProbeAsync`

**Files:**
- Modify: `src/MUI.Crawl/ProbeOptions.cs`
- Modify: `src/MUI.Crawl/TelnetProbe.cs:96-233` (Phase 1 through the `ProbeResult` construction)
- Modify: `tests/MUI.Crawl.Tests/ProbeSessionTests.cs` (extend `FakeGame` with a `Replies` map; new
  integration tests)

**Interfaces:**
- Consumes: `LoginPromptGate.Classify(string?) : LoginPromptAnswer?`, `LoginPromptCategory` (Tasks 1-2).
- Produces: `ProbeOptions.MaxPromptRounds : int`; `TelnetProbe`'s Phase 1 now sends
  category-specific answers instead of an unconditional blind flush. No public API changes beyond the
  new option — `ProbeResult`'s shape is unchanged by this task (Task 5 adds the `WhoMenu` wiring).

This is the task that makes Tasks 1-2 actually run against a socket. `FakeGame` today can only reply to
a blank line or the literal words `WHO`/`INFO`/`VERSION`; there is no way to prove the probe sent an
explicit `y` rather than a blank line without a fixture that replies differently to each. Add a
generic `Replies` map first.

- [x] **Step 1: Write the failing tests**

```csharp
// In tests/MUI.Crawl.Tests/ProbeSessionTests.cs, inside class FakeGame:
// add this init-only property beside BlankLineReply (around line 729):

        /// <summary>
        /// What a specific command gets back, keyed case-insensitively — the general-purpose sibling
        /// of <see cref="BlankLineReply"/>, for fixtures that need to prove the probe sent a specific
        /// answer (e.g. "y" to a colour question) rather than a blank line.
        /// </summary>
        public IReadOnlyDictionary<string, string> Replies { get; init; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

// In HandleAsync, replace the final `return true;` (after the VERSION block, around line 953) with:

            if (Replies.TryGetValue(command, out var reply))
            {
                await reply(reply);
            }

            return true;
        }
```

```csharp
// New test methods in ProbeSessionTests.cs, alongside AScreenBehindAColourQuestionIsTheConnectScreen:

    /// <summary>
    /// A server that requires the literal letter, not a blank default — a blind Return against this
    /// fixture would just see the same question echoed back, which is what production stored before
    /// this fix (see docs/login-prompt-scan/pre_login_prompts_report.md, cthulhumud/arcadia-mud-style
    /// strict colour gates).
    /// </summary>
    [Test]
    public async Task AColourGateThatRequiresAnExplicitLetterIsAnsweredCorrectly()
    {
        await using var game = new FakeGame
        {
            BannerTail = "Do you want ANSI color (Y/N)?",
            Replies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["y"] = "Welcome to Arcadia MUD\r\nBased on Merc 2.1\r\n",
            },
            WhoReply = "Illegal name, try again.\r\nName: \r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Banner).Contains("Welcome to Arcadia MUD");
        await Assert.That(game.Received).Contains("y");
    }

    /// <summary>
    /// A press-enter gate the old BannerGate never recognised — the real screen behind it was thrown
    /// away as flush residue and the raw "Press Enter..." line stored as the connect screen instead.
    /// </summary>
    [Test]
    public async Task APressEnterGateRevealsTheRealScreenBehindIt()
    {
        await using var game = new FakeGame
        {
            BannerTail = "Press Enter to log in...",
            BlankLineReply = "Rites of Passage\r\nA game of legend.\r\n",
            WhoReply = "Illegal name, try again.\r\nName: \r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Banner).Contains("Rites of Passage");
        await Assert.That(result.Banner).DoesNotContain("Illegal name");
    }

    /// <summary>
    /// Two gates in a row — colour, then a press-enter — both answered before the real screen is
    /// treated as settled. Proves the loop, not just one round of it.
    /// </summary>
    [Test]
    public async Task StackedGatesAreAnsweredInOrder()
    {
        await using var game = new FakeGame
        {
            BannerTail = "Do you want ANSI? (Y/n)",
            Replies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["y"] = "Ansi enabled!\r\nPress Enter to continue...",
            },
            BlankLineReply = "Welcome to New Haven\r\n",
            WhoReply = "Illegal name, try again.\r\nName: \r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Banner).Contains("Welcome to New Haven");
        await Assert.That(game.Received).Contains("y");
    }

    /// <summary>
    /// A misclassified/runaway gate must not be able to spin the probe past MaxPromptRounds.
    /// </summary>
    [Test]
    public async Task ARepeatingGateStopsAtTheRoundBound()
    {
        await using var game = new FakeGame
        {
            BannerTail = "Do you want ANSI? (Y/n)",
            // "y" always gets the same question back — an adversarial/broken server that never
            // actually accepts an answer. The loop must still terminate.
            Replies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["y"] = "Do you want ANSI? (Y/n)",
            },
        };

        var options = Fast() with { MaxPromptRounds = 2 };
        var result = await new TelnetProbe(options).ProbeAsync(game.Target);

        // Bounded: exactly MaxPromptRounds "y"s went out, not an unbounded stream of them.
        await Assert.That(game.Received.Count(line => line == "y")).IsEqualTo(2);
    }
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet build MUIndex.slnx -c Release && dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null`
Expected: `AColourGateThatRequiresAnExplicitLetterIsAnsweredCorrectly`,
`APressEnterGateRevealsTheRealScreenBehindIt`, `StackedGatesAreAnsweredInOrder` and
`ARepeatingGateStopsAtTheRoundBound` all FAIL — `TelnetProbe` still sends only a blind flush, and
`ProbeOptions.MaxPromptRounds` does not exist yet (build error on the `with` expression) until Step 3.

- [x] **Step 3: Add `ProbeOptions.MaxPromptRounds`**

```csharp
// In src/MUI.Crawl/ProbeOptions.cs, add after BannerPatience:

    /// <summary>
    /// How many pre-login prompts (colour, charset menu, press-enter, age-gate) one probe will answer
    /// in a row before treating whatever it has as the connect screen.
    /// </summary>
    /// <remarks>
    /// A misclassified screen must not be able to spin the probe through its whole <see
    /// cref="MaxPhase"/> answering itself — bounded well above anything measured (New Haven stacks a
    /// colour question and a press-enter gate; nothing surveyed stacks more than two), so an honest
    /// multi-step gate still resolves and a runaway false positive still stops quickly.
    /// </remarks>
    public int MaxPromptRounds { get; init; } = 4;
```

- [x] **Step 4: Rewrite `TelnetProbe.ProbeAsync`'s Phase 1**

```csharp
// In src/MUI.Crawl/TelnetProbe.cs, replace lines 96-132 (from the Phase 1 comment through
// `var asked = false;`) with:

            // Phase 1 — the connect screen. Banner and WHO answer are kept apart because they are
            // different evidence: one is a display asset and codebase fingerprint, the other is a
            // measurement.
            await SettleAsync(telnet, Arrived, 0, _options.SilenceGrace, budget.Token);

            // A screen that has told us it is not ready has not settled, it has paused. Waiting once
            // more is conditional on there being almost nothing there, so a server that has already
            // painted pays nothing — see ProbeOptions.BannerPatience for the server this is measured
            // against and for what recording the placeholder cost.
            if (LooksUnfinished(BannerSoFar(lines, 0, Arrived())))
            {
                await SettleAsync(telnet, Arrived, Arrived(), _options.BannerPatience, budget.Token);
            }

            // Some connect screens are not the screen at all, but one or more questions the server
            // asks before it paints one — colour, a press-enter gate, an age check. Each round
            // classifies whatever newly arrived since the last answer and sends the specific reply
            // LoginPromptGate says resolves it, then settles again; bounded by MaxPromptRounds so a
            // misread screen cannot spin the probe against itself. A blind Return sent unconditionally
            // here — the whole of what this loop replaces — was never enough: several real games only
            // accept an explicit letter, and a server that did not recognise a blind Return simply
            // re-printed the same question, which a probe run before this fix stored as the connect
            // screen. LoginPromptCategory.WhoMenu is excluded from this loop on purpose — see the block
            // below it, which handles that category once against the settled screen rather than as one
            // more round here.
            var roundStart = 0;
            for (var round = 0; round < _options.MaxPromptRounds; round++)
            {
                if (LoginPromptGate.Classify(BannerSoFar(lines, roundStart, Arrived()))
                        is not { Category: not LoginPromptCategory.WhoMenu } prompt
                    || !Live(client))
                {
                    break;
                }

                roundStart = Arrived();
                await telnet.SendAsync(Encoding.ASCII.GetBytes(prompt.Answer));
                await SettleAsync(telnet, Arrived, roundStart, _options.QuietPeriod, budget.Token);
            }

            var bannerLines = Arrived();

            // Phase 2 — an empty line, and everything it produces is thrown away.
            //
            // A server that does not implement telnet at its login screen does not recognise our own
            // IAC DO negotiation bytes as telnet: it takes them as typing and leaves them in its line
            // buffer, so the next thing we send is read as garbage-prefixed and not as WHO — the
            // count is lost. A bare terminator flushes that residue as its own line first. What comes
            // back is a reaction to bytes *we* sent, not the game's connect screen or its WHO answer,
            // so it must not be recorded as either (rule 5) — dropped deliberately, which is what the
            // gap between bannerLines and flushLines is.
            //
            // This also ends the session outright on every DIKU descendant, which reads an empty line
            // at its name prompt as a goodbye — see HungUp for what that costs.
            var flushLines = bannerLines;
            var whoLines = bannerLines;
            var infoLines = bannerLines;
            var versionLines = bannerLines;
            var asked = false;

// Then, still inside ProbeAsync, replace the single line (previously ~167) `await telnet.SendAsync([]);`
// through the `if (gated) { bannerLines = Arrived(); }` block (previously ~167-180) with just:

                await telnet.SendAsync([]);
                await SettleAsync(telnet, Arrived, bannerLines, _options.QuietPeriod, budget.Token);
                flushLines = whoLines = infoLines = versionLines = Arrived();
```

Also update `BannerSoFar` to take a range (it is currently a two-argument `(lines, count)` helper with
one call site; the new loop needs `(lines, from, to)`):

```csharp
// Replace the existing BannerSoFar method (previously lines 380-386) with:

    /// <summary>The connect screen as it stands part-way through the phase that is collecting it.</summary>
    /// <remarks>
    /// Read with the Latin-1 fallback, not the session's eventual encoding — there isn't one yet, and
    /// this text is never shown to anyone. Its readers (<see cref="LooksUnfinished"/>,
    /// <see cref="LoginPromptGate.Classify"/>) only check length, punctuation and a handful of
    /// vocabulary words, none of which an 8-bit byte changes the answer to. The real decoding decision
    /// happens once, at the end, in <see cref="WireEncoding.Read"/>.
    /// </remarks>
    private static string BannerSoFar(List<byte[]> lines, int from, int to)
    {
        lock (lines)
        {
            return string.Join(
                "\n", lines.Skip(from).Take(to - from).Select(WireEncoding.Fallback.GetString));
        }
    }
```

Finally, `BannerGate` is gone (Task 1), so remove the now-unused `gated` variable's declaration
entirely — it no longer exists after the Step 4 rewrite above, since the boundary is now decided by
where the round loop stopped rather than by a retroactive reinterpretation.

- [x] **Step 5: Run tests to verify they pass**

Run: `dotnet build MUIndex.slnx -c Release && dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null`
Expected: PASS, all of `ProbeSessionTests` including the four new tests and the pre-existing
`AScreenBehindAColourQuestionIsTheConnectScreen` (still passes: `BlankLineReply` is irrelevant to it
now, since the fixture's `Replies` is empty and `LoginPromptGate.Classify` matches the colour question
regardless of which reply mechanism the fixture wires up — the probe sends `"y"` and the fixture, in
that pre-existing test, has no `Replies["y"]` entry, so it falls through to `BlankLineReply` **only if
you kept it wired that way** — re-check this test's fixture: it currently sets `BlankLineReply`, not
`Replies["y"]`, so it will no longer receive a reply once the probe starts sending `"y"` instead of a
blank line. **Update the fixture** in this step from `BlankLineReply = "Ansi enabled!..."` to
`Replies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["y"] =
"Ansi enabled!\r\nWelcome to Adventures Unlimited\r\n..." }` (same reply text, keyed on `"y"` instead of
wired through `BlankLineReply`), matching how Task 1's answer for `Colour` is now an explicit letter.

- [x] **Step 6: Commit**

```bash
git add src/MUI.Crawl/ProbeOptions.cs src/MUI.Crawl/TelnetProbe.cs tests/MUI.Crawl.Tests/ProbeSessionTests.cs
git commit -m "feat: TelnetProbe answers pre-login prompts with a bounded, category-aware loop"
```

---

## Task 4: `LoginPromptGate` — Charset menu, UTF-8-only auto-select

**Files:**
- Modify: `src/MUI.Crawl/LoginPromptGate.cs`
- Modify: `tests/MUI.Crawl.Tests/LoginPromptGateTests.cs`
- Modify: `tests/MUI.Crawl.Tests/ProbeSessionTests.cs` (one integration test proving `WireEncoding`
  proves UTF-8 once the menu is answered)

**Interfaces:**
- Consumes: `LoginPromptGate.Classify`, the round loop from Task 3 (no `TelnetProbe.cs` changes needed
  — the loop already sends whatever `Classify` returns, for any category except `WhoMenu`).
- Produces: `Classify` now also returns `LoginPromptCategory.Charset` answers.

Roughly half the encoding-menu games surveyed (`docs/login-prompt-scan/pre_login_prompts_report.md`,
"Encoding / charset prompts") offer a numbered or lettered menu with a legible UTF-8 option —
`dreamland`, `hiervard`, `sphere-of-worlds` and others. The other half offer only GB/BIG5 or only
Cyrillic codepages with **no** UTF-8 option at all — picking one of those blind would be exactly the
guess-and-record-it-as-fact rule 5 forbids, so this task deliberately only ever picks an option whose
own label says UTF-8, and returns `null` (no answer, existing staff-override path unchanged) for every
other shape.

- [x] **Step 1: Write the failing test**

```csharp
// Append to tests/MUI.Crawl.Tests/LoginPromptGateTests.cs

    [Test]
    // dreamland's real menu (docs/login-prompt-scan chunk_5): seven options, the seventh legibly UTF-8.
    public async Task ANumberedCharsetMenuWithAUtf8OptionPicksIt()
    {
        var banner = "1. KOI8-U\n2. ALT (CP866)\n3. WIN (CP1251)\n4. ISO (ISO-8859-5)\n5. MAC\n"
            + "6. Translit\n7. UTF-8\nPlease select your Ukrainian or Russian codepage: ";

        var answer = LoginPromptGate.Classify(banner);

        await Assert.That(answer).IsNotNull();
        await Assert.That(answer!.Category).IsEqualTo(LoginPromptCategory.Charset);
        await Assert.That(answer.Answer).IsEqualTo("7");
    }

    [Test]
    // sowmud's real menu shape (docs/login-prompt-scan chunk_5) — bracketed tokens, no UTF-8 offered.
    public async Task ANumberedCharsetMenuWithNoUtf8OptionIsLeftAlone()
    {
        var banner = "0. Windows zMUD\n1. Windows JMC, Telnet\n"
            + "2. Windows JMC (old versions or #IAC send single)\n3. KOI-8R\nEnter Charset: ";

        await Assert.That(LoginPromptGate.Classify(banner)).IsNull();
    }

    [Test]
    // gulong-qunxia's real toggle shape — a single-line GB/BIG5 choice, not one option per line, and
    // no UTF-8 anywhere. Never guessed between the two.
    public async Task AGbBig5ToggleWithNoUtf8OptionIsLeftAlone()
    {
        var banner = "目前的字符集是简体，请输入GB/BIG5改变字符集，或直接登录用户。";

        await Assert.That(LoginPromptGate.Classify(banner)).IsNull();
    }
```

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null`
Expected: `ANumberedCharsetMenuWithAUtf8OptionPicksIt` FAILS (`Classify` returns null); the other two
pass trivially (already null).

- [x] **Step 3: Add the charset-menu matcher**

```csharp
// In src/MUI.Crawl/LoginPromptGate.cs, add a call inside Classify, after the LongestQuestion-bounded
// block and before the final `return null;`:

        if (ClassifyCharsetMenu(bannerSoFar ?? string.Empty) is { } charset)
        {
            return charset;
        }

        return null;
    }

// Add as a new private method, and the two new regexes, beside the existing ones:

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
        var lines = bannerSoFar
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(BannerText.Flatten)
            .Where(line => line.Length > 0);

        foreach (var line in lines)
        {
            var option = CharsetOptionLinePattern().Match(line);
            if (option.Success && Utf8LabelPattern().IsMatch(option.Groups["label"].Value))
            {
                return new LoginPromptAnswer(option.Groups["token"].Value, LoginPromptCategory.Charset);
            }
        }

        return null;
    }

    // "7. UTF-8" / "[7] UTF-8" / "7) UTF-8 encoding" — a short leading token introducing a label.
    [GeneratedRegex(@"^\[?(?<token>[0-9]{1,2}|[A-Za-z])[\]\).:]\s*[-:]?\s*(?<label>.+)$")]
    private static partial Regex CharsetOptionLinePattern();

    [GeneratedRegex(@"\bUTF-?8\b", RegexOptions.IgnoreCase)]
    private static partial Regex Utf8LabelPattern();
```

- [x] **Step 4: Run test to verify it passes**

Run: `dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null`
Expected: PASS, all three new tests plus every earlier `LoginPromptGateTests`.

- [x] **Step 5: Write the integration test proving `WireEncoding` proves UTF-8 after the menu is answered**

```csharp
// New test in tests/MUI.Crawl.Tests/ProbeSessionTests.cs

    /// <summary>
    /// Answering a charset menu's UTF-8 option is a request, not a fact — WireEncoding still
    /// independently proves the encoding from the bytes that actually arrive afterward (rule 5).
    /// </summary>
    [Test]
    public async Task AnsweringTheCharsetMenuLetsWireEncodingProveUtf8()
    {
        await using var game = new FakeGame
        {
            BannerTail = "1. KOI8-U\n2. ALT (CP866)\n3. WIN (CP1251)\n4. ISO (ISO-8859-5)\n5. MAC\n"
                + "6. Translit\n7. UTF-8\nPlease select your Ukrainian or Russian codepage: ",
            Replies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["7"] = "Ласкаво просимо до Dreamland\r\n",
            },
            WhoReply = "Illegal name, try again.\r\nName: \r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(game.Received).Contains("7");
        await Assert.That(result.Banner).Contains("Dreamland");
        await Assert.That(result.ReadAs).IsEqualTo("utf-8");
        await Assert.That(result.CharsetSource).IsEqualTo(WireCharset.Proven);
    }
```

- [x] **Step 6: Run test to verify it passes**

Run: `dotnet build MUIndex.slnx -c Release && dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null`
Expected: PASS.

- [x] **Step 7: Commit**

```bash
git add src/MUI.Crawl/LoginPromptGate.cs tests/MUI.Crawl.Tests/LoginPromptGateTests.cs tests/MUI.Crawl.Tests/ProbeSessionTests.cs
git commit -m "feat: LoginPromptGate picks a charset menu's UTF-8 option when one is offered"
```

---

## Task 5: `LoginPromptGate` — WhoMenu, and harvesting it in `TelnetProbe`

**Files:**
- Modify: `src/MUI.Crawl/LoginPromptGate.cs`
- Modify: `src/MUI.Crawl/TelnetProbe.cs`
- Modify: `tests/MUI.Crawl.Tests/LoginPromptGateTests.cs`
- Modify: `tests/MUI.Crawl.Tests/ProbeSessionTests.cs`

**Interfaces:**
- Consumes: `LoginPromptGate.Classify`; `WhoParser.Parse(string?) : WhoReading`; `PayloadRedaction.Replayable(string?) : string?` (all pre-existing).
- Produces: `Classify` now also returns `LoginPromptCategory.WhoMenu` answers; `ProbeResult.Who`/`WhoShape` may now come from a pre-login menu instead of the literal `WHO` command, transparently to every downstream consumer (`PresenceChoice.From` already ranks `Who.HasCount` first regardless of which route produced it).

A who's-online menu option is different in kind from every other category: selecting it does not
unlock a second screen hiding behind this one — for the real games measured (BatMUD, ZombieMUD,
discworld.starturtle.net), the menu **is** the permanent connect screen, printed once. So it is
classified once, against the screen already settled by Task 3's loop (never inside that loop — the
loop explicitly excludes `WhoMenu` via its `is not { Category: not LoginPromptCategory.WhoMenu }`
guard), its own answer and reply are kept out of `Banner` the same way the ordinary `WHO` phase already
is, and the reply is parsed through the exact same `WhoParser` the literal `WHO` command uses — so
downstream (`PresenceChoice`, presence rollups) sees no difference between the two routes.

- [x] **Step 1: Write the failing `LoginPromptGate` test**

```csharp
// Append to tests/MUI.Crawl.Tests/LoginPromptGateTests.cs

    [Test]
    // batmud's real menu line (docs/login-prompt-scan chunk_1).
    [Arguments("w - who is playing at the moment", "w")]
    // archipelago's real menu line.
    [Arguments("W - See who is online", "W")]
    // discworld/epitaph's real menu line.
    [Arguments("U - Short list of who is on-line", "U")]
    // way-of-the-force's real menu line.
    [Arguments("w - Who is online?", "w")]
    public async Task AWhosOnlineMenuOptionIsRecognised(string banner, string expectedToken)
    {
        var answer = LoginPromptGate.Classify(banner);

        await Assert.That(answer).IsNotNull();
        await Assert.That(answer!.Category).IsEqualTo(LoginPromptCategory.WhoMenu);
        await Assert.That(answer.Answer).IsEqualTo(expectedToken);
    }

    [Test]
    // The ordinary command a login screen might mention in passing must never trip this — WhoMenu is
    // only ever a lettered *menu option*, not any sentence containing the word "who".
    [Arguments("Type WHO to see who is connected before you log in.")]
    public async Task AWhoMentionThatIsNotAMenuOptionIsNotAWhoMenu(string banner)
    {
        var answer = LoginPromptGate.Classify(banner);

        await Assert.That(answer is not { Category: LoginPromptCategory.WhoMenu }).IsTrue();
    }
```

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null`
Expected: `AWhosOnlineMenuOptionIsRecognised` FAILS for all four arguments (`Classify` returns null);
`AWhoMentionThatIsNotAMenuOptionIsNotAWhoMenu` passes trivially.

- [x] **Step 3: Add the who-menu matcher**

```csharp
// In src/MUI.Crawl/LoginPromptGate.cs, add a call inside Classify, right after ClassifyCharsetMenu's:

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

// Add as a new private method, and its regex, beside ClassifyCharsetMenu:

    /// <summary>
    /// A menu option whose label is unmistakably "see who is online", found the same
    /// one-line-at-a-time way <see cref="ClassifyCharsetMenu"/> does.
    /// </summary>
    /// <remarks>
    /// Distinct from the ordinary <c>WHO</c> command this probe already sends for free at Phase 3
    /// (<see cref="TelnetProbe.WhoCommand"/>) — this is a *menu letter* a server prints on its
    /// permanent connect screen (BatMUD, ZombieMUD), not the command itself.
    /// </remarks>
    private static LoginPromptAnswer? ClassifyWhoMenu(string bannerSoFar)
    {
        var lines = bannerSoFar
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(BannerText.Flatten)
            .Where(line => line.Length > 0);

        foreach (var line in lines)
        {
            var option = WhoMenuOptionLinePattern().Match(line);
            if (option.Success)
            {
                return new LoginPromptAnswer(option.Groups["token"].Value, LoginPromptCategory.WhoMenu);
            }
        }

        return null;
    }

    // "w - who is playing at the moment" / "W - See who is online" / "U - Short list of who is
    // on-line" — a single-letter menu token, then a dash or bracket separator, then a label about who
    // is connected.
    [GeneratedRegex(
        @"^\[?(?<token>[A-Za-z])(?:[\]\).:]|\s+-\s*)\s*.{0,25}\bwho\b.{0,25}(?:online|playing|on[- ]?line)",
        RegexOptions.IgnoreCase)]
    private static partial Regex WhoMenuOptionLinePattern();
```

- [x] **Step 4: Run test to verify it passes**

Run: `dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null`
Expected: PASS, all `LoginPromptGateTests`.

- [x] **Step 5: Write the failing `TelnetProbe` integration test**

```csharp
// New tests in tests/MUI.Crawl.Tests/ProbeSessionTests.cs

    /// <summary>
    /// Selecting a who's-online menu option feeds the exact same WhoReading pipeline the literal WHO
    /// command does — PresenceChoice.From does not need to know which route produced it.
    /// </summary>
    [Test]
    public async Task AWhosOnlineMenuOptionIsHarvestedAsTheWhoReading()
    {
        await using var game = new FakeGame
        {
            BannerTail = "(C)onnect  (N)ew character  (W)ho is online  (Q)uit",
            Replies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["W"] = "There are 12 players connected.\r\n",
            },
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(game.Received).Contains("W");
        await Assert.That(result.Who.HasCount).IsTrue();
        await Assert.That(result.Who.Count).IsEqualTo(12);
        // The menu itself is this game's actual connect screen and stays in Banner; the roster
        // reply harvested from selecting "W" must not pollute it.
        await Assert.That(result.Banner).Contains("Who is online");
        await Assert.That(result.Banner).DoesNotContain("12 players connected");
    }

    /// <summary>
    /// Once the menu has already answered WHO, the later literal WHO phase must not run and
    /// overwrite a good reading with whatever a stray "WHO" typed at this screen produces.
    /// </summary>
    [Test]
    public async Task TheLiteralWhoPhaseIsSkippedOnceTheMenuAlreadyAnsweredIt()
    {
        await using var game = new FakeGame
        {
            BannerTail = "(C)onnect  (W)ho is online  (Q)uit",
            Replies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["W"] = "There are 12 players connected.\r\n",
                // If the probe wrongly also sent the literal word WHO at this menu, FakeGame's ordinary
                // WHO handler would reply with this and the count would be corrupted to 0.
                ["WHO"] = "That is not a valid choice.\r\n",
            },
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Who.Count).IsEqualTo(12);
        await Assert.That(game.Received).DoesNotContain("WHO");
    }
```

- [x] **Step 6: Run test to verify it fails**

Run: `dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null`
Expected: both new tests FAIL — nothing in `TelnetProbe` yet acts on `LoginPromptCategory.WhoMenu`.

- [x] **Step 7: Wire the one-shot who-menu harvest into `TelnetProbe.ProbeAsync`**

```csharp
// In src/MUI.Crawl/TelnetProbe.cs, immediately after `var bannerLines = Arrived();` (added in Task 3)
// and before the Phase 2 comment/`var flushLines = bannerLines;` block, insert:

            // A who's-online menu option is different from every category the loop above answers:
            // selecting it doesn't reveal a second screen behind this one — for every real game
            // measured (BatMUD, ZombieMUD, discworld.starturtle.net) the menu already settled into
            // bannerLines above *is* the game's permanent connect screen. So it is classified once here
            // rather than as one more round of that loop, and its own answer/reply are kept out of
            // Banner entirely, the same way the ordinary WHO phase below never becomes part of it —
            // parsed through the identical WhoParser a literal WHO answer would be, so
            // PresenceChoice.From (spec §5.2) cannot tell which route produced the reading.
            WhoReading? whoFromMenu = null;
            string? whoFromMenuShape = null;
            if (Live(client)
                && LoginPromptGate.Classify(BannerSoFar(lines, 0, bannerLines))
                    is { Category: LoginPromptCategory.WhoMenu } menu)
            {
                var menuBaseline = Arrived();
                await telnet.SendAsync(Encoding.ASCII.GetBytes(menu.Answer));
                await SettleAsync(telnet, Arrived, menuBaseline, _options.QuietPeriod, budget.Token);

                var menuReply = BannerSoFar(lines, menuBaseline, Arrived());
                whoFromMenu = new WhoParser().Parse(menuReply);
                whoFromMenuShape = PayloadRedaction.Replayable(menuReply);
            }

// Then, in the same method, change `var asked = false;` to:

            var asked = whoFromMenu is not null;

// Then, inside the `try` block, change the WHO phase guard from
// `if (Live(client)) { whoLines = infoLines = versionLines = await AskAsync(WhoCommand, flushLines, () => asked = true); }`
// to:

                // A who's-online menu already answered this probe's WHO question — asking the literal
                // word WHO at whatever this game's screen looks like now would either repeat the menu
                // or be read as a character name, corrupting a good reading with a worse one.
                if (Live(client) && whoFromMenu is null)
                {
                    whoLines = infoLines = versionLines =
                        await AskAsync(WhoCommand, flushLines, () => asked = true);
                }

// Finally, in the ProbeResult construction near the end of the method, change:
//   Who = asked ? new WhoParser().Parse(whoText) : WhoReading.NotAsked,
//   WhoShape = asked ? PayloadRedaction.Replayable(whoText) : null,
// to:

                Who = whoFromMenu ?? (asked ? new WhoParser().Parse(whoText) : WhoReading.NotAsked),
                WhoShape = whoFromMenu is not null
                    ? whoFromMenuShape
                    : (asked ? PayloadRedaction.Replayable(whoText) : null),
```

- [x] **Step 8: Run tests to verify they pass**

Run: `dotnet build MUIndex.slnx -c Release && dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null`
Expected: PASS, the full `MUI.Crawl.Tests` suite.

- [x] **Step 9: Run the full test matrix once, since this task touches the probe every other suite ultimately depends on**

Run each in turn (per CLAUDE.md — Catalog and Crawler want a real PostgreSQL; if
`MUI_REQUIRE_POSTGRES`/a running Postgres are not available in this environment, run at minimum
Crawl and Discovery, and say so explicitly rather than claiming the others passed):

```bash
dotnet run -c Release --no-build --project tests/MUI.Catalog.Tests </dev/null
dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null
dotnet run -c Release --no-build --project tests/MUI.Crawler.Tests </dev/null
dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null
dotnet run -c Release --no-build --project tests/MUI.Web.Tests </dev/null
```

Expected: PASS across all five, no regressions in `MUI.Crawler.Tests` (which exercises
`PresenceChoice`/`ProbeIngestor` against fixture `ProbeResult`s — this task changes nothing about
`ProbeResult`'s shape, only how `Who` gets populated, so those tests should be unaffected).

- [x] **Step 10: Commit**

```bash
git add src/MUI.Crawl/LoginPromptGate.cs src/MUI.Crawl/TelnetProbe.cs tests/MUI.Crawl.Tests/LoginPromptGateTests.cs tests/MUI.Crawl.Tests/ProbeSessionTests.cs
git commit -m "feat: harvest a who's-online menu option as this probe's WHO reading"
```

---

## Task 6: Document what this plan deliberately does and does not cover

**Files:**
- Modify: `docs/codebase-survey-2026-07-30.md`

**Interfaces:** None — documentation only.

CLAUDE.md: "Read `docs/codebase-survey-2026-07-30.md` before changing the probe or the parsers... nearly
every rule in `MUI.Crawl` traces to a row in it." This task adds the rows this plan's rules trace to,
and is explicit about what was deliberately left unhandled so a future reader doesn't mistake a narrow
v1 regex for full coverage of the 900-game survey.

- [x] **Step 1: Append a dated section**

```markdown
<!-- Append to docs/codebase-survey-2026-07-30.md -->

## 2026-08-20 — pre-login prompts (LoginPromptGate)

A survey of all 900 `connect_screen`/`banner` rows in production found ~95 games gating their real
connect screen behind a question or menu a blind Return does not reliably answer (full detail, quoted
per game, in the branch-local `docs/login-prompt-scan/pre_login_prompts_report.md` — not committed,
see the plan at `docs/superpowers/plans/2026-08-20-crawler-login-prompt-negotiation.md`). Measured
examples driving each `LoginPromptGate` category: **Colour** — `cthulhumud.com:8889`,
`mud.arcadia.net:4000` (both require the explicit letter `y`, not a blank default — the reason a blind
Return was never enough). **ScreenReader** — `mud.harshlands.net:5555` and `eotmud.com:4000` (the two
live phrasings map Y/N to opposite meanings, which is why this category still only ever sends blank).
**PressEnter** — `play.ropmud.com:4443`, `vormud.genesismuds.com:7777`, plus a cluster of Korean
`toox.co.kr`/`dolba.net` games using "엔터를 누르십시오" idioms. **AgeGate** — `202.103.21.247:8888`
and `112.124.8.59:6666`, both asking the identical Chinese question, suggesting a shared codebase or
operator. **Charset (UTF-8 only)** — `dreamland.rocks:9000`, `hiervard.ru:4000`. **WhoMenu** —
`batmud.bat.org:23`, `zombiemud.org:3000`, `discworld.starturtle.net:4242`.

**Deliberately not covered, and why:**
- Charset menus offering only non-UTF-8 encodings (roughly half the 29 surveyed, e.g.
  `mud.pkuxkx.net:8080`'s GBK/UTF8/BIG5 toggle with no per-line menu structure, and every
  Cyrillic-codepage-only menu) — picking between two non-UTF-8 encodings is a guess, which rule 5
  forbids recording as a fact; these stay on the existing staff `CHARSET` override.
- `LoginPromptGate.PressEnterPattern`'s Korean branch matches "누르" (press) but not "입력" (input) —
  `3-third-eye-harmony` (`toox.co.kr:6000`, "[엔터]를 입력하세요") is not recognised. Low volume (one
  game surveyed); add the alternation if a second example turns up.
- `WhoMenuOptionLinePattern` requires the option's own label to say "who"/"online"/"playing" —
  `theforestsedge.com:4000`'s "[3] View online characters" phrasing is not recognised. Also low volume.
- Multiple *identical-category* gates stacked in sequence (e.g. `havenrpg.net:3000`'s two separate
  colour questions back to back) are answered correctly by the round loop mechanically, but are not
  covered by an automated test — `FakeGame`'s `Replies` map is keyed by command text, so two different
  replies to the same "y" cannot both be expressed in one fixture. Task 3's `StackedGatesAreAnsweredInOrder`
  test instead chains two *different* categories (colour → press-enter), which exercises the same loop
  mechanism without needing that fixture capability.
```

- [x] **Step 2: Commit**

```bash
git add docs/codebase-survey-2026-07-30.md
git commit -m "docs: record the pre-login prompt survey and this plan's deliberate coverage gaps"
```

---

## Self-review

**Spec coverage:** Colour ✓ (Task 1), ScreenReader ✓ (Task 1), PressEnter ✓ (Task 2), AgeGate ✓ (Task
2), the bounded loop replacing the old single-shot gate ✓ (Task 3), Charset/UTF-8-only ✓ (Task 4),
WhoMenu harvesting into the existing `WhoReading`/`PresenceChoice` pipeline ✓ (Task 5), and the
project's own documentation convention for probe changes ✓ (Task 6). The three-phase split from the
original strategy conversation (unblock the real banner / durable charset fix / new WHO data source)
maps onto Tasks 1-3 / Task 4 / Task 5 respectively.

**Placeholder scan:** every step has runnable code, not a description of code; every regex is matched
against a real quoted fixture from the survey; every "why" traces to a specific measured game or an
existing project rule rather than a generic justification.

**Type consistency:** `LoginPromptGate.Classify(string?) : LoginPromptAnswer?` and
`LoginPromptAnswer(string Answer, LoginPromptCategory Category)` are introduced once in Task 1 and used
with identical names/shapes through Tasks 3-5. `ProbeOptions.MaxPromptRounds` (Task 3) is the only new
public surface outside `MUI.Crawl`'s existing types. `ProbeResult`'s own shape is untouched throughout
— this plan only changes *how* `Who`/`Banner` get populated, never their types.

## Execution notes

All six tasks executed inline, TDD throughout, one commit per task. Two deviations from the plan text,
both improvements found during execution:

- Task 1 kept the minimal `TelnetProbe.cs`/`MUI.Probe/Program.cs` rename-consequence edits in its own
  commit rather than leaving them as an uncommitted stopgap (the plan's Step 4 suggested not
  committing them) — there's no reason to leave the tree unbuildable at a task boundary, and Task 3's
  later rewrite superseded that code regardless.
- After Task 5, a user-requested dedicated player-count sweep (6 subagents, same 900-game corpus,
  focused specifically on player-count reveal mechanisms) found three real `WhoMenu` shapes the
  lettered-only pattern missed: numbered (not lettered) options, dot-leader separators, and reversed
  word order ("...online who can help"). Fixed in an unplanned follow-up commit between Task 5 and
  Task 6, folded into Task 6's documentation. The rest of that sweep's findings (WhoParser/BannerCount
  vocabulary gaps, and confirmation that prose "type WHO" hints need no fix) are documented, not
  implemented — see the survey doc entry.

Verification: all five test suites (Catalog/Crawl/Crawler/Discovery/Web, 2,676+ tests) pass with a real
Postgres via Podman. No regressions.
