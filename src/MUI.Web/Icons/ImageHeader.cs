using System.Buffers.Binary;

namespace MUI.Web.Icons;

/// <summary>
/// What an image's own header says it is — the type and how big — and the reader that gets it there
/// (spec §8.5, icons).
/// </summary>
/// <param name="ContentType">The type read from the bytes, never the one the far end claimed.</param>
/// <remarks>
/// A header parser, not a decoder or an image library — decoding would run a full image pipeline over
/// a file fetched from an attacker-chosen URL, a decoder attack surface acquired to answer a question
/// the header already answers. The bytes decide the type; <c>Content-Type</c> never does. SVG is not
/// here and will not be: it can carry script, and serving one from this origin is an XSS hole with an
/// image tag in front of it.
/// </remarks>
public sealed record ImageHeader(string ContentType, int Width, int Height)
{
    /// <summary>
    /// What the bytes say, or null for anything this does not recognise or cannot read.
    /// </summary>
    /// <remarks>
    /// Null rather than a guess or a throw: an unreadable header is an image we decline to serve.
    /// </remarks>
    public static ImageHeader? Read(ReadOnlySpan<byte> bytes) =>
        Png(bytes) ?? Gif(bytes) ?? WebP(bytes) ?? Ico(bytes) ?? Bmp(bytes) ?? Jpeg(bytes);

    /// <summary>PNG: an eight-byte signature, then IHDR carrying two big-endian dimensions.</summary>
    private static ImageHeader? Png(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        if (bytes.Length < 24 || !bytes[..8].SequenceEqual(signature) || !bytes[12..16].SequenceEqual("IHDR"u8))
        {
            return null;
        }

        return Sized(
            "image/png",
            BinaryPrimitives.ReadUInt32BigEndian(bytes[16..20]),
            BinaryPrimitives.ReadUInt32BigEndian(bytes[20..24]));
    }

    /// <summary>GIF: a six-byte version string, then two little-endian dimensions.</summary>
    private static ImageHeader? Gif(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 10 || (!bytes[..6].SequenceEqual("GIF87a"u8) && !bytes[..6].SequenceEqual("GIF89a"u8)))
        {
            return null;
        }

