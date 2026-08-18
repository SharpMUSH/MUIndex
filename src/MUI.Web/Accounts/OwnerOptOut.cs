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
    /// <remarks>Reported rather than silently treated as success — the owner must not believe we stopped when there was nothing to stop.</remarks>
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
/// The third of §11's three opt-out routes (MSSP and DNS are the other two, answered by the game
/// itself) — for a caller whose ownership claim is verified.
/// <b>Scoped to the game's own listeners, one record per address, never to the whole host.</b>
/// <see cref="CrawlOptOut"/> takes a null port to mean every port on a host, and a shared machine
/// would then have unrelated games delisted by one owner's button — so addresses come from the
/// endpoints measured for that specific game, each its own row.
/// The detail records who asked (<see cref="OptOutSource.Request"/>) as a real, verified claim, not
/// a defaulted one — unlike the <c>ContactedMaintainer</c> defect elsewhere in this repository's history.
/// </remarks>
public sealed class OwnerOptOut(
    IGameQueries queries,
    OwnerEnrichment ownership,
    OptOutGate gate,
    ICrawlOptOutRepository optOuts)
{
    /// <summary>Whether we are currently honouring an opt-out for this game, and by which route.</summary>
    /// <remarks>
    /// Reads every address, not just the first: a game partially opted out (<see cref="OwnerOptOutState.Partial"/>)
    /// is shown as such, not rounded to the friendlier neighbour.
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
    /// <remarks>Read from the endpoints we measured, not from anything the owner typed, so the button can only cover addresses this crawler has actually dialled for this game.</remarks>
    private async Task<IReadOnlyList<(string Host, int Port)>> AddressesAsync(
        Guid gameId,
        CancellationToken cancellationToken)
    {
        if (await queries.FindAsync(gameId, cancellationToken) is not { } page)
        {
            return [];
        }

        // Current addresses only. An address a game has LEFT may already answer for somebody else,
        // and opting out on this owner's say-so would stop us dialling a game unrelated to them. The
        // departed endpoint stays on the page (§7.5); this owner just no longer speaks for it.
        return [.. page.Endpoints
            .Where(e => e.IsCurrent)
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
    /// <remarks>An MSSP field or TXT record is the game itself saying stop on every probe; the dashboard doesn't offer to overrule it, only to withdraw a recorded request.</remarks>
    public bool WithdrawableHere =>
        Stopped.Count > 0 && Standing.All(rule => rule.Source is OptOutSource.Request);
}
