using MUI.Catalog.Persistence;

using Microsoft.Extensions.Options;

namespace MUI.Web.Api;

/// <summary>
/// Where a slug a game used to have now points (spec §5.7).
/// </summary>
/// <remarks>
/// <para>
/// A game's GUID is immutable and its slug is not, because games rename themselves — so a slug that
/// once worked has to keep working forever, exactly as an archived game's page does. Nothing is ever
/// deleted here, and a URL is a thing somebody else is holding.
/// </para>
/// <para>
/// <b>The store is <c>game_slug_history</c>, beside the games</b>, written by the only thing that
/// re-mints a slug: <c>SlugMinter</c>, when a game's declared name has held for a grace period. A row
/// there names a game rather than another slug, so a game renamed twice redirects from its oldest URL
/// in one hop and a redirect cycle cannot be expressed.
/// </para>
/// <para>
/// <b>Configuration remains the answer where there is no database.</b> <c>MUI.Web</c> starts on the
/// demo fixture with no Postgres at all, and an operator carrying a rename by hand — for a URL that
/// moved before this table existed, or one no probe can know about — is still a legitimate thing to
/// do. <see cref="StoredSlugHistory"/> is the table with that behind it, not instead of it.
/// </para>
/// </remarks>
public interface ISlugHistory
{
    /// <summary>The slug a former one now redirects to, or null if we have no record of it.</summary>
    Task<string?> CurrentSlugAsync(string formerSlug, CancellationToken cancellationToken = default);
}

/// <summary>
/// The former-slug table, with configuration behind it (spec §5.7).
/// </summary>
/// <remarks>
/// The table answers first because it is the measured record: it was written by the rename itself,
/// and a configured alias for the same URL is somebody's older recollection of the same event.
/// </remarks>
public sealed class StoredSlugHistory(ISlugHistoryStore store, ISlugHistory configured) : ISlugHistory
{
    public async Task<string?> CurrentSlugAsync(
        string formerSlug, CancellationToken cancellationToken = default) =>
        await store.CurrentSlugAsync(formerSlug, cancellationToken)
        ?? await configured.CurrentSlugAsync(formerSlug, cancellationToken);
}

/// <summary>Slug aliases from configuration: <c>SlugAliases:{former} = {current}</c>.</summary>
public sealed class ConfiguredSlugHistory(IOptions<SlugAliasOptions> options) : ISlugHistory
{
    public Task<string?> CurrentSlugAsync(
        string formerSlug, CancellationToken cancellationToken = default)
    {
        if (!options.Value.Aliases.TryGetValue(formerSlug, out var current))
        {
            return Task.FromResult<string?>(null);
        }

        // A hand-written alias is the one kind that can point at itself, and a 301 to the URL that was
        // asked for is a redirect loop a reader cannot escape. Ordinal, so an alias that exists to fix
        // the *case* of a URL still works.
        return Task.FromResult(
            string.Equals(current, formerSlug, StringComparison.Ordinal) ? null : current);
    }
}

public sealed class SlugAliasOptions
{
    public const string Section = "SlugAliases";

    /// <summary>Case-insensitive: a URL somebody typed in mixed case is the same URL.</summary>
    public Dictionary<string, string> Aliases { get; } =
        new(StringComparer.OrdinalIgnoreCase);
}
