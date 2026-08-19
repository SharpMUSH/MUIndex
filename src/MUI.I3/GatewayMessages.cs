using System.Text.Json;
using System.Text.Json.Serialization;

namespace MUI.I3;

/// <summary>
/// One mud as the Intermud-3 router describes it, relayed by the gateway's <c>mudlist</c> method.
/// </summary>
/// <remarks>
/// The router hands this out unasked, as a delta stream keyed by mud name: <see cref="Address"/> and
/// <see cref="PlayerPort"/> are an endpoint we didn't have to discover, <see cref="Services"/> says
/// whether a <c>who</c> would be welcome, and <see cref="Status"/> says whether it's worth a socket.
/// Every field is the network's claim, not ours — the same class of statement as an MSSP variable. It
/// seeds work; it does not become a measurement.
/// </remarks>
public sealed record I3Mud
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>
    /// The address the mud gave the router. <b>An IP literal in practice</b>, not the hostname a
    /// player would type, which is why binding one of these to a game we already know is a question
    /// for whatever owns identity rather than something this record answers.
    /// </summary>
    /// <remarks>
    /// The gateway spells this <c>host</c> over JSON-RPC and <c>address</c> in its on-disk state
    /// file; both are accepted since the two representations don't agree with each other.
    /// </remarks>
    [JsonPropertyName("host")]
    public string Host { get; init; } = "";

    [JsonPropertyName("address")]
    public string Address { private get; init; } = "";

    /// <summary>Whichever of the two spellings this payload used.</summary>
    public string HostAddress => string.IsNullOrEmpty(Host) ? Address : Host;

    /// <summary>
    /// Where a player would connect. <b>Zero is meaningful</b>: the spec permits it for a mud that is
    /// private or closed, and a participant that publishes 0 is telling us there is nothing to dial.
    /// MUIndex publishes 0 itself.
    /// </summary>
    [JsonPropertyName("port")]
    public int Port { get; init; }

    [JsonPropertyName("player_port")]
    public int PlayerPort { private get; init; }

    /// <summary>Whichever of the two spellings this payload used.</summary>
    public int PlayerPortNumber => Port != 0 ? Port : PlayerPort;

    /// <summary>
    /// What the mud will answer. The gateway drops zero-valued flags, so a service that is off is
    /// absent rather than present-and-false — see <see cref="Answers"/>.
    /// </summary>
    [JsonPropertyName("services")]
    public Dictionary<string, JsonElement> Services { get; init; } = new();

    /// <summary>The router's word for the mudlist entry's state: <c>up</c>, <c>down</c>, and so on.</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = "";

    /// <summary>
    /// The engine the mud runs, as it described itself at startup — <c>CoffeeMud v5.11.0.1</c>,
    /// <c>FluffOS v2.23-ds03</c>, <c>DGD 1.4.1</c>, <c>CircleMUD</c>.
    /// </summary>
    /// <remarks>
    /// Still the mud's own claim relayed by a router — the same class of statement as an MSSP
    /// <c>CODEBASE</c> — so it is a cross-check and a seed, never a measurement.
    /// </remarks>
    [JsonPropertyName("driver")]
    public string Driver { get; init; } = "";

    /// <summary>The library on top of the driver: <c>WOTFlib 0.90</c>, <c>LuminariMUD</c>.</summary>
    [JsonPropertyName("mudlib")]
    public string Mudlib { get; init; } = "";

    /// <summary>The family: <c>CoffeeMud</c>, <c>LPMud</c>, <c>DikuMUD</c>, <c>Godwars</c>.</summary>
    [JsonPropertyName("mud_type")]
    public string MudType { get; init; } = "";

    /// <summary>The mud's own word for whether it is open — free text in practice.</summary>
    [JsonPropertyName("open_status")]
    public string OpenStatus { get; init; } = "";

    /// <summary>A contact address the mud published to the whole network, often empty.</summary>
    /// <remarks>
    /// Published by them, to everyone, already visible on the router's own public pages — that
    /// doesn't make it ours to render. It's an owner-contact signal for §8's claim flow, not a field
    /// to display.
    /// </remarks>
    [JsonPropertyName("admin_email")]
    public string AdminEmail { get; init; } = "";

    /// <summary>Whether this mud advertises a service, by the mapping's own convention.</summary>
    /// <remarks>
    /// The politeness gate, not decoration: I3's services mapping is the network's own opt-in — a
    /// mud that doesn't list <c>who</c> has said it won't answer one, and asking anyway is the I3
    /// equivalent of dialling a host that asked us not to.
    /// </remarks>
    public bool Answers(string service) =>
        Services.TryGetValue(service, out var value)
        && value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt32(out var n) && n > 0,
            JsonValueKind.True => true,
            _ => false,
        };

    /// <summary>Whether the router currently believes this mud is up.</summary>
    public bool IsUp => string.Equals(Status, "up", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One user in a <c>who-reply</c>, as the remote mud chose to describe them.</summary>
/// <remarks>
/// Nothing here is persisted — read to be counted, then dropped, the same rule the telnet
/// <c>WHO</c> parser follows (spec §5.5; "Never" in CLAUDE.md).
/// </remarks>
public sealed record I3User
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>Seconds idle. The spec's <c>-1</c> means the user is not currently logged in.</summary>
    [JsonPropertyName("idle")]
    public int Idle { get; init; }
}

/// <summary>A <c>who_reply</c> event: some mud on the network enumerated its users for us.</summary>
public sealed record I3WhoReply
{
    [JsonPropertyName("from_mud")]
    public string FromMud { get; init; } = "";

    [JsonPropertyName("users")]
    public IReadOnlyList<I3User> Users { get; init; } = [];
}
