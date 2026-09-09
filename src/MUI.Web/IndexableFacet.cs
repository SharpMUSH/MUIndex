using MUI.Catalog;

namespace MUI.Web;

/// <summary>
/// The listing filters that earn a page of their own in a search index.
/// </summary>
/// <remarks>
/// <see cref="SiteUrls.CanonicalOf"/> drops the querystring, which is right for the reason written
/// there — the facet panel submits every control it has, so filter/sort combinations are unbounded.
/// Applied to every listing URL it also swept up the ones that are not near-duplicates:
/// <c>/games?codebase=PennMUSH</c> is a different question over a different set of rows, and every
/// such page was declaring itself a duplicate of <c>/games</c>.
/// <para>
/// One included value of one whitelisted dimension at the default sort is a category. Two facets, an
/// exclusion, the unknown token, a chosen sort or free text are refinements and stay consolidated —
/// which keeps the indexable set at the sum of the dimensions' values rather than their product.
/// </para>
/// <para>
/// <b>Being a category and being indexable are two different questions.</b> Naming the page is free
/// and right for any single facet: a reader who filtered to one thing should see that thing in the
/// heading. Putting it in a search index is a claim that the page is worth returning next week, and
/// a facet reading a measurement that moves — <see cref="FacetKeys.Band"/> — holds a different set of
/// games by then. Those get the heading and no canonical URL of their own.
/// </para>
/// </remarks>
public static class IndexableFacet
{
    /// <summary>One listing that is about something: the dimension asked about, and the value.</summary>
    /// <param name="Key">The querystring spelling of the dimension, from <see cref="FacetKeys"/>.</param>
    /// <param name="Value">The value, in the spelling the catalogue itself publishes.</param>
    public sealed record Category(string Key, string Value)
    {
        /// <summary>Whether this category still holds the same games next week, and so may be indexed.</summary>
        public bool IsStable => Indexable.Contains(Key);
    }

    /// <summary>
    /// The dimensions a listing can be about, and so be named after.
    /// </summary>
    /// <remarks>
    /// <see cref="FacetKeys.CodebaseVersion"/> is absent because one page per patchlevel is hundreds
    /// of near-identical listings, and <see cref="FacetKeys.Family"/> because it would draw a second
    /// heading over nearly the same games as <see cref="FacetKeys.Lineage"/>. The rest of what is
    /// missing — last seen, trending, the two measurement switches — has no heading anybody would
    /// want to read.
    /// </remarks>
    public static IReadOnlyList<string> Dimensions { get; } =
    [
        FacetKeys.Codebase,
        FacetKeys.Lineage,
        FacetKeys.Genre,
        FacetKeys.Language,
        FacetKeys.Protocol,
        FacetKeys.Charset,
        FacetKeys.Tls,
        FacetKeys.Band,
    ];

    /// <summary>
    /// The dimensions durable enough to be a page in a search index, in the order a sitemap lists them.
    /// </summary>
    /// <remarks>
    /// Everything in <see cref="Dimensions"/> except <see cref="FacetKeys.Band"/>, which reads how
    /// busy a game is right now: a URL for it names a set that has already changed by the time a
    /// crawler returns to it.
    /// </remarks>
    public static IReadOnlyList<string> Indexable { get; } =
    [
        .. Dimensions.Where(key => key != FacetKeys.Band),
    ];

    /// <summary>
    /// The category this filter names, or <see langword="null"/> where it names none.
    /// </summary>
    /// <remarks>
    /// Read off the parsed <see cref="GameFilter"/> rather than the querystring, so the old
    /// <c>?codebase-family=</c> spelling reaches the same answer and a parameter that selects nothing
    /// cannot disqualify a page.
    /// </remarks>
    public static Category? Of(GameFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (filter.Text is not null
            || filter.IncludeArchived
            || filter.IncludeAdult
            || filter.LastSeen is not null
            || filter.Uncounted is not null
            || filter.Unreachable is not null
            || filter.CodebaseVersion is not null
            || filter.Family is not null
            || filter.Trending is not null
            || filter.Sort != Default.Sort
            || filter.MeasuredProtocols.Count > 1)
        {
            return null;
        }

        Category? found = null;

        foreach (var candidate in Candidates(filter))
        {
            if (candidate is null)
            {
                continue;
            }

            if (found is not null)
            {
                return null;
            }

            found = candidate;
        }

        return found;
    }

