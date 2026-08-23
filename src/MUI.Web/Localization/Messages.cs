using System.Globalization;

using System.Resources;

namespace MUI.Web.Localization;

/// <summary>
/// Every string the chrome says, keyed by context, in ICU MessageFormat.
/// </summary>
/// <remarks>
/// Values are ICU patterns, not resx's plain <c>{0}</c> substitutions, because a plural or gendered
/// agreement needs a real branch. Ids are granular past what English needs (e.g.
/// <c>provenance.count.measured</c> vs <c>provenance.game.measured</c>) since other languages
/// inflect the same English word differently by context. Game names, hostnames, codebase strings,
/// version numbers and protocol acronyms never enter this file — they're machine voice, marked
/// <c>translate="no"</c> in markup.
/// </remarks>
public static partial class Messages
{
    /// <summary>
    /// The source bundle, compiled in. Mirrors <c>Resources/Messages.resx</c> (a test keeps them in
    /// sync) so English remains the fallback even without a satellite assembly loaded.
    /// </summary>
    /// <remarks>
    /// Assembled from the per-area partial files this class is split across — <see cref="Chrome"/>,
    /// <see cref="StaticPages"/>, <see cref="CatalogueSurfaces"/>, <see cref="GamePage"/>,
    /// <see cref="Reference"/>, <see cref="OwnerDashboard"/> and <see cref="Measurement"/> —
    /// concatenated in that order so <see cref="Ids"/> keeps declaring ids in the same order the
    /// source bundle always has.
    /// </remarks>
    private static readonly Dictionary<string, string> English = new(
        Chrome()
            .Concat(StaticPages())
            .Concat(CatalogueSurfaces())
            .Concat(GamePage())
            .Concat(Reference())
            .Concat(OwnerDashboard())
            .Concat(Measurement()),
        StringComparer.Ordinal);

    /// <summary>
    /// The bundles, by tag.
    /// </summary>
    /// <remarks>
    /// Only English is complete by design — no locale is offered before it's human-translated.
    /// <c>qps-ploc</c> is a pseudolocale that exercises routing/fallback/plural selection/width
    /// budget with something real. <c>ru-x-canary</c> is deliberately incomplete so a missing plural
    /// form fails the build instead of reaching a reader.
    /// </remarks>
    private static readonly Dictionary<string, Dictionary<string, string>> TestBundles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Generated from English so it cannot fall behind.
            ["qps-ploc"] = English.ToDictionary(e => e.Key, e => Pseudo(e.Value), StringComparer.Ordinal),

