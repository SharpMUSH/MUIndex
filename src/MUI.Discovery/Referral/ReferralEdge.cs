namespace MUI.Discovery;

/// <summary>
/// One game naming another in its MSSP <c>REFERRAL</c> field.
/// </summary>
/// <remarks>
/// Discovery is how a game is found, never how it is scheduled (spec §7.1): an edge going away just
/// sets <see cref="Present"/> to <c>false</c>, and the referred game carries on being probed on its
/// own account forever. Edges are kept because <c>REFERRAL</c> is attacker-controllable, so recording
/// who referred whom lets a poisoned source's whole subtree be traced and pruned (spec §7.2).
/// </remarks>
public sealed record ReferralEdge(
    Guid FromGameId,
    string ToHost,
    int ToPort,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    bool Present);
