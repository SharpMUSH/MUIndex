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
/// </remarks>
public static class IndexableFacet
{
    /// <summary>One indexable listing: the dimension asked about, and the value asked for.</summary>
    /// <param name="Key">The querystring spelling of the dimension, from <see cref="FacetKeys"/>.</param>
    /// <param name="Value">The value, in the spelling the catalogue itself publishes.</param>
    public sealed record Category(string Key, string Value);

    /// <summary>
    /// The dimensions whose values get a page of their own, in the order a sitemap lists them.
    /// </summary>
    /// <remarks>
    /// Each is a durable property somebody types into a search engine. <see cref="FacetKeys.Band"/>,
    /// <see cref="FacetKeys.LastSeen"/>, <see cref="FacetKeys.Trending"/> and the two measurement
    /// switches are absent because they read a measurement that moves — the page behind such a URL
    /// holds a different set of games next week. <see cref="FacetKeys.CodebaseVersion"/> is absent
    /// because one page per patchlevel is hundreds of near-identical listings.
    /// </remarks>
    public static IReadOnlyList<string> Dimensions { get; } =
    [
        FacetKeys.Codebase,
        FacetKeys.Lineage,
        FacetKeys.Genre,
        FacetKeys.Language,
        FacetKeys.Protocol,
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
            || filter.Tls
            || filter.Band is not null
            || filter.LastSeen is not null
            || filter.Uncounted is not null
            || filter.Unreachable is not null
            || filter.Charset is not null
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

        yield return filter.MeasuredProtocols.Count == 1
            ? new Category(FacetKeys.Protocol, filter.MeasuredProtocols[0])
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
