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
    /// <remarks>
    /// Said out loud rather than applied anyway. Unlisting a game we are still crawling would produce
    /// the one combination nothing else in the system can describe: a page nobody can find, filling
    /// up with fresh measurements.
    /// </remarks>
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
/// <para>
/// <b>An opt-out stops us dialling and nothing else</b>, which is what the about page has always
/// promised and what most operators who use it want — the page keeps everything measured before the
/// ask, and the empty hours name no cause. Some want the page out of the directory too, and until
/// this there was nothing to offer them but the same sentence again.
/// </para>
/// <para>
/// <b>Offered only under a standing opt-out on every address, and that gate is the design.</b>
/// <see cref="OwnerOptOut.StateAsync"/>'s <c>AllStopped</c> is the precondition:
/// <see cref="OwnerOptOutState.Partial"/> is refused, not rounded up, for the reason the panel
/// refuses to round it — a game whose second port we are still dialling has not stopped, and taking
/// its page out of the listing while measurements went on arriving would be the worst of both. It
/// also keeps the two decisions in the right order. "Stop crawling us" is reversible by a probe;
/// "and take us out of the listing" is a thing to be asked for on purpose, once, by somebody who has
/// already made the first decision.
/// </para>
/// <para>
/// <b>The account is the authorisation and it is stored.</b> Everything about this is a claim about
/// what somebody else wants, which is the class of statement this repository has already got wrong
/// by letting a default make it (<c>ContactedMaintainer</c>). The row records the account that held
/// a verified claim at the moment the button was pressed, and <c>crawl_opt_out</c> holds how the ask
/// arrived. Neither is inferred.
/// </para>
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
            // Relisting is never gated on the opt-out. An owner who has already withdrawn it in their
            // own zone file, and is waiting out §7.4's floor for the probe that would relist them
            // automatically, must not find the button that would do it now refusing on the grounds
            // that they are no longer opted out.
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
