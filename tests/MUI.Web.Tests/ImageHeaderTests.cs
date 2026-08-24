using System.Buffers.Binary;

using MUI.Web.Icons;

namespace MUI.Web.Tests;

/// <summary>
/// What an image's own header says it is (spec §8.5, icons).
/// </summary>
/// <remarks>
/// Bytes are assembled rather than loaded from files, so the format's own rules are visible in the
/// test rather than hidden in a fixture. Every negative case matters more than the positive ones:
/// this code reads bytes fetched from a URL somebody else controls.
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
    /// An ICO states each image's size in one byte apiece, and the largest is the one we report.
    /// </summary>
    /// <remarks>
    /// A multi-size icon is one file the browser picks from, so reporting the smallest entry would
    /// let a 256×256 image through a ceiling that exists to bound what we store.
    /// </remarks>
    [Test]
    public async Task AnIcoIsReadFromItsDirectoryAndTakesItsLargestEntry()
    {
        var read = ImageHeader.Read(Ico((16, 16), (48, 32), (32, 32)));

        await Assert.That(read!.ContentType).IsEqualTo("image/x-icon");
        await Assert.That(read.Width).IsEqualTo(48);
        await Assert.That(read.Height).IsEqualTo(32);
    }

    /// <summary>Zero in an ICO's size byte means 256, which is the one value it cannot hold.</summary>
    [Test]
    public async Task AnIcoSizeByteOfZeroMeans256()
    {
        var read = ImageHeader.Read(Ico((0, 0)));

        await Assert.That(read!.Width).IsEqualTo(256);
        await Assert.That(read.Height).IsEqualTo(256);
    }

    /// <summary>
    /// A cursor is not an icon: the same container, a hotspot where the colour planes go, and not a
    /// thing anybody meant to publish as their logo.
    /// </summary>
    [Test]
    public async Task ACursorIsNotAnIcon()
    {
        var bytes = Ico((32, 32));
        bytes[2] = 2;

        await Assert.That(ImageHeader.Read(bytes)).IsNull();
    }

    /// <summary>
    /// A directory claiming more entries than the body holds is a truncated file, not an icon.
    /// </summary>
    [Test]
    public async Task AnIcoPromisingMoreThanItHoldsIsRefused()
    {
        var bytes = Ico((32, 32));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), 40);

        await Assert.That(ImageHeader.Read(bytes)).IsNull();
    }

    /// <summary>
    /// A BMP holds two signed dimensions after a DIB header whose length says how wide they are.
    /// </summary>
    [Test]
    [Arguments(40)]
    [Arguments(108)]
    [Arguments(124)]
    public async Task ABmpIsReadFromWhicheverDibHeaderItCarries(int dib)
    {
        var read = ImageHeader.Read(Bmp(dib, 120, 64));

        await Assert.That(read!.ContentType).IsEqualTo("image/bmp");
        await Assert.That(read.Width).IsEqualTo(120);
        await Assert.That(read.Height).IsEqualTo(64);
    }

    /// <summary>The oldest DIB header holds 16-bit dimensions and has to be read as such.</summary>
    [Test]
    public async Task ABitmapCoreHeaderHoldsNarrowerDimensions()
    {
        var read = ImageHeader.Read(Bmp(12, 90, 45));

        await Assert.That(read!.Width).IsEqualTo(90);
        await Assert.That(read.Height).IsEqualTo(45);
    }

    /// <summary>
    /// A negative height means the rows are stored top-down, not that the image has a negative size.
    /// </summary>
    [Test]
    public async Task ATopDownBmpIsAsTallAsItsHeightsMagnitude()
    {
        await Assert.That(ImageHeader.Read(Bmp(40, 120, -64))!.Height).IsEqualTo(64);
    }

    /// <summary>
    /// SVG is not an image this site will serve, however much it looks like one.
    /// </summary>
    /// <remarks>SVG is a document that can carry script — served from our own origin it's an XSS hole with an image tag in front of it.</remarks>
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
    /// <remarks>The input is a response body from an attacker-chosen URL, so each of these is a real case: empty, truncated mid-header, a plausible signature with nothing behind it, a declared segment length that would walk off the end.</remarks>
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

    /// <summary>An ICO directory with one 16-byte entry per size given, and no image data behind it.</summary>
    private static byte[] Ico(params (byte Width, byte Height)[] entries)
    {
        var bytes = new byte[6 + (entries.Length * 16)];

        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), (ushort)entries.Length);

        for (var entry = 0; entry < entries.Length; entry++)
        {
            bytes[6 + (entry * 16)] = entries[entry].Width;
            bytes[7 + (entry * 16)] = entries[entry].Height;
        }

        return bytes;
    }

    /// <summary>A BMP file header and the first two fields of whichever DIB header was asked for.</summary>
    private static byte[] Bmp(int dibLength, int width, int height)
    {
        var bytes = new byte[26];

        "BM"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(14), (uint)dibLength);

        if (dibLength == 12)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(18), (ushort)width);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), (ushort)height);

            return bytes;
        }

        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), height);

        return bytes;
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

        // APP0 segment of length 4, then SOF0.
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
