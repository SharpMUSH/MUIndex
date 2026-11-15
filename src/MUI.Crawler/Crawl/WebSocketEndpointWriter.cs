using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawl;
using MUI.Discovery;

using Microsoft.Extensions.Logging;

namespace MUI.Crawler;

/// <summary>
/// Turns the WebSocket addresses a game declared in MSSP into endpoints, once each has answered.
/// </summary>
/// <remarks>
/// <para>
/// <b>Declared, then measured, then written.</b> <c>WEBSOCKET</c> stays an MSSP field like any other
/// (the ingestor stores it verbatim); this writes an endpoint only for an address where a handshake
/// completed, so a game page lists a way in that worked, not one that was claimed (rule 1). A failed
/// handshake writes nothing, here or anywhere: an address that did not upgrade from our vantage
/// point is not shown, and nothing says it is down (rule 5).
/// </para>
/// <para>
/// <b>Every address is server text</b>, so each goes through the same two gates as a dial of the
/// crawl loop's own: the opt-out (§11) first, then the scope guard on the resolved addresses
/// (§7.2), whose answer is what the probe dials. There is no operator-seed exemption on this path.
/// </para>
/// <para>
/// <b>An address belongs to one game, and keeps its kind.</b> <c>(host, port)</c> is the identity
/// signal (§7.3), so a declaration naming an address another game holds is ignored rather than
/// written, and one naming a telnet or TLS endpoint of this game is ignored rather than turning it
/// into a WebSocket — that address answered telnet, which a declaration cannot overrule.
/// </para>
/// </remarks>
public sealed class WebSocketEndpointWriter(
    IWebSocketProbe probe,
    OptOutGate optOut,
    IHostScopeGuard scope,
    IEndpointStore endpoints,
    TimeProvider time,
    ILogger<WebSocketEndpointWriter>? logger = null)
{
    /// <summary>
    /// Probes each address <paramref name="result"/> declared and writes the ones that answered.
    /// </summary>
    /// <returns>How many endpoints were written.</returns>
    /// <remarks>
    /// Never throws for one address: a WebSocket endpoint is a detail of a game whose measurement has
    /// already been stored, and must not cost the crawl loop the schedule it writes after this.
    /// </remarks>
    public async Task<int> ApplyAsync(Guid gameId, ProbeResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        var written = 0;

        foreach (var address in WebSocketAddresses.From(result.Mssp))
        {
            try
            {
                if (await ApplyOneAsync(gameId, address, cancellationToken))
                {
                    written++;
                }
            }
            catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger?.LogWarning(error, "WebSocket address {Address} declared by {Host}:{Port} threw", address.Uri, result.Host, result.Port);
            }
        }

        return written;
    }

    private async Task<bool> ApplyOneAsync(Guid gameId, WebSocketAddress address, CancellationToken cancellationToken)
    {
        if (await optOut.RuleOnAsync(address.Host, address.Port, cancellationToken) is not null)
        {
            logger?.LogInformation("WebSocket address {Address} has opted out; not dialled", address.Uri);
            return false;
        }

        var existing = await endpoints.ByAddressAsync(address.Host, address.Port, cancellationToken);

        if (existing is not null && (existing.GameId != gameId || !existing.IsWebSocket))
        {
            logger?.LogInformation(
                "WebSocket address {Address} is already a {Kind} endpoint of game {Game}; left as it is",
                address.Uri, existing.Kind, existing.GameId);
            return false;
        }

        var decision = await scope.InspectAsync(address.Host, cancellationToken);

        if (!decision.IsAllowed)
        {
            logger?.LogInformation("WebSocket address {Address} refused by scope: {Detail}", address.Uri, decision.Detail);
            return false;
        }

        var handshake = await probe.HandshakeAsync(address, decision.Addresses, cancellationToken);

        if (!handshake.Answered)
        {
            logger?.LogInformation("WebSocket address {Address} did not upgrade: {Detail}", address.Uri, handshake.Detail);
            return false;
        }

        var now = time.GetUtcNow();

        await endpoints.UpsertAsync(
            new GameEndpoint(
                gameId,
                address.Host,
                address.Port,
                address.Tls ? EndpointKind.SecureWebSocket : EndpointKind.WebSocket,
                existing?.FirstSeenAt ?? now,
                now,
                EndpointState.Active,
                address.Path,
                handshake.SubProtocol),
            cancellationToken);

        return true;
    }
}
