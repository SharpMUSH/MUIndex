namespace MUI.Web.Localization;

/// <summary>The CLDR plural categories.</summary>
/// <remarks>
/// Six of them exist across all languages; no single language uses more than five, and most use one
/// or two. <see cref="Other"/> is the one every language has and the only one a message must
/// declare.
/// </remarks>
public enum PluralCategory
{
    Zero,
    One,
    Two,
    Few,
    Many,
    Other,
}

/// <summary>Cardinal counts one thing; ordinal ranks it.</summary>
/// <remarks>
/// They are different rule sets and getting them from one table is a bug rather than a shortcut. In
/// English the cardinal rule has two forms — <em>1 game, 2 games</em> — and the ordinal rule has
/// four — <em>1st, 2nd, 3rd, 4th</em> — and neither can produce the other's answers.
/// </remarks>
public enum PluralKind
{
    Cardinal,
    Ordinal,
}

/// <summary>
/// Which plural form a number takes, per locale, per kind.
/// </summary>
/// <remarks>
/// <para>
/// <b>Transcribed from CLDR 46's plural charts, in the operands CLDR states them in.</b> Each rule
/// below is written the way the chart writes it — <c>i = 1 and v = 0</c> rather than
/// <c>count == 1</c> — so it can be checked against the source line by line. A transcription into a
/// different vocabulary is a transcription nobody can verify.
/// </para>
/// <para>
/// <b>Hand-written rather than taken from a library, and that is a judgement rather than a
/// preference.</b> The .NET options are thin: ICU4N is an alpha pinned to ICU 60, whose CLDR data
/// predates several of the rules below, and the MessageFormat ports are 0.1.x forks of an abandoned
/// project. Neither is a dependency worth the credibility of a site whose entire product is being
/// right about what it knows. The cost is that this table has to be maintained against CLDR when a
/// locale is added, which is why <see cref="LocalesCovered"/> exists and is asserted against the
/// locales the site actually commits to.
/// </para>
/// <para>
/// <b>An unlisted language answers <see cref="PluralCategory.Other"/>.</b> That is correct for a
/// language with one form and safe for one whose rule is not written here — the message still
/// renders, in the form every language has. What stops that being a silent wrong answer is the test
/// that walks every offered locale and refuses one this table does not cover.
/// </para>
/// </remarks>
public static class PluralRules
{
    /// <summary>The CLDR release these rules were transcribed from.</summary>
    public const string CldrVersion = "46";

    /// <summary>
    /// The languages this table states a rule for, cardinal or ordinal.
    /// </summary>
    /// <remarks>
    /// The gate on adding a locale: a tag whose language is not here falls back to <c>other</c> for
    /// every count, which is right for Chinese and wrong for German. Asserted in the tests against
    /// <see cref="Locales.All"/>.
    /// </remarks>
    public static IReadOnlyList<string> LocalesCovered { get; } =
    [
        "en", "de", "nl", "sv", "da", "no", "fi", "et", "el", "it", "es", "fr", "pt",
        "ru", "uk", "be", "pl", "cs", "sk", "hi", "th", "vi", "id", "ms", "ja", "ko", "zh",
        "tr", "he", "ar", "qps",
    ];

    /// <summary>The category <paramref name="count"/> takes.</summary>
    public static PluralCategory Of(string tag, long count, PluralKind kind = PluralKind.Cardinal) =>
        Of(tag, PluralOperands.Of(count), kind);

    /// <summary>The category a number written a particular way takes.</summary>
    public static PluralCategory Of(string tag, PluralOperands o, PluralKind kind = PluralKind.Cardinal)
    {
        ArgumentNullException.ThrowIfNull(tag);

        var language = Language(tag);

        return kind is PluralKind.Ordinal ? Ordinal(language, o) : Cardinal(language, o);
    }

