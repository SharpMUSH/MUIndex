namespace MUI.Crawl;

/// <summary>
/// Text as it came off the wire, with the one byte that is framing rather than content removed.
/// </summary>
/// <remarks>
/// <para>
/// <b>NUL is telnet's, not the game's.</b> RFC 854 defines <c>CR NUL</c> as the way to send a bare
/// carriage return in a NVT stream — the NUL exists to say "this CR is not the start of CR LF". It
/// is framing, and a line-oriented reader that keeps it is holding a byte the server never meant as
/// a character.
/// </para>
/// <para>
/// Keeping it was not cosmetic. PostgreSQL's <c>text</c> cannot store <c>0x00</c> at all, so the
/// first field carrying one raised <c>22021 invalid byte sequence for encoding "UTF8"</c> mid-write:
/// the game was left half-created with a single field, its endpoint was never scheduled, and its
/// <c>REFERRAL</c> list was never read — one NUL from one server cost that server its listing and
/// cost the crawl every game it would have led to. Measured on <c>mud.kharkov.org:3000</c>, a
/// CircleMUD which sends <c>CR NUL</c> line endings.
/// </para>
/// <para>
/// <b>It is also the cheapest denial of service a hostile server has.</b> One byte in one MSSP value
/// aborts the whole ingestion of that probe, every probe, for ever — so this is stripped at the
/// boundary where text enters the crawler rather than defended against at each store, which would be
/// several rules that can drift instead of one that cannot.
/// </para>
/// <para>
/// Nothing else is removed. Escape sequences stay, because the connect screen is rendered as the
/// game sent it and its colour is part of what a reader is being shown; tabs and newlines stay,
/// because they are layout. This is the one byte that cannot survive the round trip.
/// </para>
/// </remarks>
public static class WireText
{
    /// <summary>The text with NUL removed, or the same instance when there is none — the usual case.</summary>
    public static string Clean(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.Contains('\0', StringComparison.Ordinal) ? text.Replace("\0", string.Empty) : text;
    }
}
