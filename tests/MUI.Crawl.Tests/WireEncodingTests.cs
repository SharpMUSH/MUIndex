using System.Text;

using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// Which encoding a session's bytes are read with, pinned against bytes a real server really sent.
/// </summary>
/// <remarks>
/// <para>
/// The fixture is one line of <c>mud.pkuxkx.net:8080</c>'s connect screen — the game's own name in
/// GBK, inside the SGR it arrived in. It is here rather than invented because the whole class of bug
/// is a disagreement between what a server declares and what it sends, and only a real server
/// produces one: this one negotiates CHARSET, offers UTF-8 and nothing else, accepts our ACCEPTED,
/// and then sends this.
/// </para>
/// <para>
/// What is being defended is not "we render Chinese". It is that <b>a byte we cannot read is never
/// replaced by <c>U+FFFD</c> on the way in</b>. That is unrecoverable, it happened to thirteen of
/// 522 connect screens in production, and no later fix can undo it.
/// </para>
/// </remarks>
public class WireEncodingTests
{
    /// <summary>
    /// <c>----====   北  大  侠  客  行  ====----</c>, exactly as it came off the wire.
    /// </summary>
    private static readonly byte[] PkuxkxTitle =
    [
        0x1B, 0x5B, 0x31, 0x3B, 0x33, 0x36, 0x6D, 0x2D, 0x2D, 0x2D, 0x2D, 0x3D, 0x3D, 0x3D, 0x3D,
        0x20, 0x20, 0x20, 0xB1, 0xB1, 0x20, 0x20, 0xB4, 0xF3, 0x20, 0x20, 0xCF, 0xC0, 0x20, 0x20,
        0xBF, 0xCD, 0x20, 0x20, 0xD0, 0xD0, 0x20, 0x20, 0x3D, 0x3D, 0x3D, 0x3D, 0x2D, 0x2D, 0x2D,
        0x2D, 0x1B, 0x5B, 0x30, 0x6D,
    ];

    private static readonly byte[] Ascii = "By what name do you wish to be known?"u8.ToArray();

    /// <summary>
    /// The bug this type exists for, stated as a test: nothing it produces contains a replacement
    /// character, whatever the bytes were.
    /// </summary>
    /// <remarks>
    /// <c>Encoding.UTF8.GetString</c> — the default instance, with its replacing fallback — turns
    /// this line into eleven <c>U+FFFD</c>. That is what shipped, and it is asserted here so that a
    /// future simplification back to it fails rather than passes quietly.
    /// </remarks>
    [Test]
    public async Task BytesThatAreNotUtf8AreNeverReplacedWithU00FFFD()
    {
        await Assert.That(Encoding.UTF8.GetString(PkuxkxTitle)).Contains("�");

        var reading = WireEncoding.Read([PkuxkxTitle]);

        await Assert.That(reading.Lines[0]).DoesNotContain("�");
    }

    /// <summary>
    /// A session whose bytes are not well-formed UTF-8 falls back to Latin-1, and Latin-1 keeps the
    /// bytes — so a later override can repair the screen exactly rather than approximately.
    /// </summary>
    [Test]
    public async Task TheFallbackIsReversible()
    {
        var reading = WireEncoding.Read([PkuxkxTitle]);

        await Assert.That(reading.Charset).IsEqualTo("iso-8859-1");
        await Assert.That(reading.Source).IsEqualTo(WireCharset.Undetermined);

        // The whole point of Latin-1 rather than windows-1252: every byte survives the round trip,
        // so the original wire bytes are still in the stored string.
        await Assert.That(Encoding.Latin1.GetBytes(reading.Lines[0])).IsEquivalentTo(PkuxkxTitle);
    }

    /// <summary>An operator's override is honoured, and it is the thing that makes the screen right.</summary>
    /// <remarks>
    /// <b>The recorded name is the encoding's, not the operator's.</b> <c>gbk</c>, <c>GBK</c> and
    /// <c>gb2312</c> all resolve to code page 936, whose <see cref="Encoding.WebName"/> is
    /// <c>gb2312</c> — so every spelling of one encoding lands in the catalogue as one value, which
    /// is what a facet needs and what stops the same encoding appearing three times in a count.
    /// <c>ScreenLanguage</c> maps every spelling anyway, so nothing downstream depends on which the
    /// operator typed.
    /// </remarks>
    [Test]
    public async Task AnOverrideReadsWhatTheServerActuallySent()
    {
        var reading = WireEncoding.Read([PkuxkxTitle], "gbk");

        await Assert.That(reading.Source).IsEqualTo(WireCharset.Overridden);
        await Assert.That(reading.Charset).IsEqualTo("gb2312");
        await Assert.That(WireEncoding.Read([PkuxkxTitle], "GB2312").Charset).IsEqualTo("gb2312");
        await Assert.That(reading.Lines[0]).Contains("北  大  侠  客  行");

        // The SGR is content and survives the decode, because the connect screen is rendered as the
        // game sent it.
        await Assert.That(reading.Lines[0]).StartsWith("\u001b[1;36m");
    }

