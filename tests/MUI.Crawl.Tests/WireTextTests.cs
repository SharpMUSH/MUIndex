using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// The one byte that has to be removed before text is stored, and everything that must survive.
/// </summary>
/// <remarks>
/// Found live: a CircleMUD sending <c>CR NUL</c> line endings aborted its own ingestion with
/// PostgreSQL <c>22021</c>, half-created its game, and lost the <c>REFERRAL</c> list that crawl was
/// following. A hostile server can do the same on purpose with one byte, so this is not only a
/// compatibility fix.
/// </remarks>
public class WireTextTests
{
    [Test]
    public async Task ANulIsRemovedBecauseTelnetPutItThereAndPostgresCannotHoldIt()
    {
        await Assert.That(WireText.Clean("Welcome\r\0")).IsEqualTo("Welcome\r");
        await Assert.That(WireText.Clean("\0\0")).IsEqualTo(string.Empty);
        await Assert.That(WireText.Clean("a\0b\0c")).IsEqualTo("abc");
    }

    /// <summary>
    /// A connect screen is rendered as the game sent it, so its colour and layout are content.
    /// </summary>
    [Test]
    public async Task EverythingElseSurvives()
    {
        const string banner = "[1;32mWelcome[0m\r\n\tto the game\n";

        await Assert.That(WireText.Clean(banner)).IsEqualTo(banner);
    }

    /// <summary>Text with nothing to clean is not copied — this runs on every line of every probe.</summary>
    [Test]
    public async Task CleanTextIsReturnedUntouched()
    {
        const string line = "By what name do you wish to be known?";

        await Assert.That(ReferenceEquals(WireText.Clean(line), line)).IsTrue();
    }
}
