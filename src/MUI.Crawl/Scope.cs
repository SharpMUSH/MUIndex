using System.Net;
using System.Net.Sockets;

namespace MUI.Crawl;

/// <summary>Why a dial was allowed or refused (spec §7.2).</summary>
public enum HostScopeRuling
{
    /// <summary>Every resolved address is globally routable.</summary>
    Allowed,

    /// <summary>
    /// At least one resolved address is not globally routable. Distinct from
    /// <see cref="Unresolvable"/> because "we looked it up and will not go there" is a decision of
    /// ours, while a DNS failure is a fact about the world.
    /// </summary>
    RefusedNonGlobal,

    /// <summary>The name did not resolve. An ordinary DNS failure, not a refusal.</summary>
    Unresolvable,
}

/// <summary>A ruling plus what it was based on, so a refusal can be explained without re-resolving.</summary>
public sealed record HostScopeDecision(
    HostScopeRuling Ruling,
    IReadOnlyList<IPAddress> Addresses,
    string? Detail = null)
{
    public bool IsAllowed => Ruling is HostScopeRuling.Allowed;
}

/// <summary>Resolves a host name to addresses. Abstracted so no test needs live DNS.</summary>
public interface IHostResolver
{
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken = default);
}

/// <summary>
/// Decides whether the crawler may open a socket to a host.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gate is on the resolved address, not the name.</b> Refusing <c>10.0.0.5</c> and
/// <c>localhost</c> by inspection is not enough: <c>games.example.com</c> with an A record pointing
/// at <c>127.0.0.1</c> or <c>169.254.169.254</c> passes a name check and the socket goes somewhere it
/// must never go. <c>REFERRAL</c> is attacker-controlled, so this is reachable by anyone willing to
/// put a line in their own MSSP.
/// </para>
/// <para>
/// <b>Known limitation.</b> This is a time-of-check-to-time-of-use gap — the name is resolved, then
/// connected by name, so an answer that changes in between is not caught. The fix is to connect to
/// the pinned address that was checked. Caching resolutions would widen the window rather than close
/// it, which is why there is no cache here. Do not restate this guard as airtight: it raises the cost
/// of the attack, it does not close it.
/// </para>
/// </remarks>
public interface IHostScopeGuard
{
    Task<HostScopeDecision> InspectAsync(string host, CancellationToken cancellationToken = default);
}

/// <summary>Address classification, shared by the guard and by referral parsing.</summary>
public static class AddressScope
{
    /// <summary>
    /// Whether an address is one the crawler may dial. Everything private, loopback, link-local,
    /// multicast or otherwise special is refused — including IPv6 unique-local and the IPv4-mapped
    /// forms of all of the above, which are the obvious bypass.
    /// </summary>
    public static bool IsGloballyRoutable(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        if (address.AddressFamily is AddressFamily.InterNetworkV6)
        {
            return !address.IsIPv6LinkLocal
                && !address.IsIPv6SiteLocal
                && !address.IsIPv6Multicast
                && !IsIPv6UniqueLocal(address)
                && !address.Equals(IPAddress.IPv6Any);
        }

        var octets = address.GetAddressBytes();
        return octets[0] switch
        {
            0 => false,                                     // "this network"
            10 => false,                                    // RFC 1918
            127 => false,                                   // loopback
            169 when octets[1] == 254 => false,             // link-local, incl. 169.254.169.254
            172 when octets[1] >= 16 && octets[1] <= 31 => false,
            192 when octets[1] == 168 => false,
            192 when octets[1] == 0 && octets[2] == 0 => false,
            100 when octets[1] >= 64 && octets[1] <= 127 => false, // CGNAT
            >= 224 => false,                                // multicast and reserved
            _ => true,
        };
    }

    private static bool IsIPv6UniqueLocal(IPAddress address) =>
        (address.GetAddressBytes()[0] & 0xFE) == 0xFC;
}