    private static PluralCategory Cardinal(string language, PluralOperands o) => language switch
    {
        // one: i = 1 and v = 0
        //
        // The v = 0 is why this file carries operands at all: "1.0 stars" is other, not one, and
        // the two quantities are equal. `qps` is the pseudolocale and is English underneath, so it
        // has to select exactly what English selects or it exercises the wrong branch.
        "en" or "de" or "nl" or "sv" or "da" or "no" or "fi" or "et" or "el" or "it" or "es"
            or "qps" => o is { I: 1, V: 0 } ? PluralCategory.One : PluralCategory.Other,

        // one: i = 0,1
        // many: e = 0 and i != 0 and i % 1000000 = 0 and v = 0  (un million, not un millions)
        "fr" or "pt" =>
            o is { E: 0, V: 0, I: not 0 } && o.I % 1_000_000 == 0 ? PluralCategory.Many
            : o.I is 0 or 1 ? PluralCategory.One
            : PluralCategory.Other,

        // one:  v = 0 and i % 10 = 1 and i % 100 != 11
        // few:  v = 0 and i % 10 = 2..4 and i % 100 != 12..14
        // many: v = 0 and (i % 10 = 0 or i % 10 = 5..9 or i % 100 = 11..14)
        // other: everything with a visible fraction
        //
        // 11 and 12 end in 1 and 2 and take neither `one` nor `few`. A rule written from the first
        // three examples anybody tries is wrong for both.
        "ru" or "uk" or "be" => o.V != 0 ? PluralCategory.Other
            : (o.I % 10, o.I % 100) switch
            {
                (1, not 11) => PluralCategory.One,
                (2 or 3 or 4, not (12 or 13 or 14)) => PluralCategory.Few,
                _ => PluralCategory.Many,
            },

        // one:  i = 1 and v = 0
        // few:  v = 0 and i % 10 = 2..4 and i % 100 != 12..14
        // many: v = 0 and i != 1 and (i % 10 = 0..1 or i % 10 = 5..9 or i % 100 = 12..14)
        "pl" => o.V != 0 ? PluralCategory.Other
            : o.I == 1 ? PluralCategory.One
            : (o.I % 10, o.I % 100) switch
            {
                (2 or 3 or 4, not (12 or 13 or 14)) => PluralCategory.Few,
                _ => PluralCategory.Many,
            },

        // one: i = 1 and v = 0
        // few: i = 2..4 and v = 0
        // many: v != 0
        "cs" or "sk" => o.V != 0 ? PluralCategory.Many
            : o.I switch
            {
                1 => PluralCategory.One,
                2 or 3 or 4 => PluralCategory.Few,
                _ => PluralCategory.Other,
            },

        // one: i = 0 or n = 1
        "hi" => o.I == 0 || o.N == 1m ? PluralCategory.One : PluralCategory.Other,

        // one: i = 1 and v = 0
        // two: i = 2 and v = 0
        // many: v = 0 and n != 0..10 and n % 10 = 0
        "he" => o switch
        {
            { I: 1, V: 0 } => PluralCategory.One,
            { I: 2, V: 0 } => PluralCategory.Two,
            { V: 0 } when o.N > 10 && o.I % 10 == 0 => PluralCategory.Many,
            _ => PluralCategory.Other,
        },

        // zero: n = 0        one: n = 1        two: n = 2
        // few:  n % 100 = 3..10                many: n % 100 = 11..99
        //
        // Six categories, which is the count the review cites — and Arabic is render-only here, so
        // this rule exists for correctness of the table rather than for a locale that ships.
        "ar" => o.N switch
        {
            0 => PluralCategory.Zero,
            1 => PluralCategory.One,
            2 => PluralCategory.Two,
            _ => (o.I % 100) switch
            {
                >= 3 and <= 10 => PluralCategory.Few,
                >= 11 and <= 99 => PluralCategory.Many,
                _ => PluralCategory.Other,
            },
        },

        // one: n = 0..1
        "tr" => o.N <= 1m ? PluralCategory.One : PluralCategory.Other,

        // No plural inflection at all. This is exactly why Chinese cannot be the locale a string
        // architecture is validated against: it agrees with any shape, including a wrong one.
        "zh" or "ja" or "ko" or "th" or "vi" or "id" or "ms" => PluralCategory.Other,

        _ => PluralCategory.Other,
    };

