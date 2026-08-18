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
/// <b>Do not delete this as redundant with <see cref="ReferralCandidate.IsCrawlable"/>.</b> That
/// check classifies a <em>string</em>, and every DNS name passes it — correctly, since nothing can be
/// known about a name until DNS answers. A <c>REFERRAL</c> naming <c>internal.example.org</c>, with
/// an A record pointing at <c>10.0.0.5</c> or <c>169.254.169.254</c>, passes every string-level
/// check, so the scope check has to happen after resolution.
/// </para>
/// <para>
/// <b>Any non-global address refuses the whole target</b>, not just the bad one and not the first
/// found: a name resolving to one public and one private address is the DNS-rebinding shape, and
/// proceeding on the half we liked is a coin flip lost the moment DNS reorders.
/// </para>
/// <para>
/// <see cref="HostScopeRuling.Unresolvable"/> (an ordinary DNS failure, ordinary backoff) and
/// <see cref="HostScopeRuling.RefusedNonGlobal"/> (a decision of ours) must stay distinct facts —
/// collapsing them makes our own policy indistinguishable from the world's behaviour downstream.
/// </para>
/// <para>
/// <b>A refusal is not a <see cref="ProbeOutcome"/>, and there is deliberately no <c>Refused</c>
/// member to reach for.</b> This guard runs before a <see cref="ProbeResult"/> exists, so there is no
/// honest route by which a refusal could produce an availability row. Do not dress it as
/// <c>ProbeResult.Failed(refused, …)</c> either: <see cref="MUI.Catalog.FailureCause.Refused"/>
/// already means the far end sent an RST — a real measurement of a real host — and conflating the two
/// writes our own security policy into a game's public reachability history. Count a refusal on the
/// crawl cycle instead.
/// </para>
/// <para>
/// <b>Known limitation, stated rather than implied</b> (§7.2's own words). This is a
/// time-of-check-to-time-of-use gap: the name is resolved here, then connected by name, so a DNS
/// answer that changes in between is not caught. The fix is to connect to the pinned
/// <see cref="IPAddress"/> in <see cref="HostScopeDecision.Addresses"/> rather than re-resolving the
/// host string. Caching resolutions would <em>widen</em> this window, so there is no cache here and
/// the crawler resolves per dial. <b>Do not restate this guard as airtight; it raises the cost of the
/// attack, it does not close it.</b>
/// </para>
/// <para>
/// The range checks themselves belong to <see cref="AddressScope.IsGloballyRoutable"/> — not
/// duplicated here.
/// </para>
/// </remarks>
public sealed class HostScopeGuard(IHostResolver resolver) : IHostScopeGuard
{
    /// <summary>
    /// The explanation attached to a dial allowed by <see cref="CrawlTarget.IsOperatorSeed"/>.
    /// </summary>
    /// <remarks>
    /// An exemption and an ordinary allow are both <see cref="HostScopeRuling.Allowed"/> — the answer
    /// to "may we dial" is the same — but an operator reading a log needs to see which happened. This
    /// detail carries that without a fourth ruling every caller would have to handle.
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
    /// "Operator-supplied seeds may be exempted, and nothing else may" (§7.2) — see
    /// <see cref="CrawlTarget.IsOperatorSeed"/>. Short-circuits: there is nothing to verify about an
    /// address a human typed, and no lookup worth making.
    /// </remarks>
    public Task<HostScopeDecision> RuleOnAsync(CrawlTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        return target.IsOperatorSeed
            ? Task.FromResult(new HostScopeDecision(HostScopeRuling.Allowed, [], OperatorSeedDetail))
            : InspectAsync(target.Host, cancellationToken);
    }
}
