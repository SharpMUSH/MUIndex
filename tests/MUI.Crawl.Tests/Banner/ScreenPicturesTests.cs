using System.IO.Compression;
using System.Text;
using MarkupString.Ansi;
using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// The pictures a connect screen draws over its own cells, and the screen with them taken out.
/// </summary>
/// <remarks>
/// The inline sequences are written here by hand, the way a server writes them; the MXP tag is the one
/// <c>MarkupString.Mxp</c> writes for a figure to a client that answered <c>+image</c>, which is what
/// SharpMUSH sends.
/// </remarks>
public class ScreenPicturesTests
{
    private const string Esc = "\u001b";

    /// <summary>A real PNG, four pixels by two, built rather than pasted so its checksums are right.</summary>
    private static byte[] Png(int width = 4, int height = 2)
    {
        static byte[] Chunk(string type, byte[] data)
        {
            var body = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
            var length = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
            var crc = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(body));
            return [.. length, .. body, .. crc];
        }

        var header = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 6;

        var raw = new List<byte>();
        for (var y = 0; y < height; y++)
        {
            raw.Add(0);
            for (var x = 0; x < width; x++)
            {
                raw.AddRange([0xff, 0x00, 0x00, 0xff]);
            }
        }

        using var packed = new MemoryStream();
        using (var z = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write([.. raw]);
        }

        return [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, .. Chunk("IHDR", header), .. Chunk("IDAT", packed.ToArray()), .. Chunk("IEND", [])];
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xffffffffu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    private static string Plain(string text) => AnsiEscapeParser.Parse(text).ToPlainText();

    [Test]
    public async Task AScreenOfTextIsReturnedExactlyAsItCame()
    {
        var banner = $"{Esc}[1;31mWelcome{Esc}[0m to <Tidewater>\nconnect <name> <password>";

        var read = ScreenPictures.Read(banner, mxp: true);

        await Assert.That(read.Banner).IsEqualTo(banner);
        await Assert.That(read.Pictures).IsEmpty();
    }

    [Test]
    public async Task ASixelIsDecodedAndItsCellsLeftBlank()
    {
        // Two sixel bands of red, sixteen pixels wide: 16x12 pixels, two cells by one at 8x16.
        var banner = $"Ravenmoor\n{Esc}Pq#0;2;100;0;0#0~~~~~~~~~~~~~~~~-~~~~~~~~~~~~~~~~{Esc}\\\nconnect <name>";

        var read = ScreenPictures.Read(banner, mxp: false);

        var picture = await Assert.That(read.Pictures).HasSingleItem();
        await Assert.That(picture!.MediaType).IsEqualTo("image/png");
        await Assert.That(picture.Bytes).IsNotNull();
        await Assert.That(picture.Address).IsNull();
        await Assert.That((picture.Line, picture.Column, picture.Columns, picture.Rows)).IsEqualTo((1, 0, 2, 1));
        await Assert.That(read.Banner).DoesNotContain($"{Esc}P");

        var lines = read.Banner.Split('\n');
        await Assert.That(Plain(lines[0])).IsEqualTo("Ravenmoor");
        await Assert.That(Plain(lines[1])).IsEqualTo("  ");
        await Assert.That(Plain(lines[^1])).IsEqualTo("connect <name>");
    }

    [Test]
    public async Task AKittyTransmissionKeepsTheFileItSent()
    {
        var png = Png();
        var banner = $"Top\n{Esc}_Ga=T,f=100,t=d;{Convert.ToBase64String(png)}{Esc}\\\nBottom";

        var read = ScreenPictures.Read(banner, mxp: false);

        var picture = await Assert.That(read.Pictures).HasSingleItem();
        await Assert.That(picture!.MediaType).IsEqualTo("image/png");
        await Assert.That(picture.Bytes).IsEquivalentTo(png);
        await Assert.That(picture.Line).IsEqualTo(1);
    }

    [Test]
    public async Task AnITermFileIsDrawnOverTheCellsItAskedFor()
    {
        var png = Png();
        var banner = $"Top\n{Esc}]1337;File=inline=1;width=4;height=2:{Convert.ToBase64String(png)}\a\nBottom";

        var read = ScreenPictures.Read(banner, mxp: false);

        var picture = await Assert.That(read.Pictures).HasSingleItem();
        await Assert.That((picture!.Line, picture.Columns, picture.Rows)).IsEqualTo((1, 4, 2));
        await Assert.That(picture.Bytes).IsEquivalentTo(png);
    }