    private static PluralCategory Ordinal(string language, PluralOperands o) => language switch
    {
        // one: n % 10 = 1 and n % 100 != 11     (1st, 21st, but 11th)
        // two: n % 10 = 2 and n % 100 != 12     (2nd, 22nd, but 12th)
        // few: n % 10 = 3 and n % 100 != 13     (3rd, 23rd, but 13th)
        "en" or "qps" => (o.I % 10, o.I % 100) switch
        {
            (1, not 11) => PluralCategory.One,
            (2, not 12) => PluralCategory.Two,
            (3, not 13) => PluralCategory.Few,
            _ => PluralCategory.Other,
        },

        // one: n = 1   (1er / 1re, then 2e, 3e …)
        "fr" => o.I == 1 ? PluralCategory.One : PluralCategory.Other,

        // few: n % 10 = 3 and n % 100 != 13
        "uk" => (o.I % 10, o.I % 100) switch
        {
            (3, not 13) => PluralCategory.Few,
            _ => PluralCategory.Other,
        },

        // many: n = 11,8,80,800
        "it" => o.I is 11 or 8 or 80 or 800 ? PluralCategory.Many : PluralCategory.Other,

        // one: n = 1,5,7,8,9,10   two: n = 2,3   few: n = 4   many: n = 6
        "he" => o.I switch
        {
            1 or 5 or 7 or 8 or 9 or 10 => PluralCategory.One,
            2 or 3 => PluralCategory.Two,
            4 => PluralCategory.Few,
            6 => PluralCategory.Many,
            _ => PluralCategory.Other,
        },

        // one: n = 1   two: n = 2,3   few: n = 4   many: n = 6
        "hi" => o.I switch
        {
            1 => PluralCategory.One,
            2 or 3 => PluralCategory.Two,
            4 => PluralCategory.Few,
            6 => PluralCategory.Many,
            _ => PluralCategory.Other,
        },

        // Every other language this site knows about has one ordinal form, including German,
        // Russian, Chinese, Japanese, Thai and Portuguese.
        _ => PluralCategory.Other,
    };

    /// <summary>
    /// Every category a locale can produce, which is what a message has to cover.
    /// </summary>
    /// <remarks>
    /// The assertion the Russian canary is for. A message declaring only <c>one</c> and <c>other</c>
    /// is complete in English and silently wrong in Russian, where a count of two takes a form
    /// neither branch supplies — and a wrong plural does not read as a typo to a native speaker, it
    /// reads as illiterate.
    /// </remarks>
    public static IReadOnlyList<PluralCategory> CategoriesOf(
        string tag, PluralKind kind = PluralKind.Cardinal)
    {
        ArgumentNullException.ThrowIfNull(tag);

        // Derived by exercising the rule rather than by keeping a second table beside it, so the
        // two cannot disagree. The probes cover every boundary the rules above test — the teens,
        // the tens, the millions, and a fraction, which is the case that separates `one` from
        // `other` in English.
        var seen = new List<PluralCategory>();

        foreach (var probe in Probes)
        {
            var category = Of(tag, probe, kind);

            if (!seen.Contains(category))
            {
                seen.Add(category);
            }
        }

        return seen;
    }

    /// <summary>Numbers that between them reach every branch of every rule above.</summary>
    private static IReadOnlyList<PluralOperands> Probes { get; } =
    [
        .. new long[]
        {
            0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 20, 21, 22, 23, 24, 25,
            80, 100, 101, 102, 103, 111, 112, 113, 800, 1000, 1_000_000, 2_000_000,
        }.Select(n => PluralOperands.Of(n)),

        // A visible fraction, which is `other` in English and in Russian and `many` in Czech.
        PluralOperands.Of(1.0m, visibleFractionDigits: 1),
        PluralOperands.Of(1.5m, visibleFractionDigits: 1),
    ];

    /// <summary>The keyword a message branch is spelled with.</summary>
    public static string Keyword(PluralCategory category) => category switch
    {
        PluralCategory.Zero => "zero",
        PluralCategory.One => "one",
        PluralCategory.Two => "two",
        PluralCategory.Few => "few",
        PluralCategory.Many => "many",
        _ => "other",
    };

    /// <summary>Whether a word is one of the six category keywords.</summary>
    public static bool IsCategory(string word) =>
        word is "zero" or "one" or "two" or "few" or "many" or "other";

    /// <summary>Whether this table states a rule for a tag's language at all.</summary>
    public static bool Covers(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        return LocalesCovered.Contains(Language(tag), StringComparer.Ordinal);
    }

    /// <summary>
    /// The language subtag, which is what a plural rule is keyed on.
    /// </summary>
    /// <remarks>
    /// <c>zh-Hans</c> and <c>zh-Hant</c> pluralise identically, and so do <c>ru</c> and the CI
    /// canary's <c>ru-x-canary</c> — the script and the private-use subtag change which glyphs are
    /// drawn and which bundle is read, never how a number agrees.
    /// </remarks>
    private static string Language(string tag)
    {
        var dash = tag.IndexOf('-', StringComparison.Ordinal);

        return (dash < 0 ? tag : tag[..dash]).ToLowerInvariant();
    }
}
