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

    /// <summary>
    /// The MCP packages the server advertised, sorted. Empty when it speaks no MCP, and also when it
    /// offered MCP and then listed nothing.
    /// </summary>
    /// <remarks>
    /// A capability fingerprint no other probe yields, and a codebase tell besides: the
    /// <c>org-fuzzball-*</c> family names Fuzzball as surely as a version banner does. Measured
    /// against the catalogue before this was built -- every game that offers MCP answers a reply with
    /// its whole list, before login and without an account.
    /// </remarks>
    public IReadOnlyList<string> McpPackages { get; init; } = [];

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

    /// <summary>
    /// Raw MSDP messages the server sent back, one JSON-serialized <c>MSDP_VAR</c>/<c>MSDP_VAL</c>
    /// payload per message TelnetNegotiationCore delivered to <c>OnMSDPMessage</c>.
    /// </summary>
    /// <remarks>
    /// This is evidence, not a measurement of anything in particular. <c>MSDPLibrary.MSDPScan</c> can
    /// hand back any of MSDP's data shapes — a scalar, an array, a table, or nesting of those — and
    /// unpacking that into a typed field the rest of the codebase could read as "the game reported N
    /// players" is exactly what CLAUDE.md's fourth rule (parsers never fabricate) forbids until a real
    /// server is found doing it. <c>docs/codebase-survey-2026-07-30.md</c> records a negative result
    /// across every pre-login server sampled: nothing offered a player-count variable to <c>SEND</c>.
    /// <b>Re-verified against the real <c>SendMSDPCommand</c> API</b> (TelnetNegotiationCore PR #84,
    /// v2.8.3) on <c>empiremud.net:4000</c> and <c>exile.lurker.net:4000</c>, both of which negotiate
    /// MSDP: neither answers <c>SEND PLAYERS</c> with a <c>PLAYERS</c> value. Each instead sends one
    /// unsolicited <c>{"SERVER_ID":"…"}</c> message — an auto-report on negotiation, not a reply to
    /// our request — which lands here as ordinary evidence rather than being mistaken for an answer.
    /// </remarks>
    public IReadOnlyList<string> MsdpMessages { get; init; } = [];

    /// <summary>True when the server drove a prompt marker — EOR or Suppress-Go-Ahead.</summary>
    public bool SendsPromptMarkers { get; init; }

    public bool Speaks(string protocol) => Supported.Contains(protocol);
}
