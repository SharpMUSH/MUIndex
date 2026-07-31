namespace MUI.Crawl;

/// <summary>Tuning for a single probe. Every bound here exists because a stranger chose the input.</summary>
public sealed record ProbeOptions
{
    /// <summary>How long the whole session may take before it is abandoned.</summary>
    /// <remarks>
    /// A hard bound, not a courtesy. The crawler runs in-process with the web tier, so a probe that
    /// wedges against a black-holed host would otherwise starve request threads (spec §12).
    /// </remarks>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>How long to wait for the connect screen before giving up on a banner.</summary>
    public TimeSpan BannerQuietPeriod { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Ceiling on a single subnegotiation payload, handed to TelnetNegotiationCore's
    /// <c>WithMaxMessageSize</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately well below the library's 1 MiB default. A real MSSP report is a few kilobytes —
    /// the specification's whole official vocabulary is 45 variables — so 128 KiB is roughly two
    /// orders of magnitude of headroom for anything legitimate, while bounding what a hostile or
    /// broken server can make us allocate. A crawler connects to servers it does not trust by
    /// definition, which is precisely the case the library's own release notes call out.
    /// </para>
    /// <para>
    /// At the ceiling the report is <b>dropped, not truncated</b>, and surfaces as
    /// <see cref="MsspOutcome.RejectedTooLarge"/>. That must never be recorded as an absent report.
    /// </para>
    /// </remarks>
    public int MaxSubnegotiationBytes { get; init; } = 128 * 1024;

    /// <summary>
    /// What the crawler calls itself over TTYPE/MTTS and MNES <c>CLIENT_NAME</c> (spec §11).
    /// </summary>
    /// <remarks>
    /// An admin reading their logs must be able to find out who we are and how to opt out, so this
    /// is a politeness obligation rather than a cosmetic string. It carries a URL for the same
    /// reason.
    /// </remarks>
    public IReadOnlyList<string> TerminalTypes { get; init; } =
        ["MUINDEX-CRAWLER", "MUINDEX", "MTTS 9"];

    /// <summary>
    /// Telnet options to request outright with <c>IAC DO</c> rather than waiting to be offered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Waiting is not enough.</b> The MSSP specification says a server "should send
    /// <c>IAC WILL MSSP</c>" on connect, so a listen-only crawler assumes it will hear one. Many
    /// servers that support MSSP never volunteer anything and answer only when asked, which is why
    /// the protocol's own reference client (TinTin++'s <c>#session mssp</c>) asks rather than listens.
    /// </para>
    /// <para>
    /// Asking also makes a negative answer meaningful. Aardwolf, measured directly, offers MCCP1/2,
    /// ATCP, GMCP and its own option 102, and requests TTYPE and NAWS — but does not answer
    /// <c>IAC DO MSSP</c> at all. Because we asked, "no MSSP" there is a measurement rather than an
    /// assumption we never tested.
    /// </para>
    /// <para>
    /// This is negotiation, not traffic: <c>IAC DO</c> is the client half of the option handshake and
    /// is the only category of byte this probe puts on the wire beyond the pre-login <c>WHO</c>. Only
    /// MSSP is requested, because MSSP is the one option that exists specifically to be asked for by
    /// crawlers — the rest are observed if a server offers them and left alone if it does not.
    /// </para>
    /// </remarks>
    public IReadOnlyList<byte> RequestOptions { get; init; } = [MsspOption];

    /// <summary>The MSSP telnet option, 70.</summary>
    public const byte MsspOption = 70;

    /// <summary>Where an admin can read what we do and ask us to stop.</summary>
    public string InfoUrl { get; init; } = "https://muindex.example/crawler";
}
