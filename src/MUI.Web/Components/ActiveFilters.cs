using MUI.Catalog;
using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>One thing the query is currently asking for, and the URL that stops asking it.</summary>
/// <param name="Facet">Which question — <c>codebase</c>, <c>search</c>.</param>
/// <param name="Value">What it is asking of that question, polarity included.</param>
/// <param name="RemoveHref">The same listing with this one selection dropped and every other kept.</param>
public sealed record ActiveFilter(string Facet, string Value, string RemoveHref);

/// <summary>
/// Everything the current query asks for, read back out of the facets it was applied to.
/// </summary>
/// <remarks>
/// Rendered as a row of chips above the results, each a link that removes just that one filter —
/// the only affordance that can undo one selection without disturbing the rest. Built from
/// <see cref="FacetGroup"/> rather than <see cref="GameFilter"/>: the facets already carry each
/// value's <see cref="FacetState"/> from the same pass that produced the listing, so a chip cannot
/// claim a selection the query did not apply, and a new facet gets a chip automatically.
/// </remarks>
public static class ActiveFilters
{
    public static IReadOnlyList<ActiveFilter> For(
        string tag,
        IReadOnlyList<FacetGroup> facets,
        GameFilter filter,
        string? query)
    {
        ArgumentNullException.ThrowIfNull(facets);
        ArgumentNullException.ThrowIfNull(filter);

        var chips = new List<ActiveFilter>();

        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            chips.Add(new ActiveFilter(
                FacetWords.Group(tag, FacetKeys.Text),
                filter.Text.Trim(),
                Href(ListingLinks.With(query, FacetKeys.Text, null))));
        }

        var drawn = new HashSet<string>(StringComparer.Ordinal);

        foreach (var group in facets)
        {
            foreach (var value in group.Values.Where(v => v.State is not FacetState.Unselected))
            {
                drawn.Add(group.Key);

                chips.Add(new ActiveFilter(
                    FacetWords.Group(tag, group.Key),
                    value.State is FacetState.Excluded
                        ? FacetWords.Excluded(tag, group.Key, value)
                        : FacetWords.Value(tag, group.Key, value),

                    // A choice facet holds one selection, so removing it drops the parameter; a
                    // presence facet holds several in one repeatable, comma-separated parameter, so
                    // removing one has to leave the others behind.
                    Href(group.Kind is FacetKind.Choice
                        ? ListingLinks.With(query, group.Key, null)
                        : ListingLinks.Without(query, group.Key, value.Token))));
            }
        }

        // A selection the panel is not offering back. An open-ended facet's values come from what
        // is in the results, so a selection matching nothing left in them (e.g. ?codebase=!Evennia
        // returning no Evennia game) has no value to hang a chip on otherwise — the ordinary case,
        // not a corner, and exactly when the reader most needs a way to undo the filter.
        foreach (var (key, choice) in Open(filter))
        {
            if (choice is null || !drawn.Add(key))
            {
                continue;
            }

            var stand = new FacetValue(
                choice.Value ?? FacetChoice.UnknownToken,
                Count: 0,
                IsSelected: true,
                IsUnknown: choice.IsUnknown,
                IsExcluded: choice.Exclude);

            chips.Add(new ActiveFilter(
                FacetWords.Group(tag, key),
                choice.Exclude ? FacetWords.Excluded(tag, key, stand) : FacetWords.Value(tag, key, stand),
                Href(ListingLinks.With(query, key, null))));
        }

        // Last, since it widens rather than narrows — but present, since it's an active filter and
        // needs a way to be undone.
        var included = Messages.For(tag, "facet.value.included");

        if (filter.IncludeArchived)
        {
            chips.Add(new ActiveFilter(
                FacetWords.Group(tag, FacetKeys.Archived),
                included,
                Href(ListingLinks.With(query, FacetKeys.Archived, null))));
        }

        if (filter.IncludeAdult)
        {
            chips.Add(new ActiveFilter(
                FacetWords.Group(tag, FacetKeys.Adult),
                included,
                Href(ListingLinks.With(query, FacetKeys.Adult, null))));
        }

        return chips;
    }

    /// <summary>
    /// The facets whose vocabulary comes from the data rather than from an enum, which are the ones
    /// that can be asked for a value the current results do not contain.
    /// </summary>
    /// <remarks>
    /// The two derived facets are not here: their values are a fixed vocabulary and a selected one
    /// stays in the panel at a count of zero, so it always has a chip already.
    /// </remarks>
    private static IEnumerable<(string Key, FacetChoice? Choice)> Open(GameFilter filter) =>
    [
        (FacetKeys.Charset, filter.Charset),
        (FacetKeys.Codebase, filter.Codebase),
        (FacetKeys.CodebaseVersion, filter.CodebaseVersion),
        (FacetKeys.Family, filter.Family),
        (FacetKeys.Genre, filter.Genre),
        (FacetKeys.Language, filter.Language),
    ];

    private static string Href(string queryString) => "/games" + queryString;
}
