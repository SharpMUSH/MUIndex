namespace MUI.Crawl;

/// <summary>
/// Text as it came off the wire, with the one byte that is framing rather than content removed.
/// </summary>
/// <remarks>
/// NUL is telnet's <c>CR NUL</c> line-ending marker (RFC 854), not game content, and PostgreSQL's
/// <c>text</c> column cannot store <c>0x00</c> — an unstripped NUL in one MSSP value aborts ingestion
/// of that whole probe. Stripped once at this boundary rather than defended against at each store.
/// Escape sequences, tabs and newlines are left alone; NUL is the one byte that cannot survive the
/// round trip.
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
