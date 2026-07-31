using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// Reading a count out of a connect screen — the weakest source, and the only one that reaches
/// games with neither MSSP nor a pre-login <c>WHO</c>.
/// </summary>
public class BannerCountTests
{
    /// <summary>aardmud.org:4000, captured 2026-07-30. The count sits inside the ASCII art.</summary>
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
}