            // Missing its `few`/`many` branches on purpose — the completeness test turns that into
            // a build failure.
            ["ru-x-canary"] = new(StringComparer.Ordinal)
            {
                ["facet.count"] = "{count, plural, one {# игра} few {# игры} many {# игр} other {# игры}}",
                ["listing.total"] = "{count, plural, one {# игра} other {# игр}}, каждый факт измерен.",
                ["provenance.count.measured"] = "измерена",
                ["provenance.game.measured"] = "измерено",
                ["provenance.capability.measured"] = "измерены",
                ["kicker.measured"] = "ИЗМЕРЕНО",
            },
        };

    /// <summary>
    /// One message, rendered for a locale — or the English, where that locale has no approved one.
    /// </summary>
    /// <remarks>
    /// The fallback shows raw English rather than a smoothed-over approximation of a locked string
    /// — a reader should be able to tell a claim hasn't been translated yet.
    /// </remarks>
    public static string For(string tag, string id, IReadOnlyDictionary<string, object?>? args = null)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(id);

        var pattern = Pattern(tag, id)
            ?? throw new KeyNotFoundException($"No message '{id}' in any bundle, including English.");

        return IcuMessage.Format(pattern, tag, args);
    }

    /// <summary>
    /// The same, with the arguments named inline rather than built into a dictionary first.
    /// </summary>
    /// <remarks>
    /// <c>StringComparer.Ordinal</c> matters: an argument name is a token matched exactly, and a
    /// case-folding dictionary would answer a lookup the parser never asked for.
    /// </remarks>
    public static string Say(string tag, string id, params (string Key, object? Value)[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return For(tag, id, args.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal));
    }

    /// <summary>A count and its noun, agreeing — the commonest call by a long way.</summary>
    public static string Count(string tag, int count) =>
        For(tag, "facet.count", new Dictionary<string, object?> { ["count"] = count });

    /// <summary>
    /// A bare figure, grouped as the locale groups digits.
    /// </summary>
    /// <remarks>
    /// The same culture <see cref="IcuMessage"/> resolves <c>#</c> and <c>{n, number}</c> against, so
    /// a figure and the sentence under it cannot disagree about where the separators go — which is
    /// what "1490" above "1,361 answered" was, on one tile.
    /// </remarks>
    public static string Figure(string tag, int value)
    {
        ArgumentNullException.ThrowIfNull(tag);

        return value.ToString("N0", Locales.CultureOf(tag));
    }

    /// <summary>The raw pattern a locale would use, English included, or null.</summary>
    public static string? Pattern(string tag, string id)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(id);

        return Own(tag, id) ?? English.GetValueOrDefault(id);
    }

    /// <summary>Whether a locale carries its own text for an id, rather than falling back.</summary>
    public static bool HasOwn(string tag, string id) => Own(tag, id) is not null;

    /// <summary>
    /// What a locale itself says for an id — from its resx, or from a test bundle — or null.
    /// </summary>
    /// <remarks>
    /// Uses a raw <c>ResourceManager</c> rather than <see cref="IStringLocalizer"/>: the latter
    /// answers a missing key with the key itself rather than null, which would render
    /// <c>facet.count</c> to a reader and call it a translation.
    /// </remarks>
    private static string? Own(string tag, string id)
    {
        if (TestBundles.TryGetValue(tag, out var bundle))
        {
            return bundle.GetValueOrDefault(id);
        }

        if (string.Equals(tag, Locales.SourceTag, StringComparison.OrdinalIgnoreCase))
        {
            return English.GetValueOrDefault(id);
        }

        if (Culture(tag) is not { } culture)
        {
            return null;
        }

        // tryParents: false — GetString otherwise walks up to neutral resources and answers English
        // for an untranslated locale, when the caller here is asking "does this locale have its own
        // words for this id".
        try
        {
            return Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false)
                ?.GetString(id);
        }
        catch (MissingManifestResourceException)
        {
            return null;
        }
    }

    /// <summary>
    /// The satellite assemblies, keyed off the marker type so the base name cannot drift.
    /// </summary>
    private static readonly ResourceManager Resources = new(typeof(Web.Resources.Messages));

    private static CultureInfo? Culture(string tag)
    {
        try
        {
            return CultureInfo.GetCultureInfo(tag);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    /// <summary>The source text for an id, which is what a resx has to agree with.</summary>
    public static string? Source(string id) => English.GetValueOrDefault(id);



    /// <summary>Every id the site says, in the order the source bundle declares them.</summary>
    public static IReadOnlyList<string> Ids { get; } = [.. English.Keys];

    /// <summary>
    /// The ids a locale has not translated yet. A locale may not move to
    /// <see cref="LocaleStatus.Shipped"/> while any locked id is in this list.
    /// </summary>
    public static IReadOnlyList<string> MissingFor(string tag) =>
        [.. Ids.Where(id => !HasOwn(tag, id))];

    /// <summary>
    /// Accented, expanded English — a language nobody speaks, which is the point.
    /// </summary>
    /// <remarks>
    /// Accents prove a string came through the pipeline rather than being hard-coded; padding
    /// exercises the 1.4x width budget (German/Russian run 30–40% longer than English UI nouns).
    /// </remarks>
    private static string Pseudo(string pattern)
    {
        var b = new System.Text.StringBuilder(pattern.Length * 2);
        var depth = 0;

        foreach (var c in pattern)
        {
            if (c == '{') { depth++; b.Append(c); continue; }
            if (c == '}') { depth--; b.Append(c); continue; }

            // Inside braces is ICU syntax — accenting it would make the message unparseable.
            b.Append(depth > 0 ? c : Accent(c));
        }

        return "⟦" + b + "⟧";
    }

    private static char Accent(char c) => c switch
    {
        'a' => 'á', 'e' => 'é', 'i' => 'í', 'o' => 'ó', 'u' => 'ú', 'n' => 'ñ', 'c' => 'ç',
        'A' => 'Á', 'E' => 'É', 'I' => 'Í', 'O' => 'Ó', 'U' => 'Ú', 'N' => 'Ñ', 'C' => 'Ç',
        _ => c,
    };
}
