using System.Text.Json;
using System.Text.Json.Serialization;

namespace MUI.I3;

/// <summary>
/// One mud as the Intermud-3 router describes it, relayed by the gateway's <c>mudlist</c> method.
/// </summary>
/// <remarks>
/// <para>
/// The router hands this out unasked, as a delta stream keyed by mud name, and it is worth as much
/// as the counts are: <see cref="Address"/> and <see cref="PlayerPort"/> are an endpoint we did not
/// have to discover, <see cref="Services"/> says whether asking a <c>who</c> would be welcome, and
/// <see cref="Status"/> says whether the game is up before we spend a socket finding out.
/// </para>
/// <para>
/// <b>Every field here is the network's claim and not ours.</b> A mudlist entry is the remote mud's
/// own startup packet, forwarded — the same class of statement as an MSSP variable. It seeds work;
/// it does not become a measurement.
/// </para>
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
    /// The gateway spells this <c>host</c> over JSON-RPC and <c>address</c> in the state file it
    /// writes to disk. Both are accepted because a beta whose two representations already disagree
    /// with each other is not one to pin a single spelling to.
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
    /// <b>Carrying a version, which is the interesting part.</b> All 179 entries on the live network
    /// filled this in. It is still the mud's claim about itself relayed by a router — the same class
    /// of statement as an MSSP <c>CODEBASE</c> — so it is a cross-check and a seed, never a
    /// measurement.
    /// </remarks>
    [JsonPropertyName("driver")]
    public string Driver { get; init; } = "";

    /// <summary>The library on top of the driver: <c>WOTFlib 0.90</c>, <c>LuminariMUD</c>.</summary>
    [JsonPropertyName("mudlib")]
    public string Mudlib { get; init; } = "";

    /// <summary>The family: <c>CoffeeMud</c>, <c>LPMud</c>, <c>DikuMUD</c>, <c>Godwars</c>.</summary>
    [JsonPropertyName("mud_type")]
    public string MudType { get; init; } = "";

    /// <summary>
    /// The mud's own word for whether it is open — free text in practice, where 60 sampled muds
    /// produced 19 spellings.
    /// </summary>
    [JsonPropertyName("open_status")]
    public string OpenStatus { get; init; } = "";

    /// <summary>
    /// A contact address the mud published to the whole network, present on 125 of 179 entries.
    /// </summary>
    /// <remarks>
    /// <b>Published by them, to everyone, on purpose</b> — it is how I3 expects participants to be
    /// reachable, and it is already visible to every mud on the network and on the router's public
    /// web pages. That does not make it ours to put on a page: it is an owner-contact signal for
    /// §8's claim flow, not a field to render.
    /// </remarks>
    [JsonPropertyName("admin_email")]
    public string AdminEmail { get; init; } = "";

    /// <summary>Whether this mud advertises a service, by the mapping's own convention.</summary>
    /// <remarks>
    /// <b>This is the politeness gate and it is not decoration.</b> I3's services mapping is the
    /// network's own opt-in mechanism: a mud that does not list <c>who</c> has said it will not
    /// answer one, and asking anyway is the I3 equivalent of dialling a host that asked us not to.
    /// Of 60 online muds sampled off router.wotf.org, 59 advertise <c>who</c> — so this refuses
    /// almost nothing, which is exactly why there is no excuse for skipping it.
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
/// <b>Nothing here is persisted.</b> The name is on the wire because I3's who-reply is a list of
/// people rather than a figure, and it is read to be counted and then dropped — the same rule the
/// telnet <c>WHO</c> parser has always followed (spec §5.5, and "Never" in CLAUDE.md). The record
/// exists so the count has something to be the length of.
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
