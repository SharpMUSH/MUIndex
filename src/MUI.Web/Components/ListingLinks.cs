using Microsoft.AspNetCore.WebUtilities;
using MUI.Catalog;

namespace MUI.Web.Components;

/// <summary>
/// The listing's own URL, rewritten one parameter at a time.
/// </summary>
/// <remarks>
/// The querystring is the whole of this page's state, so every non-form affordance is a link to the
/// same URL with one parameter changed — rewritten here once so callers agree about repeated
/// parameters, comma-separated values and escaping.
/// <para>
/// Both return a querystring — leading <c>?</c> or empty — and never a path, because callers point
/// at three different ones (<c>/games</c>, <c>/games/random</c>, and the page itself).
/// </para>
/// </remarks>
public static class ListingLinks
{
    /// <summary>The same query with one parameter set, replaced, or — on a null value — removed.</summary>
    /// <remarks>
    /// A parameter's aliases go with it: <c>codebase-family</c> is the old spelling of
    /// <c>codebase</c>, so rewriting one and leaving the other would make a chip's remove-link a
    /// no-op for readers who arrived via the old spelling.
    /// </remarks>
    public static string With(string? query, string name, string? value)
    {
        var parts = Pairs(query)
            .Where(p => !Names(name).Contains(p.Key, StringComparer.OrdinalIgnoreCase))
            .Select(Encode)
            .ToList();

        if (value is not null)
        {
            parts.Add($"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}");
        }

        return Join(parts);
    }

    /// <summary>
    /// The same query with one facet's selection dropped — the whole parameter, or one value of it.
    /// </summary>
    /// <remarks>
    /// A presence facet is repeatable and comma-separated, so removing one protocol from
    /// <c>?protocol=GMCP,MSSP</c> has to rewrite the value, not delete the parameter — deleting it
    /// would silently drop MSSP too, which the reader never asked for.
    /// </remarks>
    public static string Without(string? query, string name, string? value)
    {
        if (value is null)
        {
            return With(query, name, null);
        }

        var parts = new List<string>();

        foreach (var pair in Pairs(query))
        {
            if (!string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(Encode(pair));
                continue;
            }

            var kept = pair.Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(v => !string.Equals(v, value, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (kept.Count > 0)
            {
                parts.Add(Encode(new KeyValuePair<string, string>(pair.Key, string.Join(',', kept))));
            }
        }

        return Join(parts);
    }

    /// <summary>Every spelling of one parameter, so setting or clearing it clears them all.</summary>
    /// <remarks>
    /// Symmetric, because a one-directional alias isn't an alias: naming the old spelling used to
    /// clear only the old spelling and leave the canonical one applied.
    /// </remarks>
    private static IReadOnlyList<string> Names(string name) =>
        string.Equals(name, FacetKeys.Codebase, StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, FacetKeys.CodebaseFamily, StringComparison.OrdinalIgnoreCase)
            ? [FacetKeys.Codebase, FacetKeys.CodebaseFamily]
            : [name];

    private static IEnumerable<KeyValuePair<string, string>> Pairs(string? query) =>
        QueryHelpers.ParseQuery(query ?? string.Empty)
            .SelectMany(p => p.Value.Select(v => new KeyValuePair<string, string>(p.Key, v ?? string.Empty)));

    private static string Encode(KeyValuePair<string, string> pair) =>
        $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}";

    private static string Join(List<string> parts) =>
        parts.Count == 0 ? string.Empty : "?" + string.Join('&', parts);
}
