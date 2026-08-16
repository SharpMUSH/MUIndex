using System.Buffers.Binary;

namespace MUI.Web.Icons;

/// <summary>
/// What an image's own header says it is — the type and how big — and the reader that gets it there
/// (spec §8.5, icons).
/// </summary>
/// <param name="ContentType">The type read from the bytes, never the one the far end claimed.</param>
/// <remarks>
/// <para>
/// <b>A header parser rather than a decoder, and rather than an image library.</b> All this needs to
/// know is what the bytes are and how big the picture is, and every one of the four formats states
/// both within the first thirty bytes. Decoding would mean running a full image pipeline over a file
/// fetched from a URL a stranger chose, which is a decoder attack surface acquired to answer a
/// question the header already answers — and it would mean taking a dependency whose licence we would
/// then have to keep track of.
/// </para>
/// <para>
/// <b>The bytes decide the type, and the <c>Content-Type</c> header never does.</b> A far end that
/// says <c>image/png</c> and sends a GIF is not lying about anything important, but a site that
/// echoed the claim would be serving a type it had not checked from its own origin.
/// </para>
/// <para>
/// <b>SVG is not here and will not be.</b> It is a document that can carry script, and serving one
/// from this origin is a cross-site scripting hole with an image tag in front of it. An owner who
/// wants a vector logo can publish a PNG of it.
/// </para>
/// </remarks>
public sealed record ImageHeader(string ContentType, int Width, int Height)
{
    /// <summary>
    /// What the bytes say, or null for anything this does not recognise or cannot read.
    /// </summary>
    /// <remarks>
    /// Null rather than a guess, and null rather than a throw. An unreadable header is an image we
    /// decline to serve, which is a decision with a rendering — no element at all — and not an error
    /// anybody needs to hear about.
    /// </remarks>
    public static ImageHeader? Read(ReadOnlySpan<byte> bytes) =>
        Png(bytes) ?? Gif(bytes) ?? WebP(bytes) ?? Jpeg(bytes);

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
    /// Three layouts rather than one because the format grew: <c>VP8 </c> packs 14-bit dimensions
    /// after a start code, <c>VP8L</c> bit-packs two 14-bit values less one into a 32-bit word, and
    /// <c>VP8X</c> — the one an animated or alpha-carrying file uses — stores 24-bit values less one.
    /// A reader that knew only the first would reject most real WebP files as unreadable.
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
            // A three-byte frame tag, then the 0x9D 0x01 0x2A start code, then the two dimensions in
            // the low fourteen bits of a little-endian pair each.
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
    /// JPEG: no dimensions in the header at all, so the segment chain is walked to the frame that
    /// carries them.
    /// </summary>
    /// <remarks>
    /// The only one of the four that needs walking, and the walk is bounded twice over — by the
    /// buffer, and by each segment's own declared length, which is why a malformed length terminates
    /// rather than looping. <c>C4</c>, <c>C8</c> and <c>CC</c> are excluded because they are Huffman
    /// tables and arithmetic-coding conditioning rather than frames, and they sit in the middle of
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
    /// A zero dimension is not a small image; it is a file whose header we have misread or that was
    /// truncated on the way. Refusing here rather than passing it on means the caller's size ceiling
    /// never has to distinguish "tiny" from "wrong".
    /// </remarks>
    private static ImageHeader? Sized(string contentType, uint width, uint height) =>
        width is 0 or > int.MaxValue || height is 0 or > int.MaxValue
            ? null
            : new ImageHeader(contentType, (int)width, (int)height);
}
