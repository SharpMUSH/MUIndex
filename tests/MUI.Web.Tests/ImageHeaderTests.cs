using System.Buffers.Binary;

using MUI.Web.Icons;

namespace MUI.Web.Tests;

/// <summary>
/// What an image's own header says it is (spec §8.5, icons).
/// </summary>
/// <remarks>
/// <para>
/// The bytes here are assembled rather than loaded from files, and the four builders below are the
/// interesting part of the suite: a fixture <c>logo.png</c> proves the parser reads <em>that file</em>,
/// and says nothing about the layout it is supposed to know. Assembling one puts the format's own
/// rules in the test, where a reader can check them against the specification.
/// </para>
/// <para>
/// Every negative case matters more than the positive ones. This code reads bytes fetched from a URL
/// somebody else controls, so "what does it do with something that is not an image" is the question.
/// </para>
/// </remarks>
public class ImageHeaderTests
{
    [Test]
    public async Task APngStatesItsSizeInItsFirstHeaderChunk()
    {
        var read = ImageHeader.Read(Png(48, 32));

        await Assert.That(read!.ContentType).IsEqualTo("image/png");
        await Assert.That(read.Width).IsEqualTo(48);
        await Assert.That(read.Height).IsEqualTo(32);
    }

    [Test]
    public async Task AGifStatesItsSizeLittleEndianAfterItsVersion()
    {
        var read = ImageHeader.Read(Gif(64, 16));

        await Assert.That(read!.ContentType).IsEqualTo("image/gif");
        await Assert.That(read.Width).IsEqualTo(64);
        await Assert.That(read.Height).IsEqualTo(16);
    }

    /// <summary>A JPEG carries its size in a frame segment, which has to be walked to.</summary>
    [Test]
    public async Task AJpegIsWalkedToTheFrameThatCarriesItsSize()
    {
        var read = ImageHeader.Read(Jpeg(200, 120));

        await Assert.That(read!.ContentType).IsEqualTo("image/jpeg");
        await Assert.That(read.Width).IsEqualTo(200);
        await Assert.That(read.Height).IsEqualTo(120);
    }

    /// <summary>
    /// All three WebP layouts, because a reader that knew only the first would reject most real files.
    /// </summary>
    [Test]
    [Arguments("VP8 ")]
    [Arguments("VP8L")]
    [Arguments("VP8X")]
    public async Task EveryWebPLayoutIsRead(string chunk)
    {
        var read = ImageHeader.Read(WebP(chunk, 96, 72));

        await Assert.That(read!.ContentType).IsEqualTo("image/webp");
        await Assert.That(read.Width).IsEqualTo(96);
        await Assert.That(read.Height).IsEqualTo(72);
    }

    /// <summary>
    /// SVG is not an image this site will serve, however much it looks like one.
    /// </summary>
    /// <remarks>
    /// It is a document that can carry script. Served from our own origin it is a cross-site
    /// scripting hole with an image tag in front of it, and no size ceiling or content-type header
    /// changes that.
    /// </remarks>
    [Test]
    public async Task AnSvgIsNotAnImageWeWillServe()
    {
        await Assert.That(ImageHeader.Read("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8)).IsNull();
    }

    /// <summary>The bytes decide the type. What the far end called it never enters into it.</summary>
    [Test]
    public async Task TheBytesDecideTheTypeRatherThanAnybodysClaimAboutThem()
    {
        await Assert.That(ImageHeader.Read(Gif(8, 8))!.ContentType).IsEqualTo("image/gif");
    }

    /// <summary>
    /// Nothing here throws, whatever it is handed.
    /// </summary>
    /// <remarks>
    /// The input is a response body from a URL a stranger chose, so every one of these is a real
    /// case rather than a defensive gesture: empty, truncated mid-header, a plausible signature with
    /// nothing behind it, and a file whose declared segment length would walk the reader off the end.
    /// </remarks>
    [Test]
    public async Task NothingUnreadableIsAcceptedAndNothingThrows()
    {
        byte[][] rubbish =
        [
            [],
            [0x89, 0x50],
            [.. Png(4, 4)[..20]],
            [0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x01, 0, 0, 0, 0, 0, 0],
            [.. "not an image at all"u8],
        ];

        foreach (var bytes in rubbish)
        {
            await Assert.That(ImageHeader.Read(bytes)).IsNull();
        }
    }

    /// <summary>
    /// A header claiming a zero dimension is a header we misread, not a very small picture.
    /// </summary>
    [Test]
    public async Task AZeroDimensionIsTreatedAsAnUnreadableHeader()
    {
        await Assert.That(ImageHeader.Read(Png(0, 32))).IsNull();
    }

    private static byte[] Png(uint width, uint height)
    {
        var bytes = new byte[24];

        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(bytes);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), height);

        return bytes;
    }

    private static byte[] Gif(ushort width, ushort height)
    {
        var bytes = new byte[10];

        "GIF89a"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), width);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), height);

        return bytes;
    }

    /// <summary>
    /// A JPEG with one segment before the frame, so the walk has something to walk past.
    /// </summary>
    private static byte[] Jpeg(ushort width, ushort height)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };

        // An APP0 segment of length 4 — two bytes of length and two of payload — then SOF0.
        bytes.AddRange([0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00]);
        bytes.AddRange([0xFF, 0xC0, 0x00, 0x11, 0x08]);
        bytes.AddRange([(byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width]);

        return [.. bytes];
    }

    private static byte[] WebP(string chunk, int width, int height)
    {
        var bytes = new byte[30];

        "RIFF"u8.CopyTo(bytes);
        "WEBP"u8.CopyTo(bytes.AsSpan(8));
        System.Text.Encoding.ASCII.GetBytes(chunk).CopyTo(bytes.AsSpan(12));

        switch (chunk)
        {
            case "VP8 ":
                bytes[23] = 0x9D;
                bytes[24] = 0x01;
                bytes[25] = 0x2A;
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(26), (ushort)width);
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28), (ushort)height);
                break;

            case "VP8L":
                bytes[20] = 0x2F;
                BinaryPrimitives.WriteUInt32LittleEndian(
                    bytes.AsSpan(21), (uint)((width - 1) | ((height - 1) << 14)));
                break;

            case "VP8X":
                bytes[24] = (byte)(width - 1);
                bytes[25] = (byte)((width - 1) >> 8);
                bytes[26] = (byte)((width - 1) >> 16);
                bytes[27] = (byte)(height - 1);
                bytes[28] = (byte)((height - 1) >> 8);
                bytes[29] = (byte)((height - 1) >> 16);
                break;
        }

        return bytes;
    }
}