    /// <summary>
    /// Well-formed UTF-8 is read as UTF-8 without anybody being asked, because a strict decoder
    /// proves it rather than guessing it.
    /// </summary>
    [Test]
    public async Task WellFormedUtf8NeedsNoOverride()
    {
        var utf8 = Encoding.UTF8.GetBytes("Café Noir — 北大侠客行");

        var reading = WireEncoding.Read([utf8]);

        await Assert.That(reading.Charset).IsEqualTo("utf-8");
        await Assert.That(reading.Lines[0]).IsEqualTo("Café Noir — 北大侠客行");
    }

    /// <summary>
    /// The decision is taken over the whole session, not line by line.
    /// </summary>
    /// <remarks>
    /// This is the case that makes per-line decoding wrong: the ASCII prompt is well-formed UTF-8
    /// and the title line is not, and reading each on its own merits would render one screen in two
    /// encodings. A screen is one thing a server sent and it gets one answer.
    /// </remarks>
    [Test]
    public async Task OneScreenGetsOneAnswer()
    {
        var reading = WireEncoding.Read([Ascii, PkuxkxTitle, Ascii]);

        await Assert.That(reading.Charset).IsEqualTo("iso-8859-1");
        await Assert.That(reading.Lines).Count().IsEqualTo(3);

        // Pure ASCII is identical under both, which is why it could not have decided this alone.
        await Assert.That(reading.Lines[0]).IsEqualTo("By what name do you wish to be known?");
    }

    /// <summary>
    /// A screen with nothing but ASCII in it is UTF-8, which is true and says nothing about the
    /// server — the point is that it does not fall to the 8-bit path for want of evidence.
    /// </summary>
    [Test]
    public async Task AsciiIsUtf8()
    {
        await Assert.That(WireEncoding.Read([Ascii]).Charset).IsEqualTo("utf-8");
    }

    /// <summary>
    /// An override naming an encoding this runtime does not have is ignored rather than fatal.
    /// </summary>
    /// <remarks>
    /// It is a line of operator text. A typo in one must leave the probe reading bytes the ordinary
    /// way — the alternative is a mistyped override taking a game's screen off the site until
    /// somebody notices.
    /// </remarks>
    [Test]
    public async Task ANonsenseOverrideIsIgnoredAndTheProbeStillReads()
    {
        await Assert.That(WireEncoding.Override("not-an-encoding")).IsNull();
        await Assert.That(WireEncoding.Override("  ")).IsNull();
        await Assert.That(WireEncoding.Override(null)).IsNull();

        var reading = WireEncoding.Read([PkuxkxTitle], "not-an-encoding");

        await Assert.That(reading.Source).IsEqualTo(WireCharset.Undetermined);
        await Assert.That(reading.Charset).IsEqualTo("iso-8859-1");
    }

    /// <summary>
    /// The legacy code pages resolve at all, which they do not without
    /// <c>CodePagesEncodingProvider</c> — measured, because the package reference that supplies it
    /// raises an NU1510 saying it is unnecessary.
    /// </summary>
    [Test]
    [Arguments("gbk")]
    [Arguments("big5")]
    [Arguments("euc-kr")]
    [Arguments("shift_jis")]
    [Arguments("windows-1251")]
    [Arguments("koi8-r")]
    public async Task TheLegacyCodePagesAreRegistered(string name)
    {
        await Assert.That(WireEncoding.Override(name)).IsNotNull();
    }

    /// <summary>
    /// Why there is no detection ladder, kept as an executable statement rather than a comment.
    /// </summary>
    /// <remarks>
    /// Both decoders accept these bytes — .NET's Big5 does not throw on them the way Python's does —
    /// so validity cannot separate the two and the choice would rest on scoring how plausible the
    /// output looks. That score is decisive on 2.5 KB of dense CJK and a coin flip on a game that
    /// sends forty non-ASCII bytes, and a coin flip written into a game's public record is rule 5.
    /// The operator decides; this test exists so that anybody proposing to automate it sees the
    /// reason first.
    /// </remarks>
    [Test]
    public async Task GbkAndBig5BothAcceptTheseBytesAndMeanDifferentThings()
    {
        var asGbk = WireEncoding.Read([PkuxkxTitle], "gbk").Lines[0];
        var asBig5 = WireEncoding.Read([PkuxkxTitle], "big5").Lines[0];

        await Assert.That(asGbk).IsNotEqualTo(asBig5);
        await Assert.That(asGbk).Contains("北");
        await Assert.That(asBig5).DoesNotContain("北");
    }
}
