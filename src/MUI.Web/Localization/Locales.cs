namespace MUI.Web.Localization;

/// <summary>How far along a locale is, and therefore whether a reader may be sent to it.</summary>
/// <remarks>
/// <b>A locale is offered when its locked strings are translated and reviewed, and not before.</b>
/// The handoff's order of work says it plainly — nothing ships to a reader before the glossary
/// exists — because the failure mode here is not an untranslated button. It is a provenance word
/// that has drifted: "measured", "declared", "not measured", "uncounted" and "unreachable" are five
/// different claims, and a translation engine treats them as stylistic variants of *unavailable*.
/// A reader who cannot tell a game that answered from one that did not has been told something
/// false by the one site whose whole argument is that it never guesses.
/// </remarks>
public enum LocaleStatus
{
    /// <summary>Planned for year one, with nothing translated yet. Never offered.</summary>
    Planned,

    /// <summary>Being translated. Reachable by URL for review; not in the switcher.</summary>
    InProgress,

    /// <summary>Locked strings translated and reviewed. Offered to readers.</summary>
    Shipped,

    /// <summary>
    /// A locale that exists to be tested against and is never offered to anybody.
    /// </summary>
    /// <remarks>
    /// Two of them, for two different jobs. The pseudolocale exercises the machinery — routing,
    /// fallback, plural selection, and the 1.4x width budget the nav is reviewed against — without
    /// anybody claiming it is a language. The Russian canary exists because <b>Chinese cannot fail
    /// an agreement bug</b>: it has no grammatical gender, no plural inflection and no case, so a
    /// string architecture that is wrong for every inflected language passes review against it.
    /// Russian needs four forms of "measured" and three plural categories, so a missing one fails a
    /// build instead of reaching a reader.
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
    /// <summary>Whether the switcher offers this one.</summary>
    public bool IsOffered => Status is LocaleStatus.Shipped;
}

/// <summary>
/// The interface languages, and the boundary around them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every locale here is left-to-right, and that is a decision rather than an omission.</b> A text
/// run has a direction and a layout has a direction, and only the first is in scope: <c>dir</c> on a
/// <c>&lt;bdi&gt;</c> makes an Arabic game name flow right-to-left inside the cell it occupies and
/// moves nothing else, which is why an Arabic game in an English page is correct permanently. An
/// entire Arabic <em>interface</em> still reading left-to-right is not — the page's reading path
/// fights the text's — so Arabic and Hebrew are <b>render-only</b>: indexed and displayed perfectly,
/// not offered as interface languages. Publishing that boundary is more in keeping with this site's
/// voice than quietly shipping a half-correct Arabic UI, and revisiting it means mirroring and
/// translation together, as one project.
/// </para>
/// <para>
/// Chinese is first because it is plausibly the largest non-English audience for a MU* directory and
/// it stress-tests every typographic finding at once. Chinese and Japanese are separate locales and
/// not one CJK bucket: Unicode unified thousands of Han characters across them and the correct drawn
/// form differs, so <c>lang</c> is what selects between two font families. A reader of either
/// notices the wrong one immediately, and no measurement catches it — the two renderings are exactly
/// the same width.
/// </para>
/// </remarks>
public static class Locales
{
    /// <summary>The language the site is written in, and the one every other falls back to.</summary>
    public const string SourceTag = "en";

    /// <summary>Where a reader's choice is kept.</summary>
    /// <remarks>
    /// Read by the server while the page is composed and never by a script, like the theme cookie
    /// beside it — this site runs none, and the switcher is a form for that reason.
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
    /// Ordered as the switcher lists them: the source language first, then the rest by the order
    /// they were committed to. A list ordered by "most speakers" would be an editorial claim about
    /// whose language matters, which is not a measurement and not ours to publish.
    /// </remarks>
    public static IReadOnlyList<Locale> All { get; } =
    [
        new("en", "English", LocaleStatus.Shipped),
        new("zh-Hans", "中文", LocaleStatus.Planned),
        new("ja", "日本語", LocaleStatus.Planned),
        new("de", "Deutsch", LocaleStatus.Planned),
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
    /// Stated as data rather than left implicit, so the boundary is publishable and testable rather
    /// than a thing somebody has to remember. A game whose name is in one of these is indexed,
    /// searched, isolated with <c>&lt;bdi&gt;</c> and tagged with its own <c>lang</c> exactly like
    /// every other game.
    /// </remarks>
    public static IReadOnlyList<string> RenderOnlyScripts { get; } = ["ar", "he"];

    /// <summary>The locales a reader may actually be sent to.</summary>
    public static IReadOnlyList<Locale> Offered { get; } = [.. All.Where(l => l.IsOffered)];

    /// <summary>The source locale, which is never missing and never falls back.</summary>
    public static Locale Source { get; } = All.Single(l => l.Tag == SourceTag);

    /// <summary>A tag as a locale, or null when nothing here answers to it.</summary>
    /// <remarks>
    /// Case-insensitive, because a path segment is whatever somebody typed and <c>/ZH-HANS/games</c>
    /// is the same request as <c>/zh-Hans/games</c>. Unknown tags answer null rather than falling
    /// back to English silently — the caller decides whether that is a 404 or a redirect, and those
    /// are different answers.
    /// </remarks>
    public static Locale? Find(string? tag) => tag is null
        ? null
        : All.FirstOrDefault(l => string.Equals(l.Tag, tag, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether a path segment looks like one of ours, without deciding anything else.</summary>
    public static bool IsLocaleSegment(string segment) => Find(segment) is not null;
}
