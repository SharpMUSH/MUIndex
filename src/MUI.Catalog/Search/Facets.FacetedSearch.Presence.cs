namespace MUI.Catalog;

/// <summary>
/// The presence-facet half of <see cref="FacetedSearch"/> — the facets that intersect rather than
/// choose, built from what a handshake actually offered (spec §9).
/// </summary>
public static partial class FacetedSearch
{
    private static IEnumerable<FacetGroup> Presence(IReadOnlyList<GameFacetRow> results, GameFilter filter)
    {
        var protocols = results
            .SelectMany(r => r.Summary.MeasuredProtocols)
            .GroupBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .ToList();

        // A selected protocol has already narrowed the results, so every remaining game has it: its
        // count is the listing's own size, which is what unchecking it would leave in place.
        var values = protocols
            .Select(p => new FacetValue(
                p.Name,
                Selected(p.Name) ? results.Count : p.Count,
                Selected(p.Name),
                IsUnknown: false))
            .Concat(filter.MeasuredProtocols
                .Where(p => !protocols.Any(known => string.Equals(known.Name, p, StringComparison.OrdinalIgnoreCase)))
                .Select(p => new FacetValue(p, 0, IsSelected: true, IsUnknown: false)))
            .OrderByDescending(v => v.Count)
            .ThenBy(v => v.Token, StringComparer.Ordinal)
            .ToList();

        if (values.Count > 0)
        {
            yield return new FacetGroup(
                FacetKeys.Protocol, FacetEvidence.Measured, FacetKind.Presence, results.Count, values);
        }

        var tls = filter.Tls ? results.Count : results.Count(r => r.TlsMeasured);

        // Rendered only when something was measured. Nothing writes a TLS endpoint today — the
        // crawler dials plaintext — so this group is normally absent, which is the honest rendering
        // of a measurement nobody has taken. It must never be filled in from MSSP's SSL line.
        if (tls > 0 || filter.Tls)
        {
            yield return new FacetGroup(
                FacetKeys.Tls,
                FacetEvidence.Measured,
                FacetKind.Presence,
                results.Count,
                [new FacetValue(FacetTokens.Yes, tls, filter.Tls, IsUnknown: false)]);
        }

        bool Selected(string protocol) =>
            filter.MeasuredProtocols.Contains(protocol, StringComparer.OrdinalIgnoreCase);
    }
}
