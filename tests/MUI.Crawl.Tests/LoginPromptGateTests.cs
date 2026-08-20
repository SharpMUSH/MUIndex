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
