using System.Net;

using MUI.Catalog;

namespace MUI.Web.Api;

/// <summary>
/// A listing querystring with the parameters nobody asked for taken out.
/// </summary>
/// <remarks>
/// <para>
/// <b>A browser submits every named control in a GET form, whether or not it holds anything.</b> The
/// facet panel has nine <c>&lt;select&gt;</c>s sitting on "any" and a search box that is usually
/// empty, so pressing <b>show</b> having touched nothing produced 117 characters of
/// <c>?q=&amp;sort=players&amp;band=&amp;seen=&amp;…</c> — and that is the URL a reader then copies
/// to somebody else. <c>/find</c>'s wizard does the same through its "doesn't matter" radios.
/// </para>
/// <para>
/// There is no markup that says "submit this control only if it holds something", and the panel runs
/// no script (spec §9), so it cannot be fixed where it is made. It is fixed on arrival:
/// <see cref="CanonicalListingUrls"/> redirects to whatever this returns.
/// </para>
/// <para>
/// <b>The claim that makes it safe belongs to <see cref="GameFilterBinding"/>, not to this:</b> a
/// blank parameter is no selection there — <c>Choice</c> returns null on blank, <c>TryBand</c>,
/// <c>TryLastSeen</c> and <c>TrySort</c> fall through to their defaults. Dropping the blanks
/// therefore cannot change which games come back. The one place that sentence is false is
/// <c>codebase</c>, and it is handled below.
/// </para>
/// <para>
/// <b>It filters and never rebuilds.</b> The result is a subsequence of the input, so a value is
/// never re-encoded — a canonicaliser that parsed and reassembled would rewrite <c>%20</c> as
/// <c>+</c> and bounce a URL somebody had typed correctly — and reaching a fixed point in one hop is
/// provable rather than hoped for, which is what rules out a redirect loop.
/// </para>
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

        // Whether this query is a caller *clearing* the codebase filter, which is the one thing here
        // a blank parameter can mean. See ClearsCodebase.
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
    /// <b><see cref="GameFilterBinding"/> reads <c>codebase</c> by presence, not by content</b>,
    /// because <c>codebase-family</c> is the old spelling that every codebase reference page still
    /// links with: <c>?codebase=&amp;codebase-family=PennMUSH</c> is a reader clearing the filter
    /// with a stale alias left in the query, and the current spelling has to win. Dropping the blank
    /// on its own would hand the query back to the alias and re-apply the filter they just cleared —
    /// a canonicalisation that changed the answer. So the pair leaves together.
    /// </remarks>
    private static bool ClearsCodebase(string part) =>
        Is(part, FacetKeys.Codebase) && string.IsNullOrWhiteSpace(Value(part) ?? string.Empty);

    /// <summary>
    /// The order nobody chose. The <c>&lt;select&gt;</c> always submits something, and on a form
    /// nobody touched that something is the default — while an order a reader did pick is a real
    /// answer and stays in their URL.
    /// </summary>
    /// <remarks>
    /// The default is read off a fresh <see cref="GameFilter"/>, the same way
    /// <see cref="GameFilterBinding"/> reads it. A literal here would be the second copy that lets
    /// this file and that one disagree about which URLs are equivalent.
    /// </remarks>
    private static bool IsDefaultSort(string part, string value) =>
        Is(part, FacetKeys.Sort)
        && FacetTokens.TrySort(value.Trim(), out var sort)
        && sort == new GameFilter().Sort;

    /// <summary>
    /// Whether a <c>key=value</c> pair names the given key, decoded and case-insensitively —
    /// the same two ways <c>QueryHelpers.ParseQuery</c> reads one, so this cannot disagree with the
    /// binder about which parameter it is looking at.
    /// </summary>
    private static bool Is(string part, string key)
    {
        var end = part.IndexOf('=', StringComparison.Ordinal);

        return string.Equals(
            WebUtility.UrlDecode(end < 0 ? part : part[..end]),
            key,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The decoded value of a pair, or <see langword="null"/> for a key with no <c>=</c> at all —
    /// which selects nothing either, since every flag on this surface is read through
    /// <c>Truthy.Is</c> and a valueless one is false.
    /// </summary>
    private static string? Value(string part)
    {
        var end = part.IndexOf('=', StringComparison.Ordinal);

        return end < 0 ? null : WebUtility.UrlDecode(part[(end + 1)..]);
    }
}
