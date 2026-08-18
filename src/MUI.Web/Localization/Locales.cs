using System.Collections.Concurrent;
using System.Globalization;

namespace MUI.Web.Localization;

/// <summary>How far along a locale is, and therefore whether a reader may be sent to it.</summary>
/// <remarks>
/// A locale is offered only once its locked strings are translated and reviewed: provenance words
/// like "measured", "declared" and "unreachable" are distinct claims, and a translation engine
/// otherwise treats them as interchangeable synonyms for "unavailable".
/// </remarks>
public enum LocaleStatus
{
    /// <summary>Planned for year one, with nothing translated yet. Never offered.</summary>
    Planned,

    /// <summary>
    /// Translated, and therefore offered.
    /// </summary>
    /// <remarks>
    /// The only gate is <see cref="Glossary"/>'s locked strings existing in the locale; a test walks
    /// every offered locale to prove they do.
    /// </remarks>
    Shipped,

    /// <summary>
    /// A locale that exists to be tested against and is never offered to anybody.
    /// </summary>
    /// <remarks>
    /// Two, for two jobs: the pseudolocale exercises routing, fallback and the nav's width budget
    /// without claiming to be a language. The Russian canary exists because Chinese has no gender,
    /// plural or case and so cannot fail an agreement bug that an inflected language would catch.
    /// </remarks>
    TestOnly,
}

/// <summary>One interface language, named as its own readers name it.</summary>
/// <param name="Tag">The BCP-47 tag, which is also the path segment.</param>
/// <param name="Endonym">
/// The language's name in itself — Deutsch, Русский, 中文 — never in English and never a flag.
/// </param>
/// <param name="Status">Whether a reader may be sent here.</param>
public sealed record Locale(string Tag, string Endonym, LocaleStatus Status)
{
    /// <summary>Whether a reader may be sent here by default.</summary>
    public bool IsOffered => Status is LocaleStatus.Shipped;

    /// <summary>Whether a reader who has explicitly chosen this one may be kept in it.</summary>
    /// <remarks>
    /// Wider than <see cref="IsOffered"/> by the test-only locales: a reader who picks one from the
    /// switcher stays in it even though it is never offered by default.
    /// </remarks>
    public bool IsChoosable => Status is LocaleStatus.Shipped or LocaleStatus.TestOnly;

}

/// <summary>
/// The interface languages, and the boundary around them.
/// </summary>
/// <remarks>
/// <para>
/// Every locale here is left-to-right. <c>dir</c> on a <c>&lt;bdi&gt;</c> lets an Arabic game name
/// flow right-to-left inside its own cell, but a whole Arabic <em>interface</em> reading
/// left-to-right would fight its own text's direction — so Arabic and Hebrew are render-only:
/// indexed and displayed, not offered as interface languages.
/// </para>
/// <para>
/// Chinese and Japanese are separate locales rather than one CJK bucket: Unicode unified many Han
/// characters across them but the correct drawn form differs, so <c>lang</c> selects the font
/// family — the two renderings are the same width, so no measurement catches a wrong one.
/// </para>
/// </remarks>
public static class Locales
{
    /// <summary>The language the site is written in, and the one every other falls back to.</summary>
    public const string SourceTag = "en";

    /// <summary>Where a reader's choice is kept.</summary>
    /// <remarks>
    /// Read by the server while the page is composed and never by a script — this site runs none,
    /// and the switcher is a form for that reason.
    /// </remarks>
    public const string CookieName = "mui_locale";

    /// <summary>The endpoint the switcher posts to.</summary>
    public const string Path = "/locale";

    public const string Field = "locale";

    public const string ReturnField = "return";

    /// <summary>
    /// The seven of year one, plus the two that only a test ever visits.
    /// </summary>
    /// <remarks>
    /// Ordered as the switcher lists them: source language first, then commit order. Ordering by
    /// "most speakers" would be an editorial claim about whose language matters, not a measurement.
    /// </remarks>
    public static IReadOnlyList<Locale> All { get; } =
    [
        new("en", "English", LocaleStatus.Shipped),
        new("zh-Hans", "中文", LocaleStatus.Shipped),
        new("ja", "日本語", LocaleStatus.Shipped),
        new("de", "Deutsch", LocaleStatus.Shipped),
        new("nl", "Nederlands", LocaleStatus.Shipped),

        new("ru", "Русский", LocaleStatus.Planned),
        new("th", "ไทย", LocaleStatus.Planned),
        new("hi", "हिन्दी", LocaleStatus.Planned),

        // Never offered. See LocaleStatus.TestOnly for why these two and not one.
        new("qps-ploc", "Pseudo (QA)", LocaleStatus.TestOnly),
        new("ru-x-canary", "Русский (CI canary)", LocaleStatus.TestOnly),
    ];

    /// <summary>
    /// The scripts this site renders correctly and does not offer an interface in.
    /// </summary>
    /// <remarks>
    /// A game whose name is in one of these is indexed, searched, isolated with <c>&lt;bdi&gt;</c>
    /// and tagged with its own <c>lang</c> exactly like every other game.
    /// </remarks>
    public static IReadOnlyList<string> RenderOnlyScripts { get; } = ["ar", "he"];

    /// <summary>The locales a reader may actually be sent to.</summary>
    public static IReadOnlyList<Locale> Offered { get; } = [.. All.Where(l => l.IsOffered)];

    /// <summary>
    /// The locales a switcher lists, which in a review build includes the ones that are not
    /// languages.
    /// </summary>
    /// <remarks>
    /// <see cref="Offered"/>'s gate is unchanged; this only makes the control visible to review by
    /// adding the pseudolocale and CI canary, neither of which claims to be a translation.
    /// <paramref name="preview"/> is a parameter rather than a static flag on this class because a
    /// static one was written once by composition at host start, and a test process starts many
    /// hosts — so the switcher's contents came from whichever host started last rather than the one
    /// answering the request.
    /// </remarks>
    public static IReadOnlyList<Locale> Switchable(bool preview) =>
    [
        .. All.Where(l => l.IsOffered || (preview && l.Status is LocaleStatus.TestOnly)),
    ];

    /// <summary>The source locale, which is never missing and never falls back.</summary>
    public static Locale Source { get; } = All.Single(l => l.Tag == SourceTag);

    /// <summary>A tag as a locale, or null when nothing here answers to it.</summary>
    /// <remarks>
    /// Case-insensitive: <c>/ZH-HANS/games</c> is the same request as <c>/zh-Hans/games</c>. Unknown
    /// tags answer null rather than falling back to English silently — the caller decides whether
    /// that is a 404 or a redirect.
    /// </remarks>
    public static Locale? Find(string? tag) => tag is null
        ? null
        : All.FirstOrDefault(l => string.Equals(l.Tag, tag, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether a path segment looks like one of ours, without deciding anything else.</summary>
    public static bool IsLocaleSegment(string segment) => Find(segment) is not null;

    private static readonly ConcurrentDictionary<string, CultureInfo> Cultures =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The CLDR data behind a tag — day names, number formats — or the invariant culture where
    /// .NET has never heard of it.
    /// </summary>
    /// <remarks>
    /// Day names come from CLDR rather than a hard-coded English array, so a German page cannot
    /// render a German sentence around English day names again. <c>qps-ploc</c> and
    /// <c>ru-x-canary</c> aren't real languages and asking .NET for them raises, so they fall back to
    /// the invariant culture.
    /// </remarks>
    public static CultureInfo CultureOf(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        return Cultures.GetOrAdd(tag, static t =>
        {
            try
            {
                return CultureInfo.GetCultureInfo(t);
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.InvariantCulture;
            }
        });
    }
}