        return Sized(
            "image/gif",
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..8]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..10]));
    }

    /// <summary>
    /// WebP: a RIFF container whose one chunk is lossy, lossless or extended, each storing its size
    /// differently.
    /// </summary>
    /// <remarks>
    /// Three layouts because the format grew: <c>VP8 </c> packs 14-bit dimensions after a start code,
    /// <c>VP8L</c> bit-packs two 14-bit values less one into a 32-bit word, and <c>VP8X</c> (animated
    /// or alpha-carrying) stores 24-bit values less one.
    /// </remarks>
    private static ImageHeader? WebP(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 30 || !bytes[..4].SequenceEqual("RIFF"u8) || !bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return null;
        }

        var chunk = bytes[12..16];

        if (chunk.SequenceEqual("VP8 "u8))
        {
            ReadOnlySpan<byte> startCode = [0x9D, 0x01, 0x2A];

            if (!bytes[23..26].SequenceEqual(startCode))
            {
                return null;
            }

            return Sized(
                "image/webp",
                (uint)(BinaryPrimitives.ReadUInt16LittleEndian(bytes[26..28]) & 0x3FFF),
                (uint)(BinaryPrimitives.ReadUInt16LittleEndian(bytes[28..30]) & 0x3FFF));
        }

        if (chunk.SequenceEqual("VP8L"u8))
        {
            if (bytes[20] != 0x2F)
            {
                return null;
            }

            var packed = BinaryPrimitives.ReadUInt32LittleEndian(bytes[21..25]);

            return Sized("image/webp", (packed & 0x3FFF) + 1, ((packed >> 14) & 0x3FFF) + 1);
        }

        if (chunk.SequenceEqual("VP8X"u8))
        {
            return Sized(
                "image/webp",
                (uint)(bytes[24] | (bytes[25] << 8) | (bytes[26] << 16)) + 1,
                (uint)(bytes[27] | (bytes[28] << 8) | (bytes[29] << 16)) + 1);
        }

        return null;
    }

    /// <summary>
    /// ICO: a six-byte directory header, then one 16-byte entry per image, each stating its own size
    /// in a single byte.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Here because <c>favicon.ico</c> is what a MU* operator has to hand — thirty of the catalogue's
    /// declared <c>ICON</c>s name one, which is more than name a PNG. It is a container rather than a
    /// codec, so this reads the directory and never the images inside it, which is the same
    /// no-decoder position the rest of this file takes.
    /// </para>
    /// <para>
    /// A zero byte means 256, which is the format's way of fitting 256 into a byte that stops at 255.
    /// The largest entry is the one reported: a multi-size icon is one file, the browser picks from
    /// it, and reporting the 16×16 would let a 256×256 through a ceiling that exists to bound what we
    /// store. Type 1 only — type 2 is a cursor, which has a hotspot where an icon has colour planes
    /// and is not a thing anybody meant to publish as a logo.
    /// </para>
    /// </remarks>
    private static ImageHeader? Ico(ReadOnlySpan<byte> bytes)
    {
        const int DirectoryHeader = 6;
        const int EntryLength = 16;

        if (bytes.Length < DirectoryHeader
            || BinaryPrimitives.ReadUInt16LittleEndian(bytes[..2]) != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(bytes[2..4]) != 1)
        {
            return null;
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..6]);

        // A directory that doesn't fit in what we were handed is a truncated or lying file, not an
        // icon with fewer images than it says.
        if (count == 0 || bytes.Length < DirectoryHeader + (count * EntryLength))
        {
            return null;
        }

        uint width = 0;
        uint height = 0;

        for (var entry = 0; entry < count; entry++)
        {
            var at = DirectoryHeader + (entry * EntryLength);
            var entryWidth = bytes[at] == 0 ? 256u : bytes[at];
            var entryHeight = bytes[at + 1] == 0 ? 256u : bytes[at + 1];

            if (entryWidth * entryHeight <= width * height)
            {
                continue;
            }

            width = entryWidth;
            height = entryHeight;
        }

        // image/x-icon rather than the registered image/vnd.microsoft.icon: served under nosniff, so
        // the type has to be one every browser actually renders, and this is the one they all do.
        return Sized("image/x-icon", width, height);
    }

    /// <summary>
    /// BMP: a two-byte signature, a file header, then a DIB header whose own length says which of the
    /// two dimension layouts follows.
    /// </summary>
    /// <remarks>
    /// The 12-byte <c>BITMAPCOREHEADER</c> holds 16-bit dimensions; everything later holds 32-bit
    /// ones, and a negative height there means the rows are stored top-down rather than that the
    /// image is upside down in any sense we care about — its magnitude is the height.
    /// </remarks>
    private static ImageHeader? Bmp(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 26 || !bytes[..2].SequenceEqual("BM"u8))
        {
            return null;
        }

        var dibLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes[14..18]);

        if (dibLength == 12)
        {
            return Sized(
                "image/bmp",
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[18..20]),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[20..22]));
        }

        // Every other documented DIB header — INFO at 40, V4 at 108, V5 at 124 — starts with the same
        // two signed 32-bit dimensions, so they are read the same way rather than enumerated.
        if (dibLength < 40)
        {
            return null;
        }

        var width = BinaryPrimitives.ReadInt32LittleEndian(bytes[18..22]);
        var height = BinaryPrimitives.ReadInt32LittleEndian(bytes[22..26]);

        return width <= 0 || height == 0 || height == int.MinValue
            ? null
            : Sized("image/bmp", (uint)width, (uint)Math.Abs(height));
    }

    /// <summary>
    /// JPEG: no dimensions in the header at all, so the segment chain is walked to the frame that
    /// carries them.
    /// </summary>
    /// <remarks>
    /// The walk is bounded twice over — by the buffer, and by each segment's own declared length, so a
    /// malformed length terminates rather than looping. <c>C4</c>, <c>C8</c> and <c>CC</c> are excluded
    /// since they're Huffman tables and arithmetic-coding conditioning, not frames, despite sitting in
    /// the same marker range.
    /// </remarks>
    private static ImageHeader? Jpeg(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
        {
            return null;
        }

        var at = 2;

        // Nine, and inclusive: the frame's dimensions end at at+9, so a buffer of exactly that length
        // holds a complete frame header. Written as a strict inequality this rejected the smallest
        // well-formed JPEG it could be handed.
        while (at + 9 <= bytes.Length)
        {
            if (bytes[at] != 0xFF)
            {
                return null;
            }

            var marker = bytes[at + 1];
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 2)..(at + 4)]);

            if (length < 2)
            {
                return null;
            }

            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                return Sized(
                    "image/jpeg",
                    BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 7)..(at + 9)]),
                    BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 5)..(at + 7)]));
            }

            at += 2 + length;
        }

        return null;
    }

    /// <summary>
    /// A header, unless the dimensions are absurd — which is what an unrecognised layout looks like.
    /// </summary>
    /// <remarks>
    /// A zero dimension means a misread or truncated header, not a small image — refused here so the
    /// caller's size ceiling never has to distinguish "tiny" from "wrong".
    /// </remarks>
    private static ImageHeader? Sized(string contentType, uint width, uint height) =>
        width is 0 or > int.MaxValue || height is 0 or > int.MaxValue
            ? null
            : new ImageHeader(contentType, (int)width, (int)height);
}
