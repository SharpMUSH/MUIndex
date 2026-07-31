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
/// This is a seam and not a store. The alias table belongs beside the games in Postgres, written by
/// whatever re-mints a slug; until that exists an operator can carry a rename in configuration,
/// which is enough to keep a promise about a handful of URLs and no more.
/// </para>
/// </remarks>
public interface ISlugHistory
{
    /// <summary>The slug a former one now redirects to, or null if we have no record of it.</summary>
    Task<string?> CurrentSlugAsync(string formerSlug, CancellationToken cancellationToken = default);
}

/// <summary>Slug aliases from configuration: <c>SlugAliases:{former} = {current}</c>.</summary>
public sealed class ConfiguredSlugHistory(IOptions<SlugAliasOptions> options) : ISlugHistory
{
    public Task<string?> CurrentSlugAsync(
        string formerSlug, CancellationToken cancellationToken = default) =>
        Task.FromResult(
            options.Value.Aliases.TryGetValue(formerSlug, out var current) ? current : null);
}

public sealed class SlugAliasOptions
{
    public const string Section = "SlugAliases";

    /// <summary>Case-insensitive: a URL somebody typed in mixed case is the same URL.</summary>
    public Dictionary<string, string> Aliases { get; } =
        new(StringComparer.OrdinalIgnoreCase);
}
