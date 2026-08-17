using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// The licence notice a connect screen carries, read as the codebase it credits.
/// </summary>
/// <remarks>
/// <b>Every positive fixture here is a verbatim connect screen from the live catalogue</b>, taken on
/// 2026-08-16 from games that had no codebase on record. Inventing them would have produced a tidy
/// "Powered by SMAUG 1.4" and missed the shape that actually dominates: three licences stacked in
/// three consecutive sentences, oldest last.
/// </remarks>
public class CodebaseCreditsTests
{
    /// <summary>badtrip.genesismuds.com:4000. Diku, Merc and ROM in one breath.</summary>
    private const string BadTripBanner = """
        Welcome to BadTrip!  Ride the llama, mofos.
        Based on DikuMUD by Hans Staerfeldt, Katja Nyboe, Tom Madsen, Michael Seifert,
        and Sebastian Hammer, on MERC 2.1 code by Hatchet, Fuery, and Kahn,
        and Rom 2.4 copyright (c) 1993-1996 Russ Taylor.
        By what name do you wish to be known?
        """;

    [Test]
    public async Task TheOutermostLayerOfAStackOfLicencesIsTheAnswer()
    {
        // The credits are a derivation graph written outwards: ROM sits on Merc, Merc sits on Diku,
        // and each layer credits the one below because its licence requires it. Reading DikuMUD off
        // this screen would be true and useless — it is what BadTrip is descended from, not what it
        // runs.
        await Assert.That(CodebaseCredits.Named(BadTripBanner)).IsEqualTo("ROM");
    }

    [Test]
    [Arguments(
        "Based on CircleMUD 3.0bpl10,\nA derivative of DikuMUD (GAMMA 0.0)",
        "CircleMUD")]
    [Arguments(
        "Welcome to AlexMUD V\nBased upon DikuMUD I (GAMMA 0.0) created by\n"
        + "Hans Henrik Staerfeldt, Katja Nyboe,\nTom Madsen, Michael Seifert, and Sebastian Hammer",
        "DikuMUD")]
    [Arguments(
        "A Circle Mud 3.0 variant!\nCreated by Jeremy Elson\nBased on Diku Gamma 0.0 By:",
        "CircleMUD")]
    [Arguments(
        "Asgard's Honor is running the MudOSa4DGD v1.1 mudlib on DGD v1.2p2",
        "DGD")]
    [Arguments(
        "Rapture Runtime Environment v2.4.9.1 -- (c) 2026 -- Iron Realms Entertainment",
        "Rapture")]
    public async Task ARealScreenIsReadAsTheFamilyItCredits(string banner, string expected)
    {
        await Assert.That(CodebaseCredits.Named(banner)).IsEqualTo(expected);
    }

    [Test]
    public async Task TheVersionIsNeverLiftedOutOfTheProse()
    {
        // addictmud says "Based on CircleMUD 3.0bpl10" and this reads CircleMUD and stops. PR #51 is
        // why: a value parsed out of free text is the one the page shows and nobody thinks to doubt,
        // and a version picked out of a sentence is a guess wearing a decimal point.
        var read = CodebaseCredits.Named("Based on CircleMUD 3.0bpl10, a derivative of DikuMUD.");

        await Assert.That(read).IsEqualTo("CircleMUD");
        await Assert.That(read).DoesNotContain("3.0");
    }

    /// <summary>
    /// The <c>RetroMUX</c> bug, at the reader that would have reintroduced it.
    /// </summary>
    /// <remarks>
    /// darcness.net:4201 once had <c>ROM MUX 2.12.0.10</c> published against it because a substring
    /// search found <c>rom</c> inside <c>RetroMUX</c>. <c>rom</c> is not a marker in this file at any
    /// tier — three letters that occur inside ordinary words — and its family is reachable through
    /// Russ Taylor's name instead, which appears on a MU* connect screen for exactly one reason.
    /// </remarks>
    [Test]
    [Arguments("Welcome to RetroMUX, running MUX 2.12.0.10")]
    [Arguments("The chrome gates of Rome stand open before you.")]
    [Arguments("Created by Merc the Wanderer, a smooth operator.")]
    public async Task AWordThatMerelyContainsAFamilyNameIsNotACredit(string banner)
    {
        await Assert.That(CodebaseCredits.Named(banner)).IsNull();
    }

    [Test]
    public async Task AnEngineNameWithNothingCreditingItIsNotACredit()
    {
        // The second tier's whole rule. "SMAUG" could be a realm, a dragon or a player; the word
        // becomes evidence only on a sentence that is doing attribution, and a title bar is not.
        await Assert.That(CodebaseCredits.Named("Welcome to the Halls of Smaug!")).IsNull();
        await Assert.That(CodebaseCredits.Named("The MudOS Tavern is on your left."))
            .IsNull();
    }

    [Test]
    public async Task AttributionDoesNotCarryAcrossASentenceBoundary()
    {
        // Two unrelated claims on one screen must not lend each other credibility: the first
        // sentence is doing attribution and the second merely contains a word.
        await Assert.That(CodebaseCredits.Named("Powered by dreams. Visit the MudOS Tavern."))
            .IsNull();
    }

    [Test]
    public async Task AnAuthorIsCreditEnoughOnItsOwn()
    {
        // The first tier. avendar.net's screen names the Diku authors with no "based on" anywhere
        // near them, and there is no other reason for those five names to be on a MU* login screen.
        await Assert.That(CodebaseCredits.Named(
                "Original DikuMUD by Hans Staerfeldt, Katja Nyboe,\nTom Madsen, Michael Seifert"))
            .IsEqualTo("DikuMUD");
    }

    [Test]
    public async Task NothingIsReadOutOfAnEmptyOrSilentScreen()
    {
        await Assert.That(CodebaseCredits.Named(null)).IsNull();
        await Assert.That(CodebaseCredits.Named("")).IsNull();
        await Assert.That(CodebaseCredits.Named("   \n  \n ")).IsNull();
        await Assert.That(CodebaseCredits.Named("Do you want ANSI? (Y/n)")).IsNull();
    }
}
