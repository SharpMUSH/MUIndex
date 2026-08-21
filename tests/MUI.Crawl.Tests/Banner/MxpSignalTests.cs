using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// Reading MXP off what a server sends, rather than off a negotiation it never performed.
/// </summary>
public class MxpSignalTests
{
    [Test]
    public async Task ALineModeSequenceIsMxp()
    {
        await Assert.That(MxpSignal.IsPresent("\e[1z<VERSION>\e[6z")).IsTrue();
    }

    [Test]
    [Arguments("\e[0z")]
    [Arguments("\e[6z")]
    [Arguments("\e[7z")]
    [Arguments("Welcome!\r\n\e[1zSome secure line\r\n")]
    public async Task EveryLineModeTagCounts(string text)
    {
        await Assert.That(MxpSignal.IsPresent(text)).IsTrue();
    }

    /// <summary>
    /// The <c>z</c> final byte is private to MXP, so ordinary colour must not be mistaken for it.
    /// </summary>
    [Test]
    [Arguments("\e[1;32mGreen text\e[0m")]
    [Arguments("\e[2J\e[H")]
    [Arguments("Plain text with no escapes at all")]
    [Arguments("")]
    public async Task OrdinaryAnsiIsNotMxp(string text)
    {
        await Assert.That(MxpSignal.IsPresent(text)).IsFalse();
    }

    [Test]
    [Arguments("<SUPPORT>")]
    [Arguments("<!ELEMENT RName '<SEND>'>")]
    [Arguments("<send href='look'>look</send>")]
    public async Task ServerSentTagsCount(string text)
    {
        await Assert.That(MxpSignal.IsPresent(text)).IsTrue();
    }

    /// <summary>
    /// Tags a game might legitimately print in prose are not evidence. The list is short on purpose:
    /// a false positive here writes a capability into somebody's public record.
    /// </summary>
    [Test]
    [Arguments("Type <b>help</b> for a list of commands.")]
    [Arguments("Use the <font> tag in your description.")]
    [Arguments("Enter a name between <3 and 12> characters.")]
    public async Task ProseThatMerelyContainsAngleBracketsIsNot(string text)
    {
        await Assert.That(MxpSignal.IsPresent(text)).IsFalse();
    }

    /// <summary>
    /// A connect screen that is only a version request must flatten to nothing, or two unrelated
    /// servers answering the same way share a banner hash — this caused a real duplicate.
    /// </summary>
    [Test]
    public async Task AVersionRequestIsNotABanner()
    {
        await Assert.That(BannerText.Flatten("\e[1z<VERSION>\e[6z")).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task RealBannerTextSurvivesTheStrip()
    {
        var flattened = BannerText.Flatten("\e[1z<VERSION>\e[6z\e[1;32mWelcome to Tirradyn!\e[0m");

        await Assert.That(flattened).IsEqualTo("Welcome to Tirradyn!");
    }
}
