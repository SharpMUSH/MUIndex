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

    /// <summary>
    /// Moves a game between lifecycle states. Archiving is a presentation change and never a deletion
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
    /// <para>
    /// <b>The retirement and the re-mint are one write.</b> The old URL stops being current and starts
    /// redirecting at the same instant, so there is no moment in which a slug somebody is holding is
    /// neither — and no way to leave a game renamed with nothing pointing at it, which is the failure
    /// this whole table exists to prevent.
    /// </para>
    /// <para>
    /// Renaming is a <em>measured</em> change and never a tidy-up: only something reading the winning
    /// <c>NAME</c> field may call it, and only once that name has held for a grace period, or a game
    /// that flips its name daily churns its URL — see <c>SlugMinter</c>, which is the only caller.
    /// </para>
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
    /// A cache of "does a verified claim exist", denormalised onto the game because the listing reads
    /// it for every row and §7.5's grace reads it on every sweep. <see cref="ClaimService"/> owns it;
    /// nothing else may write it, or the flag and the claims it summarises will drift.
    /// </remarks>
    Task SetClaimedAsync(Guid id, bool isClaimed, CancellationToken cancellationToken = default);

    /// <summary>
    /// Games eligible for the archive sweep: everything not already archived. Deliberately not
    /// "everything dark" — the sweeper computes darkness from the availability series, and a
    /// pre-filter here would be a second definition of it.
    /// </summary>
    Task<IReadOnlyList<GameRecord>> UnarchivedAsync(CancellationToken cancellationToken = default);
}

/// <summary>The addresses a game answers on (spec §5.5).</summary>
/// <remarks>
/// Hosts are canonicalised by every implementation, on both ends: an upsert stores
/// <c>HostName.Normalize(endpoint.Host)</c> and <see cref="ByAddressAsync"/> looks up
/// <c>HostName.Normalize(host)</c>. That is part of this interface's contract rather than one
/// implementation's private habit, because a fake that compared more leniently than the real thing
/// would pass every test while production minted a duplicate endpoint for a host spelled in capitals.
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
/// Separate from <see cref="IAvailabilityStore"/> because it answers a different question with a
/// different shape: not "what happened" but "how much of it did we watch", and split by origin
/// because <see cref="ArchivePolicy.GraceFor"/> weights an imported hour at half of one of ours.
/// Summed in the database rather than by reading every interval into memory — a game watched for a
/// decade has few intervals, but the sweep asks this of every game in the catalogue at once.
/// </remarks>
public interface IReachableHistory
{
    /// <summary>
    /// Time this site measured the game as reachable, with the open interval counted to
    /// <paramref name="now"/>. Cumulative, not span (§7.5): a game reachable for two years out of
    /// five is credited with two, and a history of flapping accrues nothing for the gaps.
    /// </summary>
    Task<TimeSpan> CumulativeReachableAsync(
        Guid gameId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
