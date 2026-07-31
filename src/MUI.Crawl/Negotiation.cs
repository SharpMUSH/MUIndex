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
    /// Currently always null: the probe declines MCCP outright. A crawler reads a few kilobytes per
    /// probe so compression buys it nothing, and accepting it today loses the payload entirely —
    /// TelnetNegotiationCore negotiates MCCP2 without inflating the stream (upstream issue #62).
    /// Kept on the record because the field is the right shape for when that is fixed.
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
