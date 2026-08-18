using System.Net;

using MUI.Catalog;

namespace MUI.Web.Api;

/// <summary>
/// A listing querystring with the parameters nobody asked for taken out.
/// </summary>
/// <remarks>
/// A browser submits every named control in a GET form regardless of value, and the facet panel runs
/// no script (spec §9) to omit blanks, so a reader who touches nothing still copies a URL full of
/// empty parameters. Fixed on arrival by <see cref="CanonicalListingUrls"/>, which redirects to
/// whatever this returns.
/// <b>The claim that makes it safe belongs to <see cref="GameFilterBinding"/>, not to this:</b> a
/// blank parameter is no selection there, so dropping it can't change which games come back — except
/// <c>codebase</c>, handled below.
/// <b>It filters and never rebuilds</b>, so a value is never re-encoded (which could rewrite
/// <c>%20</c> as <c>+</c> and bounce a correctly typed URL), and reaching a fixed point in one hop is
/// provable rather than hoped for.
/// </remarks>
public static class ListingQuery
{
    /// <summary>
    /// The same query with every parameter that selects nothing removed, leading <c>?</c> and all.
    /// </summary>
    /// <returns>
    /// A querystring beginning with <c>?</c>, or the empty string when nothing is left to ask.
    /// </returns>
    public static string Canonical(string? query)
    {
        var text = query is { Length: > 0 } && query[0] == '?' ? query[1..] : query;

        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var parts = text.Split('&');

        // Whether this query is a caller clearing the codebase filter — see ClearsCodebase.
        var clearing = Array.Exists(parts, ClearsCodebase);

        var kept = parts.Where(part => Keep(part, clearing)).ToArray();

        return kept.Length == 0 ? string.Empty : "?" + string.Join('&', kept);
    }

    private static bool Keep(string part, bool clearingCodebase) =>
        part.Length > 0
        && Value(part) is { } value
        && !string.IsNullOrWhiteSpace(value)
        && !IsDefaultSort(part, value)
        && !(clearingCodebase && Is(part, FacetKeys.CodebaseFamily));

    /// <summary>
    /// <c>?codebase=</c> — the one blank parameter on this site that says something.
    /// </summary>
    /// <remarks>
    /// <see cref="GameFilterBinding"/> reads <c>codebase</c> by presence, not content — dropping a
    /// blank <c>codebase=</c> alone would hand the query back to a stale <c>codebase-family</c> alias
    /// and re-apply the filter the reader just cleared, so the pair leaves together.
    /// </remarks>
    private static bool ClearsCodebase(string part) =>
        Is(part, FacetKeys.Codebase) && string.IsNullOrWhiteSpace(Value(part) ?? string.Empty);

    /// <summary>
    /// The order nobody chose. The <c>&lt;select&gt;</c> always submits something, and on a form
    /// nobody touched that something is the default — while an order a reader did pick is a real
    /// answer and stays in their URL.
    /// </summary>
    /// <remarks>The default is read off a fresh <see cref="GameFilter"/>, the same way <see cref="GameFilterBinding"/> does, so the two can't disagree about which URLs are equivalent.</remarks>
    private static bool IsDefaultSort(string part, string value) =>
        Is(part, FacetKeys.Sort)
        && FacetTokens.TrySort(value.Trim(), out var sort)
        && sort == new GameFilter().Sort;

    /// <summary>Whether a <c>key=value</c> pair names the given key, decoded and case-insensitively, matching how <c>QueryHelpers.ParseQuery</c> reads one.</summary>
    private static bool Is(string part, string key)
    {
        var end = part.IndexOf('=', StringComparison.Ordinal);

        return string.Equals(
            WebUtility.UrlDecode(end < 0 ? part : part[..end]),
            key,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The decoded value of a pair, or <see langword="null"/> for a key with no <c>=</c> at all.</summary>
    private static string? Value(string part)
    {
        var end = part.IndexOf('=', StringComparison.Ordinal);

        return end < 0 ? null : WebUtility.UrlDecode(part[(end + 1)..]);
    }
}
