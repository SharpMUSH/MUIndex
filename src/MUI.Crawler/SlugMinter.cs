using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawl;
using MUI.Discovery;

using Microsoft.Extensions.Logging;

namespace MUI.Crawler;

/// <summary>
/// Re-mints the URL of a game that has renamed itself, and retires the one it had (spec §5.7).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the writer <c>game_slug_history</c> exists for.</b> §5.7 promises that every slug a game
/// has ever had redirects to it for ever; an alias table nothing writes to would be that promise with
/// a schema under it and nobody keeping it. So the rename path and the redirect are one act, and it is
/// this one.
/// </para>
/// <para>
/// <b>A rename is not a re-mint.</b> §5.7: "a game that flips its name daily would otherwise churn its
/// URL — it is re-minted only when the name has been stable for one grace period, and the old slug
/// redirects from that moment". Stability is read off the change feed, which is the only record of
/// when a value <em>became</em> what it is: <c>GameField.FirstSeenAt</c> is the age of the row and
/// survives a change on purpose.
/// </para>
/// <para>
/// <b>The name it mints from is the one <see cref="CatalogueBinder"/> would have listed the game
/// under</b> — <c>MsspDefaults.MeaningfulName</c>, so a server whose <c>NAME</c> is its codebase's has
/// not renamed itself and cannot drag a dozen unrelated games to <c>/g/pennmush-4</c>. One rule, read
/// twice.
/// </para>
/// <para>
/// <b>Nothing here deletes or rewrites a former slug.</b> A game that takes back a name it once had
/// keeps both rows; the row pointing at a slug that is current again simply stops answering, which
/// <see cref="ISlugHistoryStore.CurrentSlugAsync"/> enforces in SQL.
/// </para>
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
    /// Twice <c>ProbeSchedule.LongestInterval</c>, and that is the reasoning rather than a preference:
    /// a game at the slowest schedule is probed once a week, so a shorter window could re-mint on a
    /// single sighting of a name — including one a server published while its config was half-edited.
    /// Two intervals means a name has survived at least one more probe than the one that introduced
    /// it. The redirect is for ever either way; what this buys is a URL that does not churn.
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

        // An owner has said what this game is called, and it outranks the report on every surface
        // that renders it (§5.1, §8.5). Re-minting from MSSP here would spend every cycle renaming
        // the game back to what its config says — the owner's name on the page and the report's in
        // the URL, with a change-feed entry each time. Their withdrawal is an empty row, which is why
        // this asks for a value rather than for the row's existence: nothing is deleted, so the row
        // outlives the override and must stop counting when the value goes.
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

        // Where the value has never moved, the row's own age is when this name started: the game was
        // listed under an address or a placeholder and only later said what it is called. Both are
        // "how long has it said this", which is the question §5.7 asks.
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
    /// <para>
    /// <b>No grace, and this is the whole difference from <see cref="ConsiderAsync"/>.</b> The
    /// fourteen days exist because MSSP flaps — a name published while a config was half-edited
    /// should not churn a URL, and waiting is the only way to tell a settled name from a passing one.
    /// An owner pressing save has already answered that question, and making them wait a fortnight
    /// while the page showed the new name and the URL showed the old one would be the interface
    /// disbelieving somebody it has verified.
    /// </para>
    /// <para>
    /// <b><c>MsspDefaults.MeaningfulName</c> is not consulted either.</b> That filter stops an
    /// <em>unedited</em> codebase publishing its own name from minting a dozen listings called
    /// PennMUSH. A name typed on purpose by a verified owner is edited by definition, and whoever
    /// runs the PennMUSH development server is entitled to call it PennMUSH.
    /// </para>
    /// <para>
    /// Everything after that is the same act as a measured rename, deliberately: one mint, one
    /// <c>game_slug_history</c> row, one promise that every slug redirects for ever. A second rename
    /// path would be a second place for that promise to be almost true.
    /// </para>
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
    /// Explicit, so the crawler's own callers keep the <see cref="Rename"/> they act on — the cycle
    /// logs it and the change feed is written from it — while the seam carries only what the seam
    /// needs.
    /// </remarks>
    Task IOwnerRenames.RenameAsync(Guid gameId, string name, CancellationToken cancellationToken) =>
        ApplyAsync(gameId, name, cancellationToken);

    /// <summary>
    /// Mints the URL for a name and retires the one it replaces — the act both entry points perform.
    /// </summary>
    /// <remarks>
    /// Shared rather than written twice, because the two callers differ only in <em>whether</em> to
    /// rename and never in <em>how</em>. §5.7's promise that every slug a game has ever had redirects
    /// to it for ever is kept by exactly these four lines, and a copy of them is a copy that can fall
    /// out of step with the table.
    /// </remarks>
    private async Task<Rename?> MintAsync(
        GameRecord game,
        string name,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var slug = await GameSlug.UniqueAsync(
            name,
            (candidate, ct) => IsTakenAsync(game.Id, candidate, ct),
            cancellationToken);

        string? retired;

        try
        {
            retired = await games.RenameAsync(game.Id, name, slug, now, cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // The mint asked whether the slug was taken and the write happens a moment later, so two
            // games settling on one name in the same cycle lose that race at the unique index. The
            // name is measured and stored either way; only the URL is postponed, and the next probe
            // of this game tries again against a catalogue that has moved on. Deliberately broad and
            // deliberately not rethrown: the caller has already written this probe's reachability,
            // and a rename is the one step here whose failure must not reach it.
            //
            // An owner's save takes the same treatment for the same reason: their field row is
            // already stored, the page already shows the new name, and only the URL waits.
            logger?.LogWarning(
                error,
                "{Name} could not take {Slug}; it keeps {Current} and the re-mint is retried on the "
                + "next probe",
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
    /// A game's own former slug is not taken <em>from it</em>: a game that renames back to what it was
    /// gets its old URL back rather than <c>corvid-2</c>, which is the answer a reader with the old
    /// bookmark wants and the one the table already points at.
    /// </remarks>
    private async Task<bool> IsTakenAsync(Guid gameId, string candidate, CancellationToken ct) =>
        (await games.BySlugAsync(candidate, ct) is { } holder && holder.Id != gameId)
        || (await history.RetiredByAsync(candidate, ct) is { } former && former != gameId);

    /// <summary>
    /// The winning value among what a game <em>reported</em> about itself, ignoring its owner.
    /// </summary>
    /// <remarks>
    /// <see cref="ConsiderAsync"/> is the MSSP-driven path, and an owner row would win the ladder and
    /// then be the wrong answer twice: while an override stands the method has already returned, and
    /// once it is withdrawn the row survives as an empty value — nothing is deleted — which would
    /// resolve to no meaningful name at all and freeze the URL for ever.
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
