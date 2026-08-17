using System.Text;

namespace MUI.Crawl;

/// <summary>
/// Which encoding a session's bytes are read with, and the one place that decides it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A server's declared charset is not a measurement of its bytes.</b> Measured on
/// <c>mud.pkuxkx.net:8080</c>, which offers <c>IAC WILL CHARSET</c>, answers the REQUEST with a list
/// containing UTF-8 and nothing else, accepts our ACCEPTED — and then sends 2,573 bytes of GBK,
/// because on that game the encoding is chosen from a menu on the login screen
/// (<c>Input 1 for GBK, 2 for UTF8, 3 for BIG5</c>) rather than from the option we just negotiated.
/// Nothing was negotiated wrongly by either side. The declaration is simply about a later state of
/// the session than the connect screen we are reading.
/// </para>
/// <para>
/// <b>UTF-8 is checked rather than guessed.</b> A strict decoder is a grammar check, not a
/// heuristic: a lead byte must be followed by continuation bytes in <c>0x80–0xBF</c>, so legacy
/// multi-byte text essentially never forms well-formed UTF-8 by accident, and one stray 8-bit byte
/// is an invalid lead that fails immediately. That is the whole detector, and it is the reason there
/// is no ladder below it. Both GBK and Big5 <em>accept</em> pkuxkx's bytes — .NET's Big5 decoder
/// does not throw on them, it just puts 228 characters in odd blocks and 18 in the private use
/// area — so telling those two apart means scoring plausibility, and a score that is decisive on
/// 2.5 KB of dense CJK is a coin flip on a game that sends forty non-ASCII bytes. Guessing there and
/// writing the winner down would put our guess into a game's public record as a fact about the game,
/// which is rule 5. So we do not guess: the operator sets <see cref="Override"/>.
/// </para>
/// <para>
/// <b>The fallback is Latin-1 because it is reversible.</b> It maps all 256 byte values to
/// <c>U+0000–U+00FF</c> and cannot fail, so a banner read this way still *contains* its original
/// bytes and a later override repairs it exactly. Windows-1252 renders real Western text more often
/// than not, but it leaves five byte values undefined and would need a fallback of its own — and a
/// replacement character is the one outcome this type exists to prevent. What shipped before did
/// exactly that: <c>Encoding.UTF8</c>'s default replacing fallback turned every byte it could not
/// read into <c>U+FFFD</c> on the way in, permanently, for thirteen of the catalogue's 522 connect
/// screens.
/// </para>
/// </remarks>
public static class WireEncoding
{
    /// <summary>
    /// The check. Throws <see cref="DecoderFallbackException"/> rather than emitting <c>U+FFFD</c>,
    /// which is the entire difference between this and <see cref="Encoding.UTF8"/>.
    /// </summary>
    public static readonly Encoding Utf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The reversible fallback. Total on all 256 byte values, so it has no failure mode.</summary>
    public static readonly Encoding Fallback = Encoding.Latin1;

