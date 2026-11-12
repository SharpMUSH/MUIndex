using System.Text;

namespace MUI.I3.Tests;

public class SidecarTextTests
{
    private static string AsTheGatewayFellBackToIt(string text) =>
        Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(text));

    [Test]
    public async Task AUtf8NameTheGatewayReadAsLatin1IsRecovered()
    {
        const string name = "\u001B[1;32m時\u001B[0m\u001B[0;32m空幻境\u001B[0m";

        await Assert.That(SidecarText.Recover(AsTheGatewayFellBackToIt(name))).IsEqualTo(name);
        await Assert.That(SidecarText.Recover(AsTheGatewayFellBackToIt("Café Noir"))).IsEqualTo("Café Noir");
    }

    [Test]
    public async Task TextThatWasNeverUtf8IsLeftAsItArrived()
    {
        // A Latin-1 name the gateway read correctly: its bytes are not well-formed UTF-8.
        await Assert.That(SidecarText.Recover("Café Noir")).IsEqualTo("Café Noir");

        // Big5 時空幻境, which the gateway's fallback also yields; only CHARSET can say what it is.
        var big5 = Encoding.Latin1.GetString([0xAE, 0xC9, 0xAA, 0xC5, 0xA4, 0xDB, 0xB9, 0xD2]);
        await Assert.That(SidecarText.Recover(big5)).IsEqualTo(big5);

        // Decoded properly in the first place, so there is nothing to undo.
        await Assert.That(SidecarText.Recover("時空幻境")).IsEqualTo("時空幻境");
        await Assert.That(SidecarText.Recover("Nightfall")).IsEqualTo("Nightfall");
    }
}
