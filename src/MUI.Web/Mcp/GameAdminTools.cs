using System.ComponentModel;
using System.Diagnostics;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawler;
using MUI.Discovery;

using Npgsql;

namespace MUI.Web.Mcp;

/// <summary>
/// The three catalogue-write tools -- the half of the nine that write directly to a game's own
/// record: <see cref="GameFieldSetAsync"/>, a staff override of one field;
/// <see cref="GameRenameAsync"/>, which writes <c>NAME</c> through that same override and re-mints
/// the slug; and <see cref="GameMergeAsync"/>, which drains one <c>duplicate_review</c> pair by hand.
/// The six crawl/registry tools -- <c>crawl_seed_add</c> through <c>crawl_summary</c> -- are
/// <see cref="CrawlAdminTools"/>, a separate <c>[McpServerToolType]</c> registered alongside this one
/// (<c>MuiMcp.AddMuiMcp</c>): the two groups share no state beyond <c>time</c>/<c>logger</c>, so
/// there is nothing a combined class bought except size.
/// </summary>
/// <remarks>
/// <see cref="SlugMinter"/> is the same instance <c>OwnerEnrichment</c> resolves for a verified
/// owner's own rename (<c>Passkeys.AddMuiAccounts</c>) -- <see cref="GameRenameAsync"/> takes the
/// identical no-grace mint-and-rename path, on staff's say-so instead of an owner's. Registered
/// against DI's per-request scope in stateless HTTP mode (<c>MuiMcp.AddMuiMcp</c>), so a tool call
/// reuses the request's own service provider exactly as a minimal API endpoint would.
/// </remarks>
[McpServerToolType]
public sealed class GameAdminTools(
    IGameStore games,
    IGameFieldStore fields,
    IFieldRegistry registry,
    SlugMinter minter,
    ReviewMergeService merge,
    TimeProvider time,
    ILogger<GameAdminTools>? logger = null)
{
    [McpServerTool(Name = "game_field_set")]
    [Description("""
        Staff override of one field of one game -- e.g. fixing a mis-parsed NAME or CHARSET, or hand-
        setting a DESCRIPTION. Writes through FieldSource.Staff (spec section 5.1), which outranks
        every other source and is always recorded to the change feed. `field` must be either a name
        FieldRegistry knows or one this game has already reported (both matched case-insensitively) --
        MSSP ingestion is not registry-gated, so a game can publish its own variables (DESCRIPTION-DE
        is a real one) and those are rendered and must stay correctable. A name that is neither is
        refused rather than written as garbage, and so are PLAYERS/UPTIME, which spec section 5.2
        requires to live only in the presence series and never as a GameField row.

        Out of scope: renaming a game's slug. If field is NAME, the value still lands and will affect
        what the site displays and what the identity matcher sees -- but this does NOT run the
        separate rename/re-mint dance IGameStore.RenameAsync performs for a hand-run rename (see
        SlugMinter), so the slug will NOT change on its own.
        """)]
    public async Task<GameFieldSetResult> GameFieldSetAsync(
        [Description("The game's slug, e.g. pennmush.")] string gameSlug,
        [Description(
            "Either a field name FieldRegistry knows (e.g. NAME, DESCRIPTION, CHARSET, GENRE) or one "
            + "this game has already reported (e.g. an ungated MSSP variable like DESCRIPTION-DE). "
            + "Matched case-insensitively; the stored spelling is the one written. PLAYERS and UPTIME "
            + "are refused.")]
        string field,
        [Description(
            "The new value. An empty string withdraws it -- still recorded to the change feed, "
            + "because nothing here is ever deleted (spec section 5.1).")]
        string value,
        CancellationToken cancellationToken = default)
    {
        RequireNotBlank(gameSlug, nameof(gameSlug));
        RequireNotBlank(field, nameof(field));

        if (value is null)
        {
            throw new McpException("value is required (pass an empty string to withdraw the field).");
        }

        var game = await games.BySlugAsync(gameSlug.Trim(), cancellationToken)
            ?? throw new McpException($"No game with slug '{gameSlug}'.");

        var trimmedField = field.Trim();

        // The registry is the ordinary gate, and it is what stops a typo becoming a row. It is not
        // the whole vocabulary, though: MSSP ingestion is deliberately ungated, so a game may report
        // any variable it likes — DESCRIPTION-DE is a real one — and the site stores and renders it
        // like any other. A field the game itself reported is therefore known too, or the one thing
        // staff could never correct would be what a reader is actually looking at.
        var definition = registry.Find(trimmedField)
            ?? FieldRegistry.All.FirstOrDefault(d =>
                string.Equals(d.Name, trimmedField, StringComparison.OrdinalIgnoreCase));

        // The stored spelling wins over the caller's: the primary key is (game, field, source), so a
        // second casing would be a second row that never meets the first in the precedence ladder.
        var fieldName = definition?.Name
            ?? (await fields.ForGameAsync(game.Id, cancellationToken))
                .FirstOrDefault(row =>
                    string.Equals(row.Field, trimmedField, StringComparison.OrdinalIgnoreCase))?.Field
            ?? throw new McpException(
                $"'{field}' is not a field FieldRegistry knows, and '{game.Slug}' has never reported "
                + "one by that name. See FieldRegistry.All for the list.");

        if (FieldReconciler.VolatileFields.Contains(fieldName))
        {
            throw new McpException(
                $"'{fieldName}' is measured per-probe (it belongs to the presence series) and is never "
                + "stored as a GameField row -- see FieldReconciler.VolatileFields.");
        }

        var now = time.GetUtcNow();
        var previousValue = await WriteStaffFieldAsync(game.Id, fieldName, value, now, cancellationToken);

        logger?.LogInformation(
            "game_field_set: {Slug}.{Field} := {Value} (staff)", game.Slug, fieldName, value);

        var warning = string.Equals(fieldName, "NAME", StringComparison.OrdinalIgnoreCase)
            ? "NAME was written, but the slug was NOT re-minted (this tool does not run "
                + "IGameStore.RenameAsync's rename/CTE dance) -- the page will show the new name "
                + "under the OLD url until a re-mint happens some other way. Use game_rename instead "
                + "if the slug should move too."
            : null;

        return new GameFieldSetResult(game.Slug, fieldName, previousValue, value, warning);
    }

    [McpServerTool(Name = "game_rename")]
    [Description("""
        Renames a game and mints it a new, unique slug at once -- the thing game_field_set explicitly
        declines to do when field is NAME. Writes NAME through FieldSource.Staff first (spec section
        5.1 -- the same write game_field_set itself performs, so the value has provenance and reaches
        the change feed), then runs SlugMinter.ApplyAsync -- the SAME immediate, no-grace mint-and-
        rename path a verified owner's own rename takes (spec section 5.7) -- rather than waiting for
        the ordinary fourteen-day grace a measured rename would. The old slug is retired into
        game_slug_history and 301-redirects to the new page for ever (FormerSlugRedirects); nothing
        else about the game -- its other fields, its presence history, its change feed -- is touched.

        A collision with another game's slug is not an error: GameSlug.UniqueAsync appends a numeric
        suffix (e.g. pennmush-2) the same way it does for any other mint. This tool only refuses when
        the game is not found, the requested name is the game's current name already (nothing to
        rename), or an actual database-level race prevented the mint just now -- in which case NAME
        was still written as staff and, being the highest-precedence source, it will win the ordinary
        crawl cycle's own re-mint once one next runs.
        """)]
    public async Task<GameRenameResult> GameRenameAsync(
        [Description("The game's current slug, e.g. pennmush.")] string gameSlug,
        [Description("The new name to give the game.")] string newName,
        [Description(
            "Why this is worth a new name and URL. Required -- a rename mints a URL the catalogue "
            + "keeps for ever, matching mui-crawl's --rename/--merge/--mint-now precedent that a "
            + "consequential catalogue write needs a stated reason beside it for later review.")]
        string because,
        CancellationToken cancellationToken = default)
    {
        RequireNotBlank(gameSlug, nameof(gameSlug));
        RequireNotBlank(newName, nameof(newName));
        RequireNotBlank(because, nameof(because));

        var game = await games.BySlugAsync(gameSlug.Trim(), cancellationToken)
            ?? throw new McpException($"No game with slug '{gameSlug}'.");

        var trimmedName = newName.Trim();

        if (string.Equals(trimmedName, game.Name, StringComparison.Ordinal))
        {
            throw new McpException($"'{game.Slug}' is already named '{trimmedName}'; nothing to rename.");
        }

        var now = time.GetUtcNow();

        await WriteStaffFieldAsync(game.Id, "NAME", trimmedName, now, cancellationToken);

        var rename = await minter.ApplyAsync(game.Id, trimmedName, cancellationToken)
            ?? throw new McpException(
                $"'{trimmedName}' could not be minted a unique slug for '{game.Slug}' right now (a "
                + "database-level collision SlugMinter could not resolve on this attempt). NAME was "
                + "still written as staff and will win the ordinary crawl cycle's own re-mint once "
                + "one next runs.");

        logger?.LogInformation(
            "game_rename: {Old} -> {Slug} ({Name}) -- {Because}",
            game.Slug, rename.Slug, rename.Name, because);

        return new GameRenameResult(rename.FormerSlug ?? game.Slug, rename.Slug, rename.Name);
    }

    [McpServerTool(Name = "game_merge")]
    [Description("""
        Drains one duplicate_review pair by hand -- same as mui-crawl's --merge --because (spec §7.3).
        Folds loserSlug into winnerSlug: an open review naming exactly this pair is resolved and its
        score/signals are carried onto the merge log unchanged; a pair the identity matcher never
        flagged is still mergeable, recorded as a judgement with no signals. A rival is never a merge
        on its own -- IdentityMatcher only ever opens a review (see IdentityMatcher.RivalAsync) -- this
        tool is the person acting on one.

        The merge is a redirect, not a move: loserSlug's page 301s to winnerSlug's for ever; nothing
        about loserSlug -- its presence history, its change feed, its other fields -- is touched or
        carried over. Reverting is a hand-written UPDATE merge_log SET reverted_at = now() (see
        CLAUDE.md); this tool does not expose an undo.

        Refuses when either slug is unknown, when they name the same game, or when the database itself
        refuses -- merge_log_absorbed_once_idx catches a loser already folded in elsewhere, and
        merge_log_no_chains catches a redirect chain (a game renamed then absorbed, or absorbed then
        asked to absorb another); both surface as an McpException with the schema's own message.
        """)]
    public async Task<GameMergeResult> GameMergeAsync(
        [Description("The slug that survives -- the canonical entry the other absorbs into.")]
        string winnerSlug,
        [Description("The slug that is absorbed and will 301 to winnerSlug for ever.")]
        string loserSlug,
        [Description(
            "What convinced you these are one game. Required -- matching mui-crawl's --merge "
            + "precedent that a consequential catalogue write needs a stated reason beside it.")]
        string because,
        CancellationToken cancellationToken = default)
    {
        RequireNotBlank(winnerSlug, nameof(winnerSlug));
        RequireNotBlank(loserSlug, nameof(loserSlug));
        RequireNotBlank(because, nameof(because));

        var winner = await games.BySlugAsync(winnerSlug.Trim(), cancellationToken)
            ?? throw new McpException($"No game with slug '{winnerSlug}'.");
        var loser = await games.BySlugAsync(loserSlug.Trim(), cancellationToken)
            ?? throw new McpException($"No game with slug '{loserSlug}'.");

        var verdict = await merge.MergeAsync(winner.Id, loser.Id, because, cancellationToken);

        // merge_log's own guards firing at the moment somebody would create the shape they refuse -- an
        // absorbed-elsewhere loser or a redirect chain. The database's message is more specific than
        // anything worth restating here (see Program.cs's --merge handling).
        var result = verdict switch
        {
            MergeVerdict.Merged(var merged) => merged,
            MergeVerdict.SelfMerge => throw new McpException("A game cannot be merged into itself."),
            MergeVerdict.UnknownGame(var id) => throw new McpException($"{id} does not name a game."),
            MergeVerdict.AlreadyAbsorbed(var databaseMessage) =>
                throw new McpException($"refused by the database: {databaseMessage}"),
            MergeVerdict.RedirectChain(var databaseMessage) =>
                throw new McpException($"refused by the database: {databaseMessage}"),
            _ => throw new UnreachableException($"Unhandled {nameof(MergeVerdict)}: {verdict}"),
        };

        logger?.LogInformation(
            "game_merge: {Loser} -> {Winner} (merge {MergeId}) -- {Because}",
            loser.Slug, winner.Slug, result.MergeId, because);

        return new GameMergeResult(
            winner.Slug,
            loser.Slug,
            result.MergeId,
            result.ResolvedReviewId,
            result.Score,
            result.MootReviewsResolved);
    }

    [McpServerTool(Name = "game_keep_distinct")]
    [Description("""
        Closes one duplicate_review pair the other way -- these are two games, not one (spec §7.3).
        Same as mui-crawl's --distinct --because. The counterpart to game_merge, and the only thing
        that clears a false positive: without it a pair the matcher scored middling and a person
        judged distinct stays open for ever, and most of the queue is that. Unordered -- neither
        slug wins, because nothing is absorbed.

        Nothing about either game moves and neither page changes. The one write is the
        duplicate_review row's own resolution, which is what stops the pair being asked about again;
        the row is kept, because a judgement is part of the record.

        Refuses when either slug is unknown, when both name the same game, when no open review names
        this pair (there is nothing to close, and "these are two games" is the state the catalogue is
        already in), and when a merge still in force already made them one listing -- revert that
        first, since recording both would leave the catalogue asserting one game and two.
        """)]
    public async Task<GameKeepDistinctResult> GameKeepDistinctAsync(
        [Description("One side of the pair.")] string slugA,
        [Description("The other side. Order does not matter -- nothing is absorbed.")] string slugB,
        [Description(
            "What convinced you these are two games -- a stock connect screen, one operator's "
            + "contact address across games they both run, and so on. Required, matching --merge's "
            + "precedent that a judgement nobody wrote down beside the row is one nobody can review.")]
        string because,
        CancellationToken cancellationToken = default)
    {
        RequireNotBlank(slugA, nameof(slugA));
        RequireNotBlank(slugB, nameof(slugB));
        RequireNotBlank(because, nameof(because));

        var left = await games.BySlugAsync(slugA.Trim(), cancellationToken)
            ?? throw new McpException($"No game with slug '{slugA}'.");
        var right = await games.BySlugAsync(slugB.Trim(), cancellationToken)
            ?? throw new McpException($"No game with slug '{slugB}'.");

        var verdict = await merge.KeepDistinctAsync(left.Id, right.Id, because, cancellationToken);

        var kept = verdict switch
        {
            DistinctVerdict.Kept keep => keep,
            DistinctVerdict.SameGame => throw new McpException("Both slugs name the same game."),
            DistinctVerdict.UnknownGame(var id) => throw new McpException($"{id} does not name a game."),
            DistinctVerdict.NoOpenReview =>
                throw new McpException($"No open duplicate review names '{left.Slug}' and '{right.Slug}'."),
            DistinctVerdict.AlreadyOneListing(var listing) => throw new McpException(
                $"A merge still in force already makes these one listing ({listing}). Revert it first."),
            _ => throw new UnreachableException($"Unhandled {nameof(DistinctVerdict)}: {verdict}"),
        };

        logger?.LogInformation(
            "game_keep_distinct: {A} + {B} (review {ReviewId}) -- {Because}",
            left.Slug, right.Slug, kept.ReviewId, because);

        return new GameKeepDistinctResult(left.Slug, right.Slug, kept.ReviewId, kept.Score);
    }

    /// <summary>
    /// Upserts one field of one game as <see cref="FieldSource.Staff"/> and records the transition
    /// when the value actually changed -- the write both <see cref="GameFieldSetAsync"/> and
    /// <see cref="GameRenameAsync"/> perform, the second for exactly one field, <c>NAME</c>.
    /// </summary>
    /// <returns>The value stored under this (game, field, staff) key before this call, or null.</returns>
    private async Task<string?> WriteStaffFieldAsync(
        Guid gameId, string field, string value, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = (await fields.ForGameAsync(gameId, FieldSource.Staff, cancellationToken))
            .FirstOrDefault(f => string.Equals(f.Field, field, StringComparison.Ordinal));

        // first_seen_at is only ever consumed on the INSERT branch of the upsert (NpgsqlGameFieldStore
        // deliberately does not overwrite it on conflict), so passing "now" here is safe whether this
        // is a fresh row or a confirmation of an existing one.
        await fields.UpsertAsync(
            new GameField(gameId, field, FieldSource.Staff, value, now, now), cancellationToken);

        if (existing is null || !string.Equals(existing.Value, value, StringComparison.Ordinal))
        {
            await fields.RecordChangeAsync(
                new FieldChange(gameId, field, FieldSource.Staff, existing?.Value, value, now),
                cancellationToken);
        }

        return existing?.Value;
    }


    /// <summary>
    /// The blank-input guard every staff tool (<see cref="GameFieldSetAsync"/>, <see cref="GameRenameAsync"/>,
    /// <see cref="GameMergeAsync"/>) needs on its required string parameters. A raw
    /// <see cref="ArgumentException"/> reaches a caller as the MCP SDK's generic "an error occurred" —
    /// see <see cref="ParseSeedOrThrow"/>'s own doc comment — so this throws <see cref="McpException"/>
    /// instead, the same way every other refusal in this class already does.
    /// </summary>
    private static void RequireNotBlank(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new McpException($"{parameterName} is required.");
        }
    }
}
