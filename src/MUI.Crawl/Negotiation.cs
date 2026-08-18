namespace MUI.Crawl;

/// <summary>
/// What the option handshake revealed, gathered from TelnetNegotiationCore's own callbacks.
/// </summary>
/// <remarks>
/// This is <b>measured</b>, not parsed or inferred: an entry exists only because a protocol plugin
/// actually fired during negotiation. Contrast MSSP, where a game merely <em>claims</em>
/// <c>GMCP 1</c> — both are shown, and where they disagree, that disagreement is the interesting
/// fact (spec §6.1).
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

    /// <summary>MCCP compression, and which version, when the server negotiated it.</summary>
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
