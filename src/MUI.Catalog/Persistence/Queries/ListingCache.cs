using ZiggyCreatures.Caching.Fusion;

namespace MUI.Catalog.Persistence;

/// <summary>
/// Drops the assembled catalogue the listing is served from.
/// </summary>
/// <remarks>
/// <para>
/// The listing does not query per request: <see cref="NpgsqlGameQueries.SearchAsync"/> assembles the
/// whole catalogue as facet rows and <see cref="FacetedSearch"/> applies every facet to them in
/// memory, so the expensive half is shared by every URL and is cached. That cache has a duration, but
/// a duration is the wrong answer for a change somebody just made on purpose — a staff rename should
/// be on the listing when the page is reloaded, not up to a minute later.
/// </para>
/// <para>
/// An interface rather than the cache itself, so the callers that write — the staff tools in
/// <c>MUI.Web</c> — depend on "the listing has changed" rather than on which cache library is behind
/// it. It is deliberately not on <see cref="IGameQueries"/>: that is the read surface, and a mutation
/// hanging off it would be the first thing to confuse the next reader.
/// </para>
/// <para>
/// <b>This is for deliberate, human-initiated edits only.</b> The crawler writes
/// <c>game_field</c> rows continuously as it probes, and invalidating on those would mean assembling
/// the catalogue several times a second — a cache that is never warm during a crawl cycle, which is
/// exactly when the site is busiest. Routine measurement reaches the listing when the duration
/// lapses, the same way it always has.
/// </para>
/// </remarks>
public interface IListingCache
{
    Task InvalidateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IListingCache"/> over FusionCache's tagging.
/// </summary>
/// <remarks>
/// One tag rather than a key per entry: the catalogue is cached under a handful of keys (the archive
/// toggle by the window a sort reads) and a rename can change what any of them contains, so the
/// invalidation has to reach all of them without the caller knowing how many there are. FusionCache
/// resolves a tag by writing a timestamp and comparing entries against it, so this stays a single
/// cheap write however many entries are live.
/// </remarks>
public sealed class ListingCache(IFusionCache cache) : IListingCache
{
    /// <summary>The tag every assembled catalogue is filed under.</summary>
    internal const string Tag = "mui:catalogue";

    public async Task InvalidateAsync(CancellationToken cancellationToken = default) =>
        await cache.RemoveByTagAsync(Tag, token: cancellationToken);
}
