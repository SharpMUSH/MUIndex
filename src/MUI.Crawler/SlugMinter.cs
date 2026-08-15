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
    ILogger<SlugMinter>? logger = null)
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

    private static GameField? Winner(IReadOnlyList<GameField> stored, string field) =>
        FieldPrecedence.Winner(
            stored.Where(row => string.Equals(row.Field, field, StringComparison.OrdinalIgnoreCase)));
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
