using Microsoft.AspNetCore.WebUtilities;
using MUI.Catalog;

namespace MUI.Web.Components;

/// <summary>
/// The listing's own URL, rewritten one parameter at a time.
/// </summary>
/// <remarks>
/// <para>
/// The querystring is the whole of this page's state, so every affordance that is not a form control
/// — read this as plain text, surprise me, stop asking for Evennia — is a link to the same URL with
/// one parameter changed. That rewriting is here, once, rather than in each page that needs it: the
/// listing already carried a private copy for the plain-text link, and the moment a second caller
/// wanted "the same query without this facet" the two would have had to agree about repeated
/// parameters, comma-separated values and escaping without ever being compared.
/// </para>
/// <para>
/// Both return a querystring — leading <c>?</c> or empty — and never a path, because the callers
/// point at three different ones (<c>/games</c>, <c>/games/random</c>, and the page itself).
/// </para>
/// </remarks>
public static class ListingLinks
{
    /// <summary>The same query with one parameter set, replaced, or — on a null value — removed.</summary>
    /// <remarks>
    /// A parameter's aliases go with it. <c>codebase-family</c> is the old spelling of
    /// <c>codebase</c> and both bind to one filter, so rewriting the one and leaving the other would
    /// make a chip's remove-link a no-op for exactly the readers who arrived from a codebase
    /// reference page — the query would still carry the value the chip said it had dropped.
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
    /// A presence facet is repeatable <em>and</em> comma-separated, because both are what people
    /// type (see <c>GameFilterBinding.Protocols</c>), so removing one protocol from
    /// <c>?protocol=GMCP,MSSP</c> has to rewrite the value rather than delete the parameter. Dropping
    /// the parameter would silently take MSSP off the query as well, and the chip the reader clicked
    /// said nothing about MSSP.
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
    private static IReadOnlyList<string> Names(string name) =>
        string.Equals(name, FacetKeys.Codebase, StringComparison.OrdinalIgnoreCase)
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
