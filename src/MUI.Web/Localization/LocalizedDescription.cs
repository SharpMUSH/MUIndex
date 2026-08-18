using System.Globalization;

using MUI.Catalog;

namespace MUI.Web.Localization;

/// <summary>
/// Which of a game's self-descriptions answers a browser's own language, if any of them do.
/// </summary>
/// <remarks>
/// <para>
/// A game may report <c>DESCRIPTION</c> and any number of <c>DESCRIPTION-&lt;lang&gt;</c> variants
/// of its own choosing — MSSP ingestion is not gated by a fixed vocabulary
/// (<see cref="MUI.Catalog.Persistence.FieldRegistry"/>), so the set of languages on offer is
/// whatever the operator typed, not a set MUIndex ships translations for.
/// </para>
/// <para>
/// <b>This deliberately does not reuse <see cref="LocaleRouting.Preferred"/>.</b> That method scores
/// only <see cref="Locales.Offered"/> — the UI's own shipped interface languages — and excludes
/// English on purpose, as "where the reader already is". Both are the wrong question to ask about a
/// game's own prose: a game may publish a description in a language MUIndex has no interface strings
/// for at all, and an <c>EN</c> variant is a legitimate answer distinct from the un-suffixed default,
/// not a redirect to where the reader already is.
/// </para>
/// <para>
/// The default <c>DESCRIPTION</c> is what a reader gets absent any match — this only ever narrows
/// toward a variant the browser actually asked for, never away from having a description at all.
/// </para>
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
