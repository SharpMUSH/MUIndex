namespace MUI.Catalog;

/// <summary>
/// The choice-facet half of <see cref="FacetedSearch"/> that turns one facet's rows into its
/// offered values — a fixed, ordered vocabulary (<see cref="Bounded"/>) or an open, popularity-capped
/// one (<see cref="Open"/>) — plus the shared counting pass (<see cref="Counts"/>) both read from.
/// </summary>
public static partial class FacetedSearch
{
    /// <summary>
    /// A fixed vocabulary, kept in its declared order because that order is a scale — and kept
    /// whole, because a scale with a rung missing is not the same scale.
    /// </summary>
    /// <remarks>
    /// Every value stays, including ones that return nothing. Dropping empty rows (as an open-ended
    /// facet does) previously meant filtering an unrelated facet could delete rows from a bounded
    /// one, shift the panel, and hide thresholds the reader hadn't chosen — a zero row is a real
    /// answer and stays clickable.
    /// <paramref name="catalogue"/>, not <paramref name="domain"/>, decides which rungs exist: it's
    /// the listing before any facet selection but after text search and the archived/adult
    /// switches — "the catalogue I'm looking at". <paramref name="domain"/> only supplies the
    /// counts.
    /// </remarks>
    private static List<FacetValue> Bounded(
        IReadOnlyList<IndexedRow> domain,
        IReadOnlyList<IndexedRow> catalogue,
        int facet,
        IReadOnlyList<string> vocabulary,
        FacetChoice? selection)
    {
        var counts = Counts(domain, facet);
        var ever = Counts(catalogue, facet);

        return
        [
            .. vocabulary
                .Select(token => new FacetValue(
                    token,
                    counts.GetValueOrDefault(token)?.Count ?? 0,
                    selection?.Covers(token) ?? false,
                    IsUnknown: false,
                    IsExcluded: (selection?.Covers(token) ?? false) && selection!.Exclude))
                .Where(v => (ever.GetValueOrDefault(v.Token)?.Count ?? 0) > 0 || v.IsSelected),
        ];
    }

    /// <summary>
    /// An open-ended vocabulary — codebases, genres — ordered by how much of the catalogue each
    /// covers, capped, with the unknown bucket kept whatever it weighs.
    /// </summary>
    /// <remarks>
    /// The unknown bucket survives the popularity cap deliberately — "codebase we couldn't identify"
    /// is a useful measurement and exactly what a cap would otherwise delete.
    /// The reader's own selection also survives, at zero if necessary: since the domain has that
    /// facet's own selection lifted, a combination like
    /// <c>genre=Historical&amp;language=Swedish</c> can leave the chosen value out of the counts
    /// entirely — dropping it from the panel would remove the only control that could undo the
    /// choice responsible for an empty listing.
    /// </remarks>
    private static List<FacetValue> Open(
        IReadOnlyList<IndexedRow> domain,
        int facet,
        FacetChoice? selection)
    {
        var counts = Counts(domain, facet);

        var named = counts
            .Where(c => !string.Equals(c.Key, FacetChoice.UnknownToken, StringComparison.Ordinal))
            .Select(c => new FacetValue(
                c.Value.Label,
                c.Value.Count,
                selection?.Covers(c.Key) ?? false,
                IsUnknown: false,
                IsExcluded: (selection?.Covers(c.Key) ?? false) && selection!.Exclude))
            .OrderByDescending(v => v.IsSelected)
            .ThenByDescending(v => v.Count)
            .ThenBy(v => v.Token, StringComparer.Ordinal)
            .Take(MaxValues)
            .OrderByDescending(v => v.Count)
            .ThenBy(v => v.Token, StringComparer.Ordinal)
            .ToList();

        if (selection is { Value: { } chosen }
            && !named.Any(v => string.Equals(v.Token, chosen, StringComparison.OrdinalIgnoreCase)))
        {
            named.Add(new FacetValue(
                chosen, 0, IsSelected: true, IsUnknown: false, IsExcluded: selection.Exclude));
        }

        var unknown = counts.GetValueOrDefault(FacetChoice.UnknownToken)?.Count ?? 0;
        var unknownSelected = selection?.IsUnknown ?? false;

        if (unknown > 0 || unknownSelected)
        {
            named.Add(new FacetValue(
                FacetChoice.UnknownToken,
                unknown,
                unknownSelected,
                IsUnknown: true,
                IsExcluded: unknownSelected && selection!.Exclude));
        }

        return named;
    }

    /// <summary>
    /// How many games each value covers, and every spelling they used for it.
    /// </summary>
    /// <remarks>
    /// Count each distinct spelling without retaining one entry per game. Labels use the same
    /// frequency and ordinal tie-break as <see cref="Spellings.Commonest"/>.
    /// </remarks>
    private static Dictionary<string, ValueCount> Counts(
        IReadOnlyList<IndexedRow> domain,
        int facet)
    {
        var counts = new Dictionary<string, ValueCount>(StringComparer.OrdinalIgnoreCase);

        for (var row = 0; row < domain.Count; row++)
        {
            var tokens = domain[row].Tokens[facet];
            for (var t = 0; t < tokens.Count; t++)
            {
                var key = tokens[t] ?? FacetChoice.UnknownToken;

                if (!counts.TryGetValue(key, out var spellings))
                {
                    counts[key] = spellings = new ValueCount();
                }

                spellings.Add(key);
            }
        }

        return counts;
    }

    private sealed class ValueCount
    {
        private readonly Dictionary<string, int> _spellings = new(StringComparer.Ordinal);
        private int _highest;
        public int Count { get; private set; }
        public string Label { get; private set; } = string.Empty;

        public void Add(string spelling)
        {
            Count++;
            var frequency = _spellings.GetValueOrDefault(spelling) + 1;
            _spellings[spelling] = frequency;
            if (frequency > _highest || (frequency == _highest
                && StringComparer.Ordinal.Compare(spelling, Label) < 0))
            {
                Label = spelling;
                _highest = frequency;
            }
        }
    }
}
