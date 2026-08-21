using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawl;
using MUI.Discovery;

using Microsoft.Extensions.Logging;

using Npgsql;

namespace MUI.Crawler;

/// <summary>
/// Re-mints the URL of a game that has renamed itself, and retires the one it had (spec §5.7).
/// </summary>
/// <remarks>
/// This is the writer <c>game_slug_history</c> exists for — §5.7's promise that every slug a game has
/// ever had redirects to it for ever is only true because the rename path and the redirect are one
/// act. A rename is not a re-mint: it happens only once a name has been stable for one grace period,
/// read off the change feed (<c>GameField.FirstSeenAt</c> is the row's age and survives a change on
/// purpose), so a game flipping its name daily doesn't churn its URL. Mints from the same name
/// <see cref="CatalogueBinder"/> would list under (<c>MsspDefaults.MeaningfulName</c>), so an unedited
/// codebase can't drag unrelated games to <c>/g/pennmush-4</c>. Nothing here deletes or rewrites a
/// former slug — a game that takes back an old name keeps both rows.
/// </remarks>
public sealed class SlugMinter(
    IGameStore games,
    IGameFieldStore fields,
    ISlugHistoryStore history,
    TimeSpan? grace = null,
    ILogger<SlugMinter>? logger = null) : IOwnerRenames
{
    /// <summary>
    /// How long a new name must hold before it is worth a new URL.
    /// </summary>
    /// <remarks>
    /// Twice <c>ProbeSchedule.LongestInterval</c>: a game on the slowest schedule is probed weekly, so
    /// a shorter window could re-mint on a single sighting of a name — including one published while a
    /// config was half-edited. Two intervals means the name survived at least one more probe.
    /// </remarks>
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromDays(14);

    private readonly TimeSpan _grace = grace ?? DefaultGrace;

    /// <summary>
    /// Re-mints this game's URL if its declared name has settled into something else, or does nothing.
    /// </summary>
    /// <param name="gameId">The game a probe was just attributed to.</param>
    /// <param name="now">The instant the probe observed, not the wall clock.</param>
    /// <param name="cancellationToken">The cycle's budget.</param>
    public async Task<Rename?> ConsiderAsync(
        Guid gameId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (await games.ByIdAsync(gameId, cancellationToken) is not { } game)
        {
            return null;
        }

        var stored = await fields.ForGameAsync(gameId, cancellationToken);

        // An owner's stated name outranks the report on every rendered surface (§5.1, §8.5), so
        // re-minting from MSSP while an override stands would spend every cycle renaming back to
        // MSSP's name. Checks the value, not row existence: a withdrawn override is an empty row that
        // outlives the withdrawal and must stop counting once the value is gone.
        if (stored.Any(row => row.Source is FieldSource.Owner
                && string.Equals(row.Field, IdentityMsspVariables.Name, StringComparison.OrdinalIgnoreCase)
                && row.Value.Length > 0))
        {
            return null;
        }

        if (Winner(stored, IdentityMsspVariables.Name) is not { } declared)
        {
            return null;
        }

        var name = MsspDefaults.MeaningfulName(
            declared.Value, Winner(stored, IdentityMsspVariables.Codebase)?.Value);

        if (name is null || string.Equals(name, game.Name, StringComparison.Ordinal))
        {
            return null;
        }

        // Where the value has never moved, the row's own age is when this name started.
        var since = await fields.LastChangedAtAsync(gameId, declared.Field, cancellationToken)
            ?? declared.FirstSeenAt;

        if (now - since < _grace)
        {
            return null;
        }

        return await MintAsync(game, name, now, cancellationToken);
    }

    /// <summary>
    /// Applies a name a verified owner chose, at once (spec §8.5).
    /// </summary>
    /// <remarks>
    /// No grace, unlike <see cref="ConsiderAsync"/>: the fourteen days exist to tell a settled MSSP
    /// name from a flapping one, and an owner pressing save has already answered that question.
    /// <c>MsspDefaults.MeaningfulName</c> is not consulted either — that filter stops an
    /// <em>unedited</em> codebase minting listings called PennMUSH, and a name typed on purpose by a
    /// verified owner is edited by definition. Everything after is the same act as a measured rename:
    /// one mint, one <c>game_slug_history</c> row.
    /// </remarks>
    public async Task<Rename?> ApplyAsync(
        Guid gameId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (await games.ByIdAsync(gameId, cancellationToken) is not { } game
            || string.Equals(name, game.Name, StringComparison.Ordinal))
        {
            return null;
        }

        // The owner's own instant is the write's, not a probe's: nothing was observed here.
        return await MintAsync(game, name, DateTimeOffset.UtcNow, cancellationToken);
    }

    /// <summary>
    /// The catalogue's view of the same act, so <c>OwnerEnrichment</c> can ask for it without
    /// <c>MUI.Catalog</c> knowing this class exists.
    /// </summary>
    /// <remarks>
    /// Explicit, so the crawler's own callers keep the <see cref="Rename"/> they act on while the seam
    /// carries only what it needs.
    /// </remarks>
    Task IOwnerRenames.RenameAsync(Guid gameId, string name, CancellationToken cancellationToken) =>
        ApplyAsync(gameId, name, cancellationToken);

    /// <summary>
    /// Mints the URL for a name and retires the one it replaces — the act both entry points perform.
    /// </summary>
    /// <remarks>
    /// Shared rather than written twice: the two callers differ only in <em>whether</em> to rename,
    /// never in <em>how</em>.
    /// </remarks>
    private async Task<Rename?> MintAsync(
        GameRecord game,
        string name,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Falls back to the slug it already has: a name this fold can't render keeps the
        // address-derived slug and takes its real name only on the page. Not a collision, since
        // IsTakenAsync answers for everybody except this game.
        var slug = await GameSlug.UniqueAsync(
            name,
            (candidate, ct) => IsTakenAsync(game.Id, candidate, ct),
            game.Slug,
            cancellationToken);

        string? retired;

        try
        {
            retired = await games.RenameAsync(game.Id, name, slug, now, cancellationToken);
        }
        // Two games settling on one name in the same cycle can lose the race at the unique index
        // between the taken-check and this write — game_slug_key (migration 0001), the one
        // NpgsqlGameStore.RenameAsync's own comment names as the guard against two games racing to
        // claim one address. Named separately from the catch below so the ordinary race logs at
        // Warning and everything else logs at Error, the same way NpgsqlI3BindingRepository.BindAsync
        // catches its own race.
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.UniqueViolation
            && error.ConstraintName == "game_slug_key")
        {
            // Not rethrown: the caller has already written this probe's reachability, and a rename
            // failure must not reach it — the name is stored either way, and the next probe tries the
            // URL again.
            logger?.LogWarning(
                error,
                "{Name} could not take {Slug}; it keeps {Current} and the re-mint is retried on the "
                + "next probe",
                name, slug, game.Slug);

            return null;
        }
        // Anything else here — a different constraint, a dropped connection, a bug in our own rename
        // path — must still not reach the caller: ProbeIngestor.AnsweredAsync calls this last, after
        // the probe's own reachability and presence data are already durably written, so letting an
        // exception escape would mark an already-successful probe as Errored in the crawl cycle's own
        // tally (CrawlCycle.VisitAsync's catch) — exactly the "our decision recorded as their
        // measurement" rule 5 forbids, just arriving from the failure side instead of the success
        // side. Logged at Error, not Warning, so it doesn't read as the routine race above and stays
        // visible as something that needs a look.
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger?.LogError(
                error,
                "{Name}'s rename to {Slug} failed unexpectedly; it keeps {Current} and the re-mint is "
                + "retried on the next probe",
                name, slug, game.Slug);

            return null;
        }

        logger?.LogInformation(
            "{Old} is now called {Name}; {Slug} is its URL and {Retired} redirects to it for ever",
            game.Name, name, slug, retired ?? "nothing");

        return new Rename(game.Id, name, slug, retired);
    }

    /// <summary>
    /// Whether a candidate slug belongs to somebody else — currently, or in a URL they are still
    /// holding.
    /// </summary>
    /// <remarks>
    /// A game's own former slug is not taken <em>from it</em>: renaming back to an old name gets the
    /// old URL back rather than <c>corvid-2</c>.
    /// </remarks>
    private async Task<bool> IsTakenAsync(Guid gameId, string candidate, CancellationToken ct) =>
        (await games.BySlugAsync(candidate, ct) is { } holder && holder.Id != gameId)
        || (await history.RetiredByAsync(candidate, ct) is { } former && former != gameId);

    /// <summary>
    /// The winning value among what a game <em>reported</em> about itself, ignoring its owner.
    /// </summary>
    /// <remarks>
    /// <see cref="ConsiderAsync"/> is the MSSP-driven path; an owner row would win the ladder and then
    /// be wrong twice over — the method already returns early while an override stands, and a
    /// withdrawn override survives as an empty value that would otherwise freeze the URL for ever.
    /// </remarks>
    private static GameField? Winner(IReadOnlyList<GameField> stored, string field) =>
        FieldPrecedence.Winner(stored
            .Where(row => row.Source is not FieldSource.Owner)
            .Where(row => string.Equals(row.Field, field, StringComparison.OrdinalIgnoreCase)));
}

/// <summary>
/// A game's name and URL, as they now are (spec §5.7).
/// </summary>
/// <param name="GameId">The game. Unchanged, and unchangeable — the id is the identifier.</param>
/// <param name="Name">What it now calls itself.</param>
/// <param name="Slug">The URL it now answers on.</param>
/// <param name="FormerSlug">
/// The URL it used to answer on, which now redirects here for ever, or null when the new name minted
/// the same slug as the old one.
/// </param>
public sealed record Rename(Guid GameId, string Name, string Slug, string? FormerSlug);