    static WireEncoding()
    {
        // .NET ships UTF-*, ASCII and Latin-1 in the box and nothing else: GBK, Big5, EUC-KR,
        // Shift-JIS and the Windows code pages all raise ArgumentException from GetEncoding until
        // this provider is registered. Measured on .NET 10 rather than assumed — the NU1510 the
        // package reference raises claims it is unnecessary, and it is not.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// The encoding an override names, or null when it names nothing this runtime has.
    /// </summary>
    /// <remarks>
    /// Null rather than a throw or a substitute: an override is a line of operator text and a typo
    /// in one must leave the probe reading bytes the ordinary way, not fail the session and not
    /// silently read them as something else. The caller records which it got.
    /// </remarks>
    public static Encoding? Override(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        try
        {
            // Replacing fallbacks, deliberately, and only here. A strict decoder is how we test a
            // guess; an override is not a guess, and one malformed byte in a screen somebody has
            // already told us how to read must not throw the screen away.
            return Encoding.GetEncoding(name.Trim());
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a whole session's lines, deciding the encoding once from all of them.
    /// </summary>
    /// <param name="lines">Every line the session produced, as it came off the wire.</param>
    /// <param name="overrideName">The operator's <c>CHARSET</c> override for this game, if any.</param>
    /// <remarks>
    /// <b>Once, from all of them.</b> A single line of pure ASCII is well-formed under every
    /// candidate and says nothing, so deciding per line would read the same server two ways within
    /// one screen. Testing every line is the same test as testing the concatenation, because a
    /// UTF-8 sequence cannot contain the <c>0x0A</c> these were split on.
    /// </remarks>
    public static WireReading Read(IReadOnlyList<byte[]> lines, string? overrideName = null)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (Override(overrideName) is { } forced)
        {
            return new WireReading(Decode(lines, forced), forced.WebName, WireCharset.Overridden);
        }

        if (IsUtf8(lines))
        {
            return new WireReading(Decode(lines, Utf8), Utf8.WebName, WireCharset.Proven);
        }

        return new WireReading(Decode(lines, Fallback), Fallback.WebName, WireCharset.Undetermined);
    }

    /// <summary>Whether every line is well-formed UTF-8.</summary>
    private static bool IsUtf8(IReadOnlyList<byte[]> lines)
    {
        foreach (var line in lines)
        {
            try
            {
                Utf8.GetString(line);
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<string> Decode(IReadOnlyList<byte[]> lines, Encoding encoding)
    {
        var decoded = new string[lines.Count];

        for (var i = 0; i < lines.Count; i++)
        {
            // Cleaned here, at the one place bytes become text in the crawler. See WireText.
            decoded[i] = WireText.Clean(encoding.GetString(lines[i]));
        }

        return decoded;
    }
}

/// <summary>
/// How much is actually known about the encoding a session's bytes were read with.
/// </summary>
/// <remarks>
/// <b>Three states, because two of them are knowledge and one is the absence of it</b> — and a
/// caller that cannot tell them apart will write our fallback down as a fact about the game. The
/// distinction is on the type rather than inferred from the charset name so that it cannot be lost.
/// </remarks>
public enum WireCharset
{
    /// <summary>A strict UTF-8 decoder accepted every byte. Evidence, not preference.</summary>
    Proven,

    /// <summary>An operator said what these bytes are, and we read them that way.</summary>
    Overridden,

    /// <summary>
    /// The bytes are not UTF-8 and nothing has said what they are, so they were read with the
    /// reversible fallback to keep them whole.
    /// </summary>
    /// <remarks>
    /// <b>This is not a measurement of ISO-8859-1 and must never be recorded as one.</b> Latin-1 was
    /// chosen because it is total over all 256 byte values and round-trips, so the screen survives
    /// until somebody says what it is — the encoding is <em>undetermined</em>, and an undetermined
    /// value stored as a determined one is rule 4 twice over: it fabricates an answer, and it does
    /// it under a source that says we measured it.
    /// </remarks>
    Undetermined,
}

/// <summary>
/// A session's text and the encoding it was read with.
/// </summary>
/// <param name="Lines">The decoded lines, in arrival order.</param>
/// <param name="Charset">
/// The encoding's <see cref="Encoding.WebName"/> — what was actually used, never what was declared
/// and never what an operator typed. <c>gbk</c>, <c>GBK</c> and <c>gb2312</c> all resolve to code
/// page 936 and are recorded as <c>gb2312</c>, so one encoding is one value in the catalogue.
/// </param>
/// <param name="Source">How much is known about that choice. Read this before storing anything.</param>
public sealed record WireReading(IReadOnlyList<string> Lines, string Charset, WireCharset Source)
{
    /// <summary>Whether an operator's override drove the decode, rather than the bytes or a fallback.</summary>
    public bool Overridden => Source is WireCharset.Overridden;
}
