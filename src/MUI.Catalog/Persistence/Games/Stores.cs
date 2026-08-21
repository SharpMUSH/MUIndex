namespace MUI.Catalog.Persistence;

/// <summary>
/// The <c>game</c> table (spec §5, §7.5). <see cref="IGameFieldStore"/>, <see cref="IPresenceStore"/>
/// and <see cref="IAvailabilityStore"/> all hang off it.
/// </summary>
public interface IGameStore
{
    Task<GameRecord?> ByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<GameRecord?> BySlugAsync(string slug, CancellationToken cancellationToken = default);

    Task InsertAsync(GameRecord game, CancellationToken cancellationToken = default);

    /// <summary>Takes a game out of the listing because it isn't a game for players.</summary>
    /// <remarks>
    /// Separate from <see cref="SetStateAsync"/>: this is the only state change that carries a
    /// reason, and refuses to move a game that's already excluded — an automatic sweep must not
    /// discard a judgement a person made.
    /// </remarks>
    Task ExcludeAsync(Guid id, string reason, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>Puts an excluded game back in the listing. A person's act, like the exclusion.</summary>
    Task IncludeAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes a game out of the listing because the people who run it asked (spec §11, migration 0025).
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ExcludeAsync"/>: this carries an account, not an argument. The reason
    /// is that they asked; <paramref name="byUserId"/> is the account that held a verified claim, and
    /// <c>crawl_opt_out</c> holds how the ask arrived.
    /// </remarks>
    /// <param name="byUserId">
    /// The account that asked — claim-verified through the dashboard, or the operator's own where
    /// §11's recorded-request route reaches the listing by hand. Never inferred, never defaulted.
    /// </param>
    Task UnlistAsync(
        Guid id,
        Guid byUserId,
        DateTimeOffset at,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts an unlisted game back in the listing, and hands it back to the crawl.
    /// </summary>
    /// <remarks>
    /// Called by the owner's own button, and by <see cref="ArchiveSweeper.RestoreAsync"/> when a
    /// probe answers — the documented exit for an operator who withdrew the opt-out in their own
    /// zone file and never had an account here.
    /// </remarks>
    Task RelistAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a game between lifecycle states. Archiving is a presentation change, never a deletion
    /// (§7.5): the row, its fields, its history and its slug all survive it untouched.
    /// </summary>
    Task SetStateAsync(
        Guid id,
        LifecycleState state,
        DateTimeOffset at,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a submitted game because a probe showed it to be one (spec §7.8). Write-once: the
    /// signals are what was true when it published, and a later probe does not revise them.
    /// </summary>
    Task CorroborateAsync(
        Guid id,
        DateTimeOffset at,
        IReadOnlyList<string> signals,
        CancellationToken cancellationToken = default);

    /// <summary>Records that the game answered, which is what §7.5's grace is measured from.</summary>
    Task MarkReachableAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-mints a game's name and URL, retiring the slug it had (spec §5.7).
    /// </summary>
    /// <remarks>
    /// The retirement and the re-mint are one write: the old URL stops being current and starts
    /// redirecting at the same instant, so a slug somebody is holding is never left in between, and a
    /// game can't end up renamed with nothing pointing at it.
    /// Renaming is measured, never a tidy-up: only something reading the winning <c>NAME</c> field
    /// may call it, and only after that name has held for a grace period — see <c>SlugMinter</c>, the
    /// only caller.
    /// </remarks>
    /// <returns>
    /// The slug that was retired, or null when the slug did not move — a game may change its name
    /// without changing the URL that name mints ("Corvid!" and "Corvid" are one slug).
    /// </returns>
    Task<string?> RenameAsync(
        Guid id,
        string name,
        string slug,
        DateTimeOffset at,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets whether any account has proved control of this game (spec §8).
    /// </summary>
    /// <remarks>
    /// A cache of "does a verified claim exist", denormalised because the listing and §7.5's grace
    /// both read it on every row. <see cref="ClaimService"/> owns it; nothing else may write it, or
    /// it will drift from the claims it summarises.
    /// </remarks>
    Task SetClaimedAsync(Guid id, bool isClaimed, CancellationToken cancellationToken = default);

    /// <summary>
    /// Games eligible for the archive sweep: everything not already archived — deliberately not
    /// "everything dark", since the sweeper computes darkness itself and a pre-filter here would be a
    /// second definition of it.
    /// </summary>
    Task<IReadOnlyList<GameRecord>> UnarchivedAsync(CancellationToken cancellationToken = default);
}

/// <summary>The addresses a game answers on (spec §5.5).</summary>
/// <remarks>
/// Hosts are canonicalised by every implementation, on both ends — an upsert stores
/// <c>HostName.Normalize(endpoint.Host)</c> and <see cref="ByAddressAsync"/> looks up the same. Part
/// of the interface's contract, not one implementation's habit: a lenient fake would pass every test
/// while production minted a duplicate endpoint for a host spelled in capitals.
/// </remarks>
public interface IEndpointStore
{
    Task<IReadOnlyList<GameEndpoint>> ForGameAsync(Guid gameId, CancellationToken cancellationToken = default);

    /// <summary>§7.3's strongest identity signal, asked of an address with no game in hand.</summary>
    Task<GameEndpoint?> ByAddressAsync(string host, int port, CancellationToken cancellationToken = default);

    Task UpsertAsync(GameEndpoint endpoint, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reachable time summed by who measured it (spec §7.5, §7.6).
/// </summary>
/// <remarks>
/// Separate from <see cref="IAvailabilityStore"/>: answers "how much did we watch", not "what
/// happened", and split by origin because <see cref="ArchivePolicy.GraceFor"/> weights an imported
/// hour at half of one of ours. Summed in the database, not by reading every interval into memory —
/// the sweep asks this of every game in the catalogue at once.
/// </remarks>
public interface IReachableHistory
{
    /// <summary>
    /// Time this site measured the game as reachable, open interval counted to <paramref name="now"/>.
    /// Cumulative, not span (§7.5): a game reachable two years out of five is credited two, and
    /// flapping accrues nothing for the gaps.
    /// </summary>
    Task<TimeSpan> CumulativeReachableAsync(
        Guid gameId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
