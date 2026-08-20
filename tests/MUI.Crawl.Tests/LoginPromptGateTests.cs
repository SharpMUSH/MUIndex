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

    [Test]
    // batmud's real menu line (docs/login-prompt-scan chunk_1).
    [Arguments("w - who is playing at the moment", "w")]
    // archipelago's real menu line.
    [Arguments("W - See who is online", "W")]
    // discworld/epitaph's real menu line.
    [Arguments("U - Short list of who is on-line", "U")]
    // way-of-the-force's real menu line.
    [Arguments("w - Who is online?", "w")]
    // eternitymud-com's real menu line — a numbered, not lettered, option.
    [Arguments("2. who is playing", "2")]
    // tauros-rebirth's real menu line — numbered with a dot-leader separator ("[2]....").
    [Arguments("[2]....See who is currently logged in.", "2")]
    // legendmud's real menu line — "online" precedes "who" rather than following it.
    [Arguments("[4] List immortals online who can help", "4")]
    public async Task AWhosOnlineMenuOptionIsRecognised(string banner, string expectedToken)
    {
        var answer = LoginPromptGate.Classify(banner);

        await Assert.That(answer).IsNotNull();
        await Assert.That(answer!.Category).IsEqualTo(LoginPromptCategory.WhoMenu);
        await Assert.That(answer.Answer).IsEqualTo(expectedToken);
    }

    /// <summary>
    /// Several real menus pack every option onto one line, and the who's-online option is rarely the
    /// first of them. Measured live: sending BatMUD the token of the option *before* the one we meant
    /// put "2" (visit the game) on the wire instead of "w".
    /// </summary>
    [Test]
    // batmud.bat.org:23's real menu line, both options as the server prints them.
    [Arguments("  2 - visit the game                    w - who is playing at the moment", "w")]
    // zombiemud.org:3000's real menu line.
    [Arguments("\t    [C]reate a new character     [W]ho is playing", "W")]
    // The who's-online option last of three, so a greedy reader has two chances to take the wrong one.
    [Arguments("(N)ew character  (V)isit the game  (W)ho is online", "W")]
    public async Task TheTokenTakenIsTheOneWhoseOwnLabelSaysWho(string banner, string expectedToken)
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
}
