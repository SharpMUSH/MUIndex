using System.Net;
using MUI.Crawl;

namespace MUI.Discovery;

/// <summary>The real resolver. Injected so no test in this suite performs a live lookup.</summary>
public sealed class SystemHostResolver : IHostResolver
{
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken = default)
    {
        if (IPAddress.TryParse(host, out var literal))
        {
            // A literal needs no lookup, and asking DNS about one invites a resolver to answer
            // something else entirely.
            return [literal];
        }

        return await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Spec §7.2, "the gate is on the resolved address, not the name".
/// </summary>
/// <remarks>
/// <para>
/// <b>Do not delete this as redundant with <see cref="ReferralCandidate.IsCrawlable"/>. It is not.</b>
/// That check classifies a <em>string</em>, and every DNS name passes it — correctly, because nothing
/// can be known about a name until DNS answers. The consequence is that the literal-address checks are
/// worth nothing against an attacker who owns a domain: a <c>REFERRAL</c> naming
/// <c>internal.example.org</c>, with an A record pointing at <c>10.0.0.5</c> or
/// <c>169.254.169.254</c>, passes every check the referral writer makes. Publishing that record costs
/// an attacker nothing, so a DNS name is the cheapest bypass of the one gate §7.2 exists to provide.
/// The check has to happen after resolution, and a referral string never sees one.
/// </para>
/// <para>
/// <b>Any non-global address refuses the whole target.</b> Not the first one found, and the good ones
/// are not filtered out and used: a name resolving to one public and one private address is the
/// DNS-rebinding shape, proceeding on the half we liked is a coin flip lost the moment DNS reorders,
/// and a mixed answer is itself evidence of intent.
/// </para>
/// <para>
/// <b>"Could not resolve" and "resolved somewhere we won't go" are different facts.</b>
/// <see cref="HostScopeRuling.Unresolvable"/> is an ordinary DNS failure that gets ordinary backoff;
/// <see cref="HostScopeRuling.RefusedNonGlobal"/> is a decision of ours. Collapsing them would make our
/// own policy indistinguishable from the world's behaviour in every downstream reading.
/// </para>
/// <para>
/// <b>A refusal is not a <c>ProbeOutcome</c>, and there is deliberately no <c>Refused</c> member to
/// reach for.</b> <see cref="ProbeOutcome"/> has exactly two members, <c>Answered</c> and
/// <c>Failed</c>, and <em>both mean the socket was opened</em>. This guard runs <b>before</b> a
/// <see cref="ProbeResult"/> exists at all, so there is no honest route by which a refusal could
/// produce an availability row — which is how §7.2's "a refusal writes no availability sample" is
/// satisfied structurally rather than by a check somebody has to remember. <b>The tempting shortcut is
/// <c>ProbeResult.Failed(refused, …)</c>, and it is wrong twice:</b>
/// <see cref="MUI.Catalog.FailureCause.Refused"/> means the far end sent an RST — a real measurement of
/// a real host — so dressing a policy refusal as a probe failure makes the two permanently inseparable
/// downstream, and it writes our own security policy into a game's public reachability history, which
/// is exactly what §7.2 forbids. Count a refusal on the crawl cycle instead. Do not add the enum
/// member; do not manufacture a <see cref="ProbeResult"/> here.
/// </para>
/// <para>
/// <b>Known limitation, stated rather than implied</b> (§7.2's own words). This is a
/// time-of-check-to-time-of-use gap: the name is resolved here, then connected by name, so a DNS answer
/// that changes in between is not caught. The fix is to connect to the pinned
/// <see cref="IPAddress"/> in <see cref="HostScopeDecision.Addresses"/> rather than re-resolving the
/// host string — a transport change, and worth doing. Caching resolutions would <em>widen</em> this
/// window, so there is no cache here and the crawler resolves per dial. <b>Do not restate this guard
/// as airtight; it raises the cost of the attack, it does not close it.</b>
/// </para>
/// <para>
/// The range checks themselves are <see cref="AddressScope.IsGloballyRoutable"/>'s. Writing a second
/// copy here would be two sets of rules that must agree for ever, and the day they disagree is the day
/// one of them is wrong about <c>169.254.169.254</c>.
/// </para>
/// </remarks>
public sealed class HostScopeGuard(IHostResolver resolver) : IHostScopeGuard
{
    /// <summary>
    /// The explanation attached to a dial allowed by <see cref="CrawlTarget.IsOperatorSeed"/>.
    /// </summary>
    /// <remarks>
    /// An exemption and an ordinary allow are both <see cref="HostScopeRuling.Allowed"/> because the
    /// answer to "may we dial" is the same, but they are not the same event, and an operator reading a
    /// log needs to see which happened. The detail carries that without a fourth ruling that every
    /// caller would then have to handle.
    /// </remarks>
    public const string OperatorSeedDetail = "operator seed: exempt from the resolved-address gate; no lookup performed";

    /// <summary>
    /// Rules on a bare host name. Every address must be globally routable; there is no exemption on
    /// this path, because a bare string carries no evidence that a human chose it.
    /// </summary>
    public async Task<HostScopeDecision> InspectAsync(string host, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);

        IReadOnlyList<IPAddress> addresses;
        try
        {
            addresses = await resolver.ResolveAsync(host, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Fail closed. A guard that allows a dial because DNS was briefly unhappy is not a guard.
            return new HostScopeDecision(HostScopeRuling.Unresolvable, [], error.Message);
        }

        if (addresses.Count == 0)
        {
            return new HostScopeDecision(HostScopeRuling.Unresolvable, [], $"{host} resolved to no addresses");
        }

        // Every address, not the first: the answer as a whole has to be clean.
        foreach (var address in addresses)
        {
            if (!AddressScope.IsGloballyRoutable(address))
            {
                return new HostScopeDecision(
                    HostScopeRuling.RefusedNonGlobal,
                    addresses,
                    $"{host} resolved to {address}, which is not globally routable");
            }
        }

        return new HostScopeDecision(HostScopeRuling.Allowed, addresses);
    }

    /// <summary>
    /// Rules on a registry target, honouring <see cref="CrawlTarget.IsOperatorSeed"/>.
    /// </summary>
    /// <remarks>
    /// "Operator-supplied seeds may be exempted, and nothing else may" (§7.2). The exemption is a
    /// stored property defaulting to <em>not</em> exempt, never inferred, and never granted by a
    /// referral or an import — so the dangerous paths are guarded by not having to remember to guard
    /// them. It short-circuits: there is nothing to verify about an address a human typed, and no
    /// lookup worth making.
    /// </remarks>
    public Task<HostScopeDecision> RuleOnAsync(CrawlTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        return target.IsOperatorSeed
            ? Task.FromResult(new HostScopeDecision(HostScopeRuling.Allowed, [], OperatorSeedDetail))
            : InspectAsync(target.Host, cancellationToken);
    }
}
