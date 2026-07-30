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

    /// <summary>Where an admin can read what we do and ask us to stop.</summary>
    public string InfoUrl { get; init; } = "https://muindex.example/crawler";
}
