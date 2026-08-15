using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Discovery;

namespace MUI.Web.Accounts;

/// <summary>What an owner's opt-out request did, and to how many of their addresses.</summary>
public enum OwnerOptOutVerdict
{
    Applied,

    /// <summary>Nobody with a verified claim on this game asked. Nothing was written.</summary>
    NotAnOwner,

    /// <summary>
    /// The game has no endpoint on record, so there is no address to stop dialling.
    /// </summary>
    /// <remarks>
    /// Said out loud rather than reported as success. An opt-out that silently covered nothing is
    /// the worst possible answer to this particular request: the owner believes we have stopped.
    /// </remarks>
    NoAddresses,
}

/// <summary>The verdict and the addresses it covered.</summary>
public sealed record OwnerOptOutOutcome(OwnerOptOutVerdict Verdict, int Addresses)
{
    public static readonly OwnerOptOutOutcome NotAnOwner = new(OwnerOptOutVerdict.NotAnOwner, 0);

    public static readonly OwnerOptOutOutcome NoAddresses = new(OwnerOptOutVerdict.NoAddresses, 0);

    public bool IsApplied => Verdict is OwnerOptOutVerdict.Applied;
}

/// <summary>
/// §11's opt-out, asked for by the one person who does not have to be taken on trust (spec §8.5).
/// </summary>
/// <remarks>
/// <para>
/// §11 gives three routes and the site shipped two of them reachable: MSSP and DNS are answered by
/// the game itself, and the third — a recorded request — could only be entered with the crawler CLI.
/// That left the operator who had already proved they run the game unable to ask through the
/// interface built for them, while the two self-serve routes required editing a config file or a
/// zone. This is that third route, for a caller whose claim is verified.
/// </para>
/// <para>
/// <b>Scoped to the game's own listeners, one record per address, never to the whole host.</b> This
/// is the part worth being careful about. <see cref="CrawlOptOut"/> takes a null port to mean every
/// port on a host, and a shared machine — a hosting provider, a hobbyist running four games on one
/// box — would then have three other games delisted by one owner's button. An owner may speak for
/// the ports their game answers on and for nothing else, so the addresses come from the endpoints we
/// measured for that game and each gets its own row.
/// </para>
/// <para>
/// The detail records who asked, as <see cref="OptOutSource.Request"/> requires. It is a real claim
/// rather than a defaulted one: the account named held a verified claim on the game at the moment
/// the button was pressed, which is exactly what the <c>ContactedMaintainer</c> defect in this
/// repository's history did not have.
/// </para>
/// </remarks>
public sealed class OwnerOptOut(
    IGameQueries queries,
    OwnerEnrichment ownership,
    OptOutGate gate,
    ICrawlOptOutRepository optOuts)
{
    /// <summary>Whether we are currently honouring an opt-out for this game, and by which route.</summary>
    /// <remarks>
    /// Reads every address rather than the first: a game whose TLS port was opted out and whose plain
    /// port was not is in neither state, and a control rendered off one of the two would be lying
    /// about the other. <see cref="OwnerOptOutState.Partial"/> is that case, and it is shown rather
    /// than rounded to the friendlier neighbour.
    /// </remarks>
    public async Task<OwnerOptOutState> StateAsync(Guid gameId, CancellationToken cancellationToken = default)
    {
        var addresses = await AddressesAsync(gameId, cancellationToken);

        if (addresses.Count == 0)
        {
            return new OwnerOptOutState([], [], []);
        }

        List<string> stopped = [];
        List<string> dialling = [];
        List<CrawlOptOut> standing = [];

        foreach (var (host, port) in addresses)
        {
            if (await optOuts.StandingAsync(host, port, cancellationToken) is { } rule)
            {
                stopped.Add($"{host}:{port}");
                standing.Add(rule);
            }
            else
            {
                dialling.Add($"{host}:{port}");
            }
        }

        return new OwnerOptOutState(stopped, dialling, standing);
    }

    /// <summary>Stops dialling every address this game answers on, or starts again.</summary>
    public async Task<OwnerOptOutOutcome> SetAsync(
        Guid gameId,
        Guid userId,
        bool stop,
        string askedBy,
        CancellationToken cancellationToken = default)
    {
        if (!await ownership.OwnsAsync(gameId, userId, cancellationToken))
        {
            return OwnerOptOutOutcome.NotAnOwner;
        }

        var addresses = await AddressesAsync(gameId, cancellationToken);

        if (addresses.Count == 0)
        {
            return OwnerOptOutOutcome.NoAddresses;
        }

        foreach (var (host, port) in addresses)
        {
            if (stop)
            {
                await gate.RecordRequestAsync(
                    host,
                    port,
                    $"the game's verified owner ({askedBy}) asked through the owner dashboard",
                    cancellationToken);
            }
            else
            {
                await gate.WithdrawRequestAsync(host, port, cancellationToken);
            }
        }

        return new OwnerOptOutOutcome(OwnerOptOutVerdict.Applied, addresses.Count);
    }

    /// <summary>
    /// The addresses this game answers on, deduplicated.
    /// </summary>
    /// <remarks>
    /// Read from the endpoints we measured rather than from anything the owner typed, so that the
    /// button can only ever cover addresses this crawler has actually dialled for this game.
    /// </remarks>
    private async Task<IReadOnlyList<(string Host, int Port)>> AddressesAsync(
        Guid gameId,
        CancellationToken cancellationToken)
    {
        if (await queries.FindAsync(gameId, cancellationToken) is not { } page)
        {
            return [];
        }

        return [.. page.Endpoints
            .Select(e => (e.Host, e.Port))
            .Distinct()];
    }
}

/// <summary>
/// Which of a game's addresses we are honouring an opt-out for.
/// </summary>
/// <param name="Stopped">Addresses we are not dialling, formatted for a reader.</param>
/// <param name="Dialling">Addresses we still are.</param>
/// <param name="Standing">The rules behind <paramref name="Stopped"/>, which say by which route.</param>
public sealed record OwnerOptOutState(
    IReadOnlyList<string> Stopped,
    IReadOnlyList<string> Dialling,
    IReadOnlyList<CrawlOptOut> Standing)
{
    public bool Any => Stopped.Count > 0 || Dialling.Count > 0;

    public bool AllStopped => Stopped.Count > 0 && Dialling.Count == 0;

    /// <summary>Some addresses stopped and some not, which is a state and not a rounding error.</summary>
    public bool Partial => Stopped.Count > 0 && Dialling.Count > 0;

    /// <summary>
    /// Whether every standing rule came from a recorded request, and so can be taken back here.
    /// </summary>
    /// <remarks>
    /// An MSSP field or a TXT record is the game still saying stop on every probe. The dashboard does
    /// not offer to overrule it: the way to take one of those back is to stop publishing it, and a
    /// button here that appeared to would be claiming an authority this side does not have.
    /// </remarks>
    public bool WithdrawableHere =>
        Stopped.Count > 0 && Standing.All(rule => rule.Source is OptOutSource.Request);
}
