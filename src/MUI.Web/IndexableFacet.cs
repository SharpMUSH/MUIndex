using MUI.Catalog;

namespace MUI.Web;

/// <summary>
/// The listing filters that earn a page of their own in a search index.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> <see cref="SiteUrls.CanonicalOf"/> drops the querystring, which is right
/// for the reason written there — the facet panel's GET form makes every filter/sort/exclude
/// combination another URL, and an unbounded supply of near-duplicates is exactly what a canonical
/// link is for. But it was applied to <em>every</em> listing URL, and that swept up the handful that
/// are not near-duplicates at all: <c>/games?codebase=PennMUSH</c> is the answer to "which games run
/// PennMUSH", a different question from "which games are there" and a different set of rows. Every
/// one of them declared <c>/games</c> as its canonical and carried <c>/games</c>'s title and
/// description, so a search engine was being told, correctly by its own rules, to index none of them.
/// </para>
/// <para>
/// <b>What separates the two.</b> A category is one included value of one dimension, at the default
/// sort, with nothing else asked. Everything else — two facets at once, an excluded value
/// (<c>?codebase=!Evennia</c>), the unknown token, a chosen sort, free text — stays consolidated onto
/// <c>/games</c>, because those are refinements of a category rather than categories, and they are
/// where the combinatorial explosion lives. That keeps the indexable set at roughly the sum of the
/// dimensions' value counts rather than their product.
/// </para>
/// <para>
/// <b>The dimensions are a whitelist, not everything filterable.</b> Each one here is a durable
/// property of a game that somebody types into a search engine — a codebase, a lineage, a genre, a
/// language, a protocol. <see cref="FacetKeys.Band"/>, <see cref="FacetKeys.LastSeen"/>,
/// <see cref="FacetKeys.Trending"/> and the two measurement switches are deliberately absent: they
/// read a measurement that moves, so the page behind such a URL is a different set of games next
/// week, and an index entry for it would be wrong more often than right.
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
    /// <see cref="FacetKeys.CodebaseVersion"/> is not here on purpose: one page per patchlevel is
    /// hundreds of near-identical listings, which is the thing this type exists to avoid.
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
    /// Read off the parsed <see cref="GameFilter"/> rather than off the querystring, so the old
    /// <c>?codebase-family=</c> spelling and the current one reach the same answer, and so a
    /// parameter that selects nothing cannot disqualify a page by being present.
    /// </remarks>
    public static Category? Of(GameFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        // Everything a bare /games does not ask. Any of these set means this is a refinement, and a
        // refinement consolidates onto /games.
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
            || filter.Sort != Default.Sort)
        {
            return null;
        }

        // At most one protocol, and it counts as one of the selections below.
        if (filter.MeasuredProtocols.Count > 1)
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

            // Two dimensions at once is a refinement, not a category.
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
    /// links to, and without this both would be self-canonical — two indexed URLs for one page, which
    /// is the duplication this whole type is trying to prevent. Matched against the facet groups the
    /// same pass produced, so the spelling can only ever be one the site itself links to. A value the
    /// panel does not offer (an open-ended facet only publishes its commonest values) is left as it
    /// was rather than dropped: it still selects a real, distinct set of games.
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
    /// <see cref="GameFilter.IncludeAdult"/> defaults to <c>true</c> on the record and to <c>false</c>
    /// on the listing surface (see <c>GameFilterBinding</c>), so the baseline is written the
    /// surface's way rather than the record's.
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
    /// An exclusion and the unknown token both return null rather than a category: "every game that
    /// is not Evennia" and "every game whose codebase we could not read" are real questions the
    /// listing answers, and neither is a thing anybody searches for by name.
    /// </remarks>
    private static Category? Included(string key, FacetChoice? choice) =>
        choice is { Exclude: false, Value: { } value } && !string.IsNullOrWhiteSpace(value)
            ? new Category(key, value)
            : null;
}
