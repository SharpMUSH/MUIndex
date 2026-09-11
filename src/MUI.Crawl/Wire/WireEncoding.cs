using System.Text;

namespace MUI.Crawl;

/// <summary>
/// Which encoding a session's bytes are read with, and the one place that decides it.
/// </summary>
/// <remarks>
/// A server's declared charset is not a measurement of its bytes: <c>mud.pkuxkx.net:8080</c>
/// negotiates <c>CHARSET</c> down to UTF-8 and then sends GBK anyway, because the actual encoding is
/// chosen from a menu on the login screen, a later point in the session than what we negotiated.
/// So UTF-8 is checked with a strict decoder rather than guessed — a lead byte must be followed by
/// continuation bytes in <c>0x80–0xBF</c>, so legacy multi-byte text essentially never forms
/// well-formed UTF-8 by accident. Anything else falls back to Latin-1, which is total over all 256
/// byte values and round-trips exactly, so a banner survives until an operator's <see
/// cref="Override"/> says what it really is. Guessing between other encodings (e.g. GBK vs Big5) and
/// writing the winner down would be recording our guess as a fact about the game, which is rule 5 —
/// and a replacement-character fallback (what shipped before this type) throws the original bytes
/// away permanently.
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
        // The provider ships in the shared framework, but nothing registers it: without this call,
        // GetEncoding throws ArgumentException for GBK, Big5, EUC-KR, Shift-JIS and the Windows code
        // pages. The System.Text.Encoding.CodePages *package* is not needed for it -- NU1510 said so,
        // and the GBK/Big5 cases in WireEncodingTests pass without it on .NET 11 -- the call is.
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
            // Replacing fallback, deliberately, and only here: an override isn't a guess, so one
            // malformed byte must not throw the whole screen away.
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
    /// <param name="alsoFromThisSession">
    /// More bytes the same server sent that are not part of the ordered screen — MSSP values, which
    /// arrive in a subnegotiation rather than in the stream. They decide the encoding along with
    /// <paramref name="lines"/> and are not returned; the caller decodes them with
    /// <see cref="WireReading.MsspEncoding"/>.
    /// </param>
    /// <param name="msspOverrideName">
    /// The operator's <c>CHARSET-MSSP</c> override, for a game whose report is not in the same
    /// encoding as its screen. Null on all but the few that have needed one.
    /// </param>
    /// <remarks>
    /// <para>
    /// Decided once for all lines, not per line — a single line of pure ASCII is well-formed under
    /// every candidate and would let the same server be read two ways within one screen. The unit is
    /// the <em>session</em> rather than the screen for the same reason one step out: a game with an
    /// ASCII login prompt and a GBK name in MSSP would otherwise be called UTF-8 on the strength of
    /// the prompt, and its name read with an encoding nothing ever tested.
    /// </para>
    /// <para>
    /// That last argument is exactly why <paramref name="msspOverrideName"/> takes the report's bytes
    /// out of the vote when it is set: the vote protects the report, and a report whose encoding has
    /// been stated no longer needs protecting. Leaving it in would mean an operator who fixes a
    /// game's name drags its connect screen to Latin-1 in the same stroke. Measured at
    /// <c>doom.twmuds.com:4000</c>, whose screen is Big5 and whose report is UTF-8 — no one value of
    /// <c>CHARSET</c> reads both, and the one that read the screen destroyed the name.
    /// </para>
    /// </remarks>
    public static WireReading Read(
        IReadOnlyList<byte[]> lines,
        string? overrideName = null,
        IReadOnlyList<byte[]>? alsoFromThisSession = null,
        string? msspOverrideName = null)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var report = Override(msspOverrideName);

        // Evidence about the screen only while nothing has said what the report is.
        var votes = report is null ? alsoFromThisSession : null;

        if (Override(overrideName) is { } forced)
        {
            return new WireReading(
                Decode(lines, forced), forced.WebName, WireCharset.Overridden, forced, report ?? forced);
        }

        if (IsUtf8(lines) && IsUtf8(votes))
        {
            return new WireReading(
                Decode(lines, Utf8), Utf8.WebName, WireCharset.Proven, Utf8, report ?? Utf8);
        }

        return new WireReading(
            Decode(lines, Fallback), Fallback.WebName, WireCharset.Undetermined, Fallback, report ?? Fallback);
    }

    /// <summary>Whether every line is well-formed UTF-8. Nothing to read is nothing against it.</summary>
    /// <remarks>
    /// A yes/no question answered without throwing or allocating: <see cref="System.Text.Unicode.Utf8.IsValid"/>
    /// applies the same strict-UTF-8 rule as <see cref="Utf8"/>'s decoder (a lead byte must be
    /// followed by well-formed continuation bytes, no substitution), which matters because most
    /// sessions this asks about are not UTF-8 — an exception per line was the normal, expected case
    /// on every server that isn't, not the rare one.
    /// </remarks>
    private static bool IsUtf8(IReadOnlyList<byte[]>? lines)
    {
        if (lines is null)
        {
            return true;
        }

        foreach (var line in lines)
        {
            if (!System.Text.Unicode.Utf8.IsValid(line))
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
/// Three states because two are knowledge and one is its absence — a caller that can't tell them
/// apart will write our fallback down as a fact about the game. Kept on the type, not inferred from
/// the charset name, so the distinction can't be lost.
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
    /// Not a measurement of ISO-8859-1 and must never be recorded as one — the encoding is genuinely
    /// <em>undetermined</em>; storing it as determined would be rule 4 twice over.
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
/// <param name="Encoding">
/// The encoding itself, for the rest of the session's bytes. Anything this server sent that is not
/// in <paramref name="Lines"/> — an MSSP value, which arrives in a subnegotiation — has to be read
/// with the decision the whole session produced rather than with one taken again over a single
/// field, which is usually a handful of bytes and decides nothing.
/// </param>
/// <param name="MsspEncoding">
/// The encoding the report's values are read with. The same object as <paramref name="Encoding"/>
/// unless an operator's <c>CHARSET-MSSP</c> says the report is in an encoding of its own — which
/// happens because world text is legacy when the game is, while a report is generally a config file
/// somebody wrote in a modern editor, and nothing makes the two agree.
/// </param>
public sealed record WireReading(
    IReadOnlyList<string> Lines,
    string Charset,
    WireCharset Source,
    Encoding Encoding,
    Encoding MsspEncoding)
{
    /// <summary>Whether an operator's override drove the decode, rather than the bytes or a fallback.</summary>
    public bool Overridden => Source is WireCharset.Overridden;
}
