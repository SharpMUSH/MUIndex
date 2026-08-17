using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// Telling a connect screen from the one question a server asks before it paints one.
/// </summary>
/// <remarks>
/// Every fixture is a stored connect screen from the live catalogue on 2026-08-16 — which is to say
/// every one of them is a screen this crawler had already recorded as a game's connect screen, and
/// most of them are under thirty characters long.
/// </remarks>
public class BannerGateTests
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
    [Arguments("Screen reader user? Yes or No")]
    [Arguments("View End of Time in full (Y) or screen reader (N) mode?")]
    public async Task AScreenThatIsOnlyAQuestionIsAnsweredByTheFlush(string banner)
    {
        await Assert.That(BannerGate.IsAnsweredByReturn(banner)).IsTrue();
    }

    /// <summary>
    /// The look-alike that is the opposite fact, and the reason this file is a whitelist.
    /// </summary>
    /// <remarks>
    /// A server saying "please wait" is telling us it is <em>not</em> waiting for us: it will paint
    /// on its own, and <c>ProbeOptions.BannerPatience</c> already waits for it. Reading one of these
    /// as a gate would extend the banner across the flush on a server that never asked anything,
    /// which is where a stray Return produces a goodbye on a DIKU and a repainted screen on a
    /// TinyMUSH.
    /// </remarks>
    [Test]
    [Arguments("Detecting client, please wait...")]
    [Arguments("Attempting to detect client, please wait...")]
    [Arguments("Identifying client, please wait...")]
    [Arguments("Please Wait, while we attempt to detect your client...")]
    [Arguments("Identificazione del client in corso...")]
    [Arguments("Welcome to Medieval Times MUD. If you are using a screen reader\nplease type yes, else, enter no.")]
    public async Task AServerThatIsNotWaitingForUsIsNotAGate(string banner)
    {
        await Assert.That(BannerGate.IsAnsweredByReturn(banner)).IsFalse();
    }

    [Test]
    public async Task AScreenThatHasAlreadyPaintedIsNeverAGateHoweverItEnds()
    {
        // The load-bearing negative. A game that painted a screen and *then* asked about colour has
        // already told us everything the gate exists to recover, and extending its banner across the
        // flush would sweep in whatever it says to a stray Return. Length is the cheap half of the
        // test and this is what it buys.
        var painted = new string('=', 300) + "\nDo you want ANSI colour? (Y/n)";

        await Assert.That(BannerGate.IsAnsweredByReturn(painted)).IsFalse();
    }

    [Test]
    [Arguments("Welcome to Nowhere MUD.")]
    [Arguments("By what name do you wish to be known?")]
    [Arguments("Please enter a character name >")]
    [Arguments("What is your name: ")]
    public async Task AnOrdinaryPromptIsNotAGate(string banner)
    {
        // A question mark is not enough on its own: "By what name do you wish to be known?" is a
        // server that has finished painting and is asking for a login, and answering it with a bare
        // Return produces a refusal rather than a screen.
        await Assert.That(BannerGate.IsAnsweredByReturn(banner)).IsFalse();
    }

    [Test]
    public async Task NothingIsAGate()
    {
        await Assert.That(BannerGate.IsAnsweredByReturn(null)).IsFalse();
        await Assert.That(BannerGate.IsAnsweredByReturn("")).IsFalse();
        await Assert.That(BannerGate.IsAnsweredByReturn("   \n\n  ")).IsFalse();
    }

    [Test]
    public async Task ColourPaintedPastTheQuestionMarkStillReadsAsOne()
    {
        // legends-of-krynn writes "Do you see COLOR?" with the question mark in the middle of the
        // escape sequences, so the flattened text ends on the coloured word rather than on the
        // punctuation. The question mark is looked for anywhere for exactly this.
        var krynn = "\e[1;37mDo you see\e[0m? \e[1;31mC\e[1;32mO\e[1;33mL\e[1;34mO\e[1;35mR\e[1;37m ";

        await Assert.That(BannerGate.IsAnsweredByReturn(krynn)).IsTrue();
    }
}
