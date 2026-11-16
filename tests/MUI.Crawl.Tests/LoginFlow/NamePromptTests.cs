using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// A login prompt that took what we typed for a character name, in the words games actually use.
/// </summary>
public class NamePromptTests
{
    /// <summary>
    /// The ES2/MudOS family's answers, measured at <c>fs.twkang.net:5555</c> (issue #182), and their
    /// simplified-script spellings.
    /// </summary>
    [Test]
    [Arguments("好吧﹐那麼請重新輸入您的英文名字﹕")]
    [Arguments("使用 version 這個名字將會創造一個新的人物﹐您確定嗎(y/n)﹖")]
    [Arguments("好吧，那么请重新输入您的英文名字：")]
    [Arguments("使用 version 这个名字将会创造一个新的人物，您确定吗(y/n)？")]
    public async Task TheChineseFamilysPromptsAreRead(string line)
    {
        await Assert.That(NamePrompt.IsIn(line)).IsTrue();
    }

    [Test]
    [Arguments("No character by that name found.")]
    [Arguments("By what name do you wish to be known? Please enter your name:")]
    [Arguments("Name: ")]
    [Arguments("Password:")]
    [Arguments("Did I get that right, Info (Y/N)?")]
    public async Task TheEnglishPromptsStillAre(string line)
    {
        await Assert.That(NamePrompt.IsIn(line)).IsTrue();
    }

    /// <summary>
    /// What a game says when it did not understand us is not a name prompt, and neither is anything a
    /// real answer contains.
    /// </summary>
    [Test]
    [Arguments("Huh?  (Type \"help\" for help.)")]
    [Arguments("Name: ElendorMUSH")]
    [Arguments("目前共有 0 位巫師、83 位玩家在線上，以及 1 位使用者嘗試連線中。")]
    [Arguments("PennMUSH version 1.8.8 patchlevel 0 [2020-01-01]")]
    public async Task OtherRepliesAreNot(string line)
    {
        await Assert.That(NamePrompt.IsIn(line)).IsFalse();
    }

    /// <summary>
    /// <c>WHO</c> at the same family is now read as what it is — the login prompt — rather than as a
    /// dialect our parser could not read, which would be our defect written down about their game.
    /// </summary>
    [Test]
    public async Task AWhoTakenForANameInChineseIsALoginPrompt()
    {
        var reading = new WhoParser().Parse("好吧﹐那麼請重新輸入您的英文名字﹕");

        await Assert.That(reading.Confidence).IsEqualTo(WhoConfidence.LoginPrompt);
    }
}
