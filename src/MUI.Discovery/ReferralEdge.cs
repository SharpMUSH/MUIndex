namespace MUI.Discovery;

/// <summary>
/// One game naming another in its MSSP <c>REFERRAL</c> field.
/// </summary>
/// <remarks>
/// <para>
/// Discovery is how a game is found and never how it is scheduled (spec §7.1). Anything that ever
/// answered becomes a crawl target with its own schedule, so an edge going away costs nothing —
/// it sets <see cref="Present"/> to <c>false</c> and the referred game carries on being probed on
/// its own account, forever.
/// </para>
/// <para>
/// Edges are kept for two other reasons: <c>REFERRAL</c> is attacker-controllable, so recording who
/// referred whom lets a poisoned source's whole subtree be traced and pruned (spec §7.2); and the
/// graph is worth rendering.
/// </para>
/// </remarks>
public sealed record ReferralEdge(
    Guid FromGameId,
    string ToHost,
    int ToPort,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    bool Present);
