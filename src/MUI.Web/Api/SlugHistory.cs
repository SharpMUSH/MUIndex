using MUI.Catalog.Persistence;

using Microsoft.Extensions.Options;

namespace MUI.Web.Api;

/// <summary>
/// Where a slug a game used to have now points (spec §5.7).
/// </summary>
/// <remarks>
/// A slug that once worked has to keep working forever, exactly as an archived game's page does.
/// <b>The store is <c>game_slug_history</c></b>, written by <c>SlugMinter</c> when a game's declared
/// name has held for a grace period; a row names a game rather than another slug, so a redirect cycle
/// can't be expressed. <b>Configuration remains the answer where there is no database</b> —
/// <see cref="StoredSlugHistory"/> has it behind the table, not instead of it.
/// </remarks>
public interface ISlugHistory
{
    /// <summary>The slug a former one now redirects to, or null if we have no record of it.</summary>
    Task<string?> CurrentSlugAsync(string formerSlug, CancellationToken cancellationToken = default);
}

/// <summary>
/// The former-slug table, with configuration behind it (spec §5.7).
/// </summary>
/// <remarks>The table answers first because it's the measured record, written by the rename itself; a configured alias is somebody's older recollection of the same event.</remarks>
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

        // A hand-written alias can point at itself, which would be a redirect loop. Ordinal, so an
        // alias that exists to fix the *case* of a URL still works.
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
