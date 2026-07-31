namespace MUI.Crawl;

/// <summary>
/// The plaintext <c>MSSP-REQUEST</c> form of MSSP: a line of text at the login screen, answered with
/// <c>MSSP-REPLY-START</c>, tab-separated <c>name</c>/<c>value</c> lines, and <c>MSSP-REPLY-END</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is layer 4's second route (spec §6.4), and it is a genuinely different one: the telnet
/// option is a subnegotiation the library decodes, while this is text the server prints and we read.
/// The two need not agree byte for byte — which is exactly why <see cref="MsspTransport"/> is
/// recorded beside the value rather than thrown away once parsed.
/// </para>
/// <para>
/// <b>Nothing here goes on the wire.</b> Sending the request is <see cref="TelnetProbe"/>'s decision
/// and is off unless a caller asks for it; this type only reads a reply that has already arrived,
/// which is what makes it testable against a captured transcript with no socket anywhere.
/// </para>
/// </remarks>
public static class PlaintextMssp
{
    /// <summary>The line the server is asked with. On <see cref="TelnetProbe.PermittedCommands"/>.</summary>
    public const string Request = "MSSP-REQUEST";

    /// <summary>The marker that opens a reply.</summary>
    public const string ReplyStart = "MSSP-REPLY-START";

    /// <summary>The marker that closes one.</summary>
    public const string ReplyEnd = "MSSP-REPLY-END";

    /// <summary>
    /// The most fields a reply may carry before the rest are ignored.
    /// </summary>
    /// <remarks>
    /// The official vocabulary is 45 variables and servers invent a few more, so 512 is generous for
    /// anything legitimate. It exists because the option-70 path is bounded by
    /// <see cref="ProbeOptions.MaxSubnegotiationBytes"/> and this path would otherwise be bounded by
    /// nothing but how long a stranger cares to keep typing.
    /// </remarks>
    public const int MaxFields = 512;

    /// <summary>The most characters one value may carry.</summary>
    public const int MaxValueLength = 4096;

    /// <summary>
    /// Reads a reply out of the lines that arrived after the request, or null if none is there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Null and empty are different answers and both are real.</b> Null means these lines are not
    /// an MSSP reply — the overwhelmingly common case, since a server that does not implement the
    /// form reads <c>MSSP-REQUEST</c> as a character name and says so
    /// (<c>Illegal name, try another.</c>, measured on <c>realms.reichel.net:4000</c> and
    /// <c>tsosmud.org:7070</c>). An empty dictionary means the server opened a reply and put nothing
    /// in it, which is its answer and is the plaintext twin of an empty option-70 report.
    /// </para>
    /// <para>
    /// A missing <c>MSSP-REPLY-END</c> is tolerated rather than fatal. The marker is how the server
    /// says it has finished, but the probe already stops reading on a quiet period, so treating a
    /// reply whose end marker has not yet landed as no reply at all would discard a good report over
    /// our own timing. The opening marker is required, because without it there is nothing to say
    /// these lines were addressed to us.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>>? Parse(IEnumerable<string>? lines)
    {
        if (lines is null)
        {
            return null;
        }

        var fields = new OrderedDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var started = false;

        foreach (var raw in lines)
        {
            var line = (raw ?? string.Empty).Trim();

            if (!started)
            {
                started = line.Equals(ReplyStart, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (line.Equals(ReplyEnd, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            // MSSP's own separator is a tab, and only the first one separates: a value may contain
            // tabs, and splitting on all of them would silently truncate it.
            var tab = line.IndexOf('\t');
            if (tab <= 0)
            {
                continue;
            }

            var name = line[..tab].Trim();
            var value = line[(tab + 1)..].Trim();

            if (name.Length == 0 || fields.Count >= MaxFields)
            {
                continue;
            }

            if (value.Length > MaxValueLength)
            {
                value = value[..MaxValueLength];
            }

            // A repeated name is MSSP's array notation — PORT and REFERRAL both arrive that way, and
            // REFERRAL is the whole basis of crawl discovery. Appended, never joined: a value may
            // contain a comma, so a joined string cannot be split back apart and is a fabrication
            // dressed as a measurement.
            if (!fields.TryGetValue(name, out var values))
            {
                values = [];
                fields[name] = values;
            }

            values.Add(value);
        }

        if (!started)
        {
            return null;
        }

        var report = new OrderedDictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in fields)
        {
            report[name] = values;
        }

        return report;
    }
}
