using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// The one assumed codebase, and the guard that keeps it from reaching a game that is not one.
/// </summary>
/// <remarks>
/// Every input here is a real one, shortened: they are the games in the catalogue whose own text
/// says <c>MUCK</c>, and the single game whose text says it and means something else.
/// </remarks>
public class MuckNamingTests
{
    [Test]
    public async Task AConnectScreenThatNamesAMuckIsOne()
    {
        var banner = "\e[1;36mWelcome to Twisted MUCK!\e[0m";

        await Assert.That(MuckNaming.Assumed(banner)).IsEqualTo("MUCK");
    }

    [Test]
    public async Task TheWordIsReadAsTheTailOfANameAndOfAnAddress()
    {
        // How these games are actually written. A word boundary on the left would lose every one of
        // them, and the MUCK line is the family with the least else to go on.
        await Assert.That(MuckNaming.Assumed("FurryMuck is a Service Mark")).IsEqualTo("MUCK");
        await Assert.That(MuckNaming.Assumed("http://www.fluffmuck.org/")).IsEqualTo("MUCK");
        await Assert.That(MuckNaming.Assumed(null, "kitsumuck.com")).IsEqualTo("MUCK");
        await Assert.That(MuckNaming.Assumed("Staff: PokemonFusionMUCK@gmail.com")).IsEqualTo("MUCK");
        await Assert.That(MuckNaming.Assumed("Truly amazing is that.. *Tiny-Muck *2.2* fb5.60,*"))
            .IsEqualTo("MUCK");
        await Assert.That(MuckNaming.Assumed("_______MUCK______/\\/\\__/\\__")).IsEqualTo("MUCK");
    }

    [Test]
    public async Task ADigitMayFollowTheWordAndALetterMayNot()
    {
        // Measured on Pegasus, which prints its codebase with no space in it.
        await Assert.That(MuckNaming.Assumed("Currently running TinyMUCK2.3b2!")).IsEqualTo("MUCK");

        // A longer word is a different word.
        await Assert.That(MuckNaming.Assumed("A MUCKer wrote this")).IsNull();
        await Assert.That(MuckNaming.Assumed("mucking about in the swamp")).IsNull();
    }

    [Test]
    public async Task AnEnglishWordEndingInTheMarkerIsNotAGame()
    {
        await Assert.That(MuckNaming.Assumed("The goblins run amuck through the hall.")).IsNull();
    }

    [Test]
    public async Task AnotherFamilyNamedAnywhereWithdrawsTheAssumption()
    {
        // mud.stick.org:9000, listed as Stick in the Mud: a ROM/Merc/Diku derivative whose ASCII art
        // says "in da Muck" nine lines above the credit that says what it actually runs. The word is
        // there, the game is not a MUCK, and this is the only thing standing between the two.
        var banner = """
            "Don't get Stuck...  in da Muck"
                     STICK Version: 2.3 mud.stick.org 9000
            Original DikuMUD by Hans Staerfeldt, Katja Nyboe, Tom Madsen,
            Michael Seifert, and Sebastian Hammer. Based on MERC 2.1 code
            by Hatchet, Furey, and Kahn. Based on ROM II by Alander.
            """;

        await Assert.That(MuckNaming.Assumed(banner)).IsNull();
    }

    [Test]
    public async Task AContradictionInAnyTextWithdrawsIt()
    {
        // The reading is of everything the game said, not of whichever text happened to match first:
        // a name that says MUCK and a version reply that says PennMUSH is a game we have not
        // identified, and the order the two are passed in cannot decide that.
        await Assert.That(MuckNaming.Assumed("Ambush MUCK", "PennMUSH 1.8.8p1")).IsNull();
        await Assert.That(MuckNaming.Assumed("PennMUSH 1.8.8p1", "Ambush MUCK")).IsNull();
    }

    [Test]
    public async Task TheMuckLineItselfIsNotAContradiction()
    {
        await Assert.That(MuckNaming.Assumed("This tinymuck is an adventure land")).IsEqualTo("MUCK");
        await Assert.That(MuckNaming.Assumed("kitsumuck.com runs ProtoMUCK")).IsEqualTo("MUCK");

        // feathermuck.avians.net, which recounts its own history on the way in. Fuzzball is what a
        // MUCK runs, and reading it as a competing family lost the assumption on the most
        // MUCK-shaped connect screen there is.
        await Assert.That(MuckNaming.Assumed(
                "* Originally we ran the *Tiny-Muck *2.2* fb5.60,* now Fuzzball on UNIX box"))
            .IsEqualTo("MUCK");
    }

    [Test]
    public async Task TextWithoutTheWordSaysNothing()
    {
        await Assert.That(MuckNaming.Assumed("Welcome to Ginko. Type connect <name> <password>."))
            .IsNull();
        await Assert.That(MuckNaming.Assumed(null, null, "   ")).IsNull();
        await Assert.That(MuckNaming.Assumed()).IsNull();
    }
}