    /// <summary>
    /// Whether this filter selects nothing, so the rows drawn are the whole listing.
    /// </summary>
    /// <remarks>
    /// Asked so a page can tell whether it may publish its rows as <c>/games</c>. Written against
    /// <see cref="Default"/> rather than member by member on purpose: a filter member added later and
    /// not thought about here makes this <see langword="false"/>, which withholds a graph rather than
    /// publishing a wrong one.
    /// </remarks>
    public static bool IsUnfiltered(GameFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        // Neutralised because record equality compares the list by reference, not by contents.
        return filter.MeasuredProtocols.Count == 0
            && filter with { MeasuredProtocols = Default.MeasuredProtocols } == Default;
    }

    /// <summary>The querystring for a category — leading <c>?</c>, escaped, and nothing else in it.</summary>
    public static string Query(Category category)
    {
        ArgumentNullException.ThrowIfNull(category);

        return $"?{Uri.EscapeDataString(category.Key)}={Uri.EscapeDataString(category.Value)}";
    }

    /// <summary>
    /// The same category with its value respelled as the catalogue publishes it.
    /// </summary>
    /// <remarks>
    /// A hand-typed <c>?codebase=pennmush</c> selects the same games as the <c>PennMUSH</c> the panel
    /// links to; without this both would be self-canonical, which is the duplication this type exists
    /// to prevent. A value the panel does not offer — an open-ended facet only publishes its
    /// commonest — is left as it was rather than dropped: it still selects a real set of games.
    /// </remarks>
    public static Category AsPublished(Category category, IReadOnlyList<FacetGroup> facets)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(facets);

        var group = facets.FirstOrDefault(
            g => string.Equals(g.Key, category.Key, StringComparison.OrdinalIgnoreCase));

        var token = group?.Values.FirstOrDefault(
            v => !v.IsUnknown
                && string.Equals(v.Token, category.Value, StringComparison.OrdinalIgnoreCase));

        return token is null ? category : category with { Value = token.Token };
    }

    /// <summary>The filter a bare <c>/games</c> produces, which every category differs from in one place.</summary>
    /// <remarks>
    /// <see cref="GameFilter.IncludeAdult"/> written the listing surface's way rather than the
    /// record's — see <c>GameFilterBinding</c>.
    /// </remarks>
    private static readonly GameFilter Default = new() { IncludeAdult = false };

    private static IEnumerable<Category?> Candidates(GameFilter filter)
    {
        yield return Included(FacetKeys.Codebase, filter.Codebase);
        yield return Included(FacetKeys.Lineage, filter.Lineage);
        yield return Included(FacetKeys.Genre, filter.Genre);
        yield return Included(FacetKeys.Language, filter.Language);

        yield return Included(FacetKeys.Charset, filter.Charset);

        yield return filter.MeasuredProtocols.Count == 1
            ? new Category(FacetKeys.Protocol, filter.MeasuredProtocols[0])
            : null;

        // Both are switches rather than open-ended values: the querystring spelling is the one the
        // panel links, so a category built from either round-trips to the same filter.
        yield return filter.Tls ? new Category(FacetKeys.Tls, FacetTokens.Yes) : null;

        yield return filter.Band is { } band
            ? new Category(FacetKeys.Band, FacetTokens.Of(band))
            : null;
    }

    /// <summary>
    /// A facet selection as a category, or nothing.
    /// </summary>
    /// <remarks>
    /// An exclusion and the unknown token are real questions the listing answers, and neither is a
    /// thing anybody searches for by name.
    /// </remarks>
    private static Category? Included(string key, FacetChoice? choice) =>
        choice is { Exclude: false, Value: { } value } && !string.IsNullOrWhiteSpace(value)
            ? new Category(key, value)
            : null;
}