    [Test]
    public async Task AnSvgIsNeverKept()
    {
        // A document that can carry script, and served from this origin it would run as ours.
        var svg = Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><script>alert(1)</script></svg>");
        var banner = $"Top\n{Esc}]1337;File=inline=1;width=2;height=1:{Convert.ToBase64String(svg)}\a\nBottom";

        var read = ScreenPictures.Read(banner, mxp: false);

        await Assert.That(read.Pictures).IsEmpty();
        await Assert.That(read.Banner).IsEqualTo(banner)
            .Because("a screen that turned out to hold no picture we keep is not rewritten");
    }

    [Test]
    public async Task AnImageOverItsCellsBecomesAnAddressAndTheCellsStay()
    {
        var banner = string.Join('\n',
            $"{Esc}[1zWelcome to Ravenmoor",
            $"{Esc}[1z  <IMAGE castle.png URL=https://ravenmoor.example/pictures/ W=6c H=3c>      ",
            $"{Esc}[1z        ",
            $"{Esc}[1z        ",
            $"{Esc}[1zconnect <name> <password>");

        var read = ScreenPictures.Read(banner, mxp: true);

        var picture = await Assert.That(read.Pictures).HasSingleItem();
        await Assert.That(picture!.Address).IsEqualTo("https://ravenmoor.example/pictures/castle.png");
        await Assert.That((picture.Line, picture.Column, picture.Columns, picture.Rows)).IsEqualTo((1, 2, 6, 3));
        await Assert.That(read.Banner).DoesNotContain("<IMAGE");
        await Assert.That(read.Banner).Contains("connect <name> <password>")
            .Because("a decoded entity in the legend is text, not a tag to drop");
        await Assert.That(Plain(read.Banner.Split('\n')[1])).IsEqualTo("        ");
    }

    [Test]
    [Arguments("<IMAGE FName=\"castle.png\" URL=\"https://ravenmoor.example/pictures\" W=6c H=3c>")]
    [Arguments("<image 'https://ravenmoor.example/pictures/castle.png' w=6C h=3C>")]
    public async Task AnImageTagIsReadHoweverItIsSpelled(string tag)
    {
        var read = ScreenPictures.Read($"{Esc}[1z{tag}      \nafter", mxp: true);

        var picture = await Assert.That(read.Pictures).HasSingleItem();
        await Assert.That(picture!.Address).IsEqualTo("https://ravenmoor.example/pictures/castle.png");
        await Assert.That((picture.Columns, picture.Rows)).IsEqualTo((6, 3));
    }

    [Test]
    [Arguments("<IMAGE castle.png W=6c H=3c>")]
    [Arguments("<IMAGE castle.png URL=https://ravenmoor.example/ W=48 H=48>")]
    [Arguments("<IMAGE castle.png URL=file:///etc/ W=6c H=3c>")]
    [Arguments("<IMAGE castle.png URL=https://ravenmoor.example/ W=0c H=3c>")]
    public async Task AnImageWithNoAddressOrNoCellsStaysText(string tag)
    {
        // No directory to resolve against, a size in pixels (an inline picture with nowhere to draw
        // it), a scheme nobody fetches, and a picture no cells wide.
        var banner = $"{Esc}[1z{tag}      \nafter";

        var read = ScreenPictures.Read(banner, mxp: true);

        await Assert.That(read.Pictures).IsEmpty();
        await Assert.That(read.Banner).IsEqualTo(banner);
    }

    [Test]
    public async Task WithoutMxpAnImageTagIsAWord()
    {
        var banner = "<IMAGE castle.png URL=https://ravenmoor.example/ W=6c H=3c>\nafter";

        var read = ScreenPictures.Read(banner, mxp: false);

        await Assert.That(read.Pictures).IsEmpty();
        await Assert.That(read.Banner).IsEqualTo(banner);
    }

    [Test]
    public async Task NoMoreThanTheCeilingIsRead()
    {
        var tag = "<IMAGE castle.png URL=https://ravenmoor.example/ W=2c H=1c>  ";
        var banner = $"{Esc}[1z" + string.Concat(Enumerable.Repeat(tag, ScreenPictures.MaxPictures + 3));

        var read = ScreenPictures.Read(banner, mxp: true);

        await Assert.That(read.Pictures.Count).IsEqualTo(ScreenPictures.MaxPictures);
    }
}
