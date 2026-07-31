namespace MUI.Crawl;

/// <summary>
/// What the option handshake revealed, gathered from TelnetNegotiationCore's own callbacks.
/// </summary>
/// <remarks>
/// <para>
/// This is layer 1, and it is <b>measured</b>: every entry here exists because a protocol plugin
/// actually fired, which only happens when the server negotiated that option. Nothing is parsed out
/// of the byte stream and nothing is inferred — the library already did the negotiating, so the
/// honest way to learn what a server supports is to be told by the thing that agreed it.
/// </para>
/// <para>
/// Contrast MSSP, where a game <em>claims</em> <c>GMCP 1</c>. Both go on the page; where they
/// disagree, that is the interesting fact (spec §6.1).
/// </para>
/// </remarks>
public sealed record Negotiation
{
    /// <summary>Protocols observed active, by name.</summary>
    public IReadOnlySet<string> Supported { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>The encoding in force, whether negotiated or defaulted.</summary>
    public string? Charset { get; init; }

    /// <summary>
    /// Whether CHARSET actually ran, as opposed to the interpreter simply having a default.
    /// </summary>
    /// <remarks>
    /// The distinction matters for the same reason everything else here does: an encoding is always
    /// present, so reading one off the interpreter proves nothing. Only the negotiation firing is a
    /// measurement of the server.
    /// </remarks>
    public bool CharsetNegotiated { get; init; }

    /// <summary>
    /// MCCP compression, and which version if it engaged.
    /// </summary>
    /// <remarks>
    /// <b>Currently always null, and that is a stopgap rather than a design position.</b> The probe
    /// declines MCCP because TelnetNegotiationCore negotiates MCCP2 and never inflates the stream
    /// (upstream issue #62), so accepting it loses the banner and the whole <c>WHO</c> reply to raw
    /// zlib decoded as text — 37% printable against 100% when declined, measured on
    /// <c>realms.reichel.net:4000</c>. The cost of declining is that we cannot see a server
    /// <em>offer</em> MCCP either, since the library only reports it on acceptance. The field stays
    /// because it is the right shape for the day #62 ships and this goes back on.
    /// </remarks>
    public int? CompressionVersion { get; init; }

    /// <summary>
    /// Environment variables the server asked for over NEW-ENVIRON — the MNES handshake.
    /// </summary>
    /// <remarks>
    /// One of the few places a server tells you about itself by asking a question. A server that
    /// asks for <c>CLIENT_NAME</c>, <c>CLIENT_VERSION</c> or <c>MTTS</c> is implementing MNES, and
    /// the set it asks for says which parts of it.
    /// </remarks>
    public IReadOnlyList<string> EnvironmentRequested { get; init; } = [];

    /// <summary>GMCP packages the server sent unprompted, which name what it is willing to talk about.</summary>
    public IReadOnlyList<string> GmcpPackages { get; init; } = [];

    /// <summary>True when the server drove a prompt marker — EOR or Suppress-Go-Ahead.</summary>
    public bool SendsPromptMarkers { get; init; }

    public bool Speaks(string protocol) => Supported.Contains(protocol);
}
