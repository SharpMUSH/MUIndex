using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// Reading a count out of a connect screen — the weakest source, and the only one that reaches
/// games with neither MSSP nor a pre-login <c>WHO</c>.
/// </summary>
public class BannerCountTests
{
    /// <summary>A real connect screen where the count sits inside the ASCII art.</summary>
    private const string AardwolfBanner = """""
        #############################################################################
        ##[                                               ]##########################
        ##[        --- Welcome to Aardwolf MUD ---        ]############ /"  #########
        ##[                                               ]########  _-`"""', #######
        ##[         Players Currently Online: 218         ]#####  _-"       )  ######
        ##[                                               ]### _-"          |  ######
        -----------------------------------------------------------------------------
            Enter your character name or type 'NEW' to create a new character
        """"";

    [Test]
    public async Task TheCountIsFoundInsideTheArt()
    {
        // Aardwolf publishes no MSSP and answers no pre-login WHO, so this is the only route to a
        // number for one of the largest games in the hobby. An MSSP-only crawler records nothing.
        await Assert.That(BannerCount.Find(AardwolfBanner)).IsEqualTo(218);
    }

    [Test]
    [Arguments("Players online: 42", 42)]
    [Arguments("Currently connected: 7", 7)]
    [Arguments("Users Online - 15", 15)]
    [Arguments("Players currently online: 0", 0)]
    public async Task ALabelledCountIsRead(string line, int expected)
    {
        await Assert.That(BannerCount.Find(line)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("Welcome to Dragonspire, established 1997.")]
    [Arguments("##[   1024   ]##")]
    [Arguments("Enter your character name or type 'NEW'.")]
    [Arguments("Uptime: 3721")]
    [Arguments("")]
    [Arguments(null)]
    public async Task ABareNumberIsNeverACount(string? banner)
    {
        // The label is mandatory. Connect screens are full of numbers that are years, room counts,
        // uptimes and box-drawing coordinates, and this source is weak enough without guessing.
        await Assert.That(BannerCount.Find(banner)).IsNull();
    }

    [Test]
    public async Task TwoDisagreeingCountsRefuseRatherThanPick()
    {
        // A screen boasting both a live count and a record high gives no way to tell which is which.
        // Choosing would be guessing, and guessing is what this whole layer is trying not to do.
        var banner = "Players online: 42\nMost players ever connected: 900";

        await Assert.That(BannerCount.Find(banner)).IsNull();
    }

    [Test]
    public async Task TheSameCountStatedTwiceIsStillOneAnswer()
    {
        var banner = "Players online: 42\n... Players Currently Online: 42 ...";

        await Assert.That(BannerCount.Find(banner)).IsEqualTo(42);
    }

    [Test]
    public async Task AnImplausibleNumberIsRefused()
    {
        // The largest MU* peaks are in the low thousands. Five figures beside the word "online" is
        // a year, a room count, or a total-ever figure — not people logged in right now.
        await Assert.That(BannerCount.Find("Players online: 45000")).IsNull();
        await Assert.That(BannerCount.Find($"Players online: {BannerCount.Implausible + 1}")).IsNull();
    }

    [Test]
    public async Task AnsiColourDoesNotHideTheCount()
    {
        var coloured = "\u001b[1;33mPlayers Currently Online:\u001b[0m \u001b[32m218\u001b[0m";

        await Assert.That(BannerCount.Find(coloured)).IsEqualTo(218);
    }

    /// <summary>
    /// The second way a screen states a count: a sentence rather than a label.
    /// </summary>
    /// <remarks>
    /// Both fixtures are live connect screens. This reader now goes through
    /// <see cref="WhoParser.TryStatedCount"/>, the same one the <c>WHO</c> reply uses, rather than
    /// keeping its own narrower idea of what a stated count looks like.
    /// </remarks>
    [Test]
    [Arguments("It is 8:00 pm in Mountain View.\nThere are 122 local users.", 122)]
    [Arguments("Welcome to Erion MUD!\nThere are 41 players and 3 immortals online.", 41)]
    [Arguments("There are no players connected.", 0)]
    public async Task ACountStatedAsASentenceIsReadToo(string banner, int expected)
    {
        await Assert.That(BannerCount.Find(banner)).IsEqualTo(expected);
    }

    [Test]
    public async Task TheCeilingRuleArrivesWithTheSharedReader()
    {
        // retromud prints "11 out of 200 users playing" — eleven people and a licence for two
        // hundred. That rule lives in the WHO parser and this reader gets it by using it rather than
        // by growing a second copy that would have to learn it again.
        await Assert.That(BannerCount.Find("There are currently 11 out of 200 users playing."))
            .IsEqualTo(11);
    }

    [Test]
    public async Task ANameLengthRuleIsNotACount()
    {
        // akanbar's login screen, and the false positive the sentence reader is anchored against: a
        // number beside a People noun is not a population unless something says it is.
        await Assert.That(BannerCount.Find("Your name must be between 6 and 12 characters long."))
            .IsNull();
        await Assert.That(BannerCount.Find("There are 20 new players registered today.")).IsNull();
    }

    [Test]
    public async Task TwoDifferentFiguresAreStillRefused()
    {
        // The rule that keeps this the weakest source honest, now that a screen can state a count
        // two ways: when they disagree we cannot tell which is the players online, and picking is
        // guessing.
        await Assert.That(BannerCount.Find("Players online: 5\nThere are 60 users connected."))
            .IsNull();
    }
}
