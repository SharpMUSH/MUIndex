using MUI.Catalog;
using MUI.Catalog.Persistence;

namespace MUI.Web.Accounts;

/// <summary>What an owner's unlisting request did.</summary>
public enum OwnerListingVerdict
{
    Applied,

    /// <summary>Nobody with a verified claim on this game asked. Nothing was written.</summary>
    NotAnOwner,

    /// <summary>
    /// We are still dialling at least one of this game's addresses, so there is no standing decision
    /// to extend.
    /// </summary>
    /// <remarks>Refused rather than applied anyway: unlisting a still-crawled game would leave a page nobody can find but that keeps filling with fresh measurements.</remarks>
    NotOptedOut,
}

/// <summary>The verdict, and nothing else — an unlisting covers the game rather than its addresses.</summary>
public sealed record OwnerListingOutcome(OwnerListingVerdict Verdict)
{
    public static readonly OwnerListingOutcome Applied = new(OwnerListingVerdict.Applied);

    public static readonly OwnerListingOutcome NotAnOwner = new(OwnerListingVerdict.NotAnOwner);

    public static readonly OwnerListingOutcome NotOptedOut = new(OwnerListingVerdict.NotOptedOut);

    public bool IsApplied => Verdict is OwnerListingVerdict.Applied;
}

/// <summary>
/// The second half of §11's opt-out: out of the listing as well as out of the crawl (migration 0025).
/// </summary>
/// <remarks>
/// <b>An opt-out stops us dialling and nothing else</b> — the page keeps everything measured before
/// the ask, and empty hours name no cause. This is the separate ask to take the page out of the
/// directory too.
/// <b>Offered only under a standing opt-out on every address</b> (<see cref="OwnerOptOutState.Partial"/>
/// is refused, not rounded up): a game we are still dialling has not stopped, so taking its page out
/// of the listing while measurements keep arriving would be the worst of both, and it keeps "stop
/// crawling us" (reversible by a probe) ordered before "and delist us" (a deliberate second ask).
/// <b>The account is the stored authorisation</b> — never inferred, unlike the <c>ContactedMaintainer</c> defect elsewhere in this repository's history.
/// </remarks>
public sealed class OwnerListing(
    IGameQueries queries,
    IGameStore games,
    OwnerEnrichment ownership,
    OwnerOptOut optOut,
    TimeProvider time)
{
    /// <summary>Whether this game is in the listing, and whether the owner may take it out.</summary>
    public async Task<OwnerListingState> StateAsync(Guid gameId, CancellationToken cancellationToken = default)
    {
        var page = await queries.FindAsync(gameId, cancellationToken);
        var stopped = (await optOut.StateAsync(gameId, cancellationToken)).AllStopped;

        return new OwnerListingState(
            IsUnlisted: page?.Summary.State is LifecycleState.Unlisted,
            MayUnlist: stopped);
    }

    /// <summary>Takes the game out of the listing, or puts it back.</summary>
    public async Task<OwnerListingOutcome> SetAsync(
        Guid gameId,
        Guid userId,
        bool unlist,
        CancellationToken cancellationToken = default)
    {
        if (!await ownership.OwnsAsync(gameId, userId, cancellationToken))
        {
            return OwnerListingOutcome.NotAnOwner;
        }

        if (!unlist)
        {
            // Relisting is never gated on the opt-out — an owner who already withdrew it via their own
            // zone file and is waiting out §7.4's floor must not find this button refusing them too.
            await games.RelistAsync(gameId, time.GetUtcNow(), cancellationToken);

            return OwnerListingOutcome.Applied;
        }

        if (!(await optOut.StateAsync(gameId, cancellationToken)).AllStopped)
        {
            return OwnerListingOutcome.NotOptedOut;
        }

        await games.UnlistAsync(gameId, userId, time.GetUtcNow(), cancellationToken);

        return OwnerListingOutcome.Applied;
    }
}

/// <summary>What the dashboard needs to render the listing control.</summary>
/// <param name="IsUnlisted">Whether the game is out of the listing right now.</param>
/// <param name="MayUnlist">
/// Whether every address is under a standing opt-out, which is what the control is offered under.
/// </param>
public sealed record OwnerListingState(bool IsUnlisted, bool MayUnlist);
