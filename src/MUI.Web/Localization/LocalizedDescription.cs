using System.Globalization;

using MUI.Catalog;

namespace MUI.Web.Localization;

/// <summary>
/// Which of a game's self-descriptions answers a browser's own language, if any of them do.
/// </summary>
/// <remarks>
/// A game may report <c>DESCRIPTION</c> and any number of <c>DESCRIPTION-&lt;lang&gt;</c> variants
/// of its own choosing — the set of languages on offer is whatever the operator typed, not a set
/// MUIndex ships translations for. This deliberately does not reuse
/// <see cref="LocaleRouting.Preferred"/>: that method scores only MUIndex's own shipped interface
/// languages and excludes English on purpose, which is the wrong question to ask about a game's own
/// prose — an <c>EN</c> variant is a legitimate distinct answer here, not a redirect. The plain
/// <c>DESCRIPTION</c> is the fallback; this only ever narrows toward a variant the browser asked for.
/// </remarks>
public static class LocalizedDescription
{
    private const string FieldPrefix = "description-";

    /// <summary>
    /// The best-matching <c>DESCRIPTION-&lt;lang&gt;</c> value for a browser's own list, or
    /// <paramref name="fallback"/> when nothing declared answers to it.
    /// </summary>
    /// <param name="acceptLanguage">The request's raw <c>Accept-Language</c> header, or null.</param>
    /// <param name="fallback">What to show when nothing on offer matches — the plain <c>DESCRIPTION</c>.</param>
    /// <param name="declared">
    /// A game's declared fields, keyed lower-invariant (<see cref="GamePage.Declared"/>) — the same
    /// map "Declared by the game" already renders, so a variant this picks is never a fact invented
    /// for the purpose.
    /// </param>
    public static string? Choose(
        string? acceptLanguage,
        string? fallback,
        IReadOnlyDictionary<string, ProvenanceChip> declared)
    {
        ArgumentNullException.ThrowIfNull(declared);

        if (string.IsNullOrWhiteSpace(acceptLanguage))
        {
            return fallback;
        }

        foreach (var tag in ByQuality(acceptLanguage))
        {
            if (declared.TryGetValue(FieldPrefix + PrimarySubtag(tag), out var chip) && chip.Value.Length > 0)
            {
                return chip.Value;
            }
        }

        return fallback;
    }

    /// <summary>The tags of an <c>Accept-Language</c> header, highest quality first.</summary>
    private static IEnumerable<string> ByQuality(string acceptLanguage) =>
        acceptLanguage
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(ParsePart)
            .Where(part => part.Quality > 0 && part.Tag != "*")
            .OrderByDescending(part => part.Quality)
            .Select(part => part.Tag);

    private static (string Tag, double Quality) ParsePart(string part)
    {
        var bits = part.Split(';', StringSplitOptions.TrimEntries);
        var quality = 1d;

        foreach (var parameter in bits.Skip(1))
        {
            if (parameter.StartsWith("q=", StringComparison.OrdinalIgnoreCase)
                && double.TryParse(
                    parameter[2..], NumberStyles.Float, CultureInfo.InvariantCulture, out var q))
            {
                quality = q;
            }
        }

        return (bits[0], quality);
    }

    private static string PrimarySubtag(string tag)
    {
        var dash = tag.IndexOf('-', StringComparison.Ordinal);

        return (dash < 0 ? tag : tag[..dash]).ToLowerInvariant();
    }
}
