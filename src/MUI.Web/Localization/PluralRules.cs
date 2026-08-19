namespace MUI.Web.Localization;

/// <summary>The CLDR plural categories. <see cref="Other"/> is the only one every language has.</summary>
public enum PluralCategory
{
    Zero,
    One,
    Two,
    Few,
    Many,
    Other,
}

/// <summary>Cardinal counts one thing (1 game, 2 games); ordinal ranks it (1st, 2nd, 3rd, 4th).</summary>
/// <remarks>Different rule sets — getting them from one table is a bug, not a shortcut.</remarks>
public enum PluralKind
{
    Cardinal,
    Ordinal,
}

/// <summary>Which plural form a number takes, per locale, per kind.</summary>
/// <remarks>
/// Transcribed from CLDR 46's plural charts in the operands CLDR states them in (<c>i = 1 and
/// v = 0</c> rather than <c>count == 1</c>) so each rule can be checked against the source line by
/// line. Hand-written rather than via a library — the .NET options (ICU4N alpha pinned to ICU 60;
/// abandoned 0.1.x MessageFormat forks) aren't worth the dependency risk. An unlisted language falls
/// back to <see cref="PluralCategory.Other"/>, which is correct for one-form languages and a safe
/// default otherwise; the test suite walks every offered locale and refuses one this table doesn't
/// cover.
/// </remarks>
public static class PluralRules
{
    /// <summary>The CLDR release these rules were transcribed from.</summary>
    public const string CldrVersion = "46";

    /// <summary>The languages this table states a rule for, cardinal or ordinal.</summary>
    /// <remarks>An uncovered language falls back to <c>other</c> for every count — right for Chinese,
    /// wrong for German. Asserted in tests against <see cref="Locales.All"/>.</remarks>
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
        // "1.0 stars" is `other`, not `one`, despite being numerically equal to 1 — the v = 0 clause
        // is why this table carries operands instead of a bare count.
        "en" or "de" or "nl" or "sv" or "fi" or "et" or "qps" =>
            o is { I: 1, V: 0 } ? PluralCategory.One : PluralCategory.Other,

        // one: n = 1
        // Not the rule above — 1.0 is `one` here (Greek, Norwegian, Spanish, Turkish) but `other` in
        // English; they agree on integers only, which is why this must not be folded with English's.
        "el" or "no" or "tr" => o.N == 1m ? PluralCategory.One : PluralCategory.Other,

        // one: n = 1 or t != 0 and i = 0,1
        // Danish is the only `one` here reaching a non-1 quantity: "0,5 stjerne" not "0,5 stjerner".
        "da" => o.N == 1m || (o.T != 0 && o.I is 0 or 1)
            ? PluralCategory.One
            : PluralCategory.Other,

        // one:  i = 1 and v = 0                                   (it)
        //       n = 1                                             (es)
        //       i = 0,1                                           (fr, pt)
        // many: e = 0 and i != 0 and i % 1000000 = 0 and v = 0    (all four)
        //
        // The Romance millions rule ("un millón de juegos") must not be dropped for it/es by folding
        // them into English's rule — that silently removes the `many` branch a translator needs.
        "it" => Millions(o) ? PluralCategory.Many
            : o is { I: 1, V: 0 } ? PluralCategory.One
            : PluralCategory.Other,

        "es" => Millions(o) ? PluralCategory.Many
            : o.N == 1m ? PluralCategory.One
            : PluralCategory.Other,

        "fr" or "pt" => Millions(o) ? PluralCategory.Many
            : o.I is 0 or 1 ? PluralCategory.One
            : PluralCategory.Other,

        // one:  v = 0 and i % 10 = 1 and i % 100 != 11
        // few:  v = 0 and i % 10 = 2..4 and i % 100 != 12..14
        // many: v = 0 and (i % 10 = 0 or i % 10 = 5..9 or i % 100 = 11..14)
        // other: everything with a visible fraction
        //
        // 11 and 12 end in 1 and 2 but take neither `one` nor `few` — the %100 exclusions matter.
        "ru" or "uk" => o.V != 0 ? PluralCategory.Other
            : (o.I % 10, o.I % 100) switch
            {
                (1, not 11) => PluralCategory.One,
                (2 or 3 or 4, not (12 or 13 or 14)) => PluralCategory.Few,
                _ => PluralCategory.Many,
            },

        // one:  n % 10 = 1 and n % 100 != 11
        // few:  n % 10 = 2..4 and n % 100 != 12..14
        // many: n % 10 = 0 or n % 10 = 5..9 or n % 100 = 11..14
        //
        // Belarusian states this on `n`, not on `i` with `v = 0` as Russian does — so 1.0 is `one`
        // here and `other` in Russian. Must not be folded with the Russian rule above.
        "be" => !Whole(o) ? PluralCategory.Other
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

        // one: i = 1 and v = 0 or i = 0 and v != 0
        // two: i = 2 and v = 0
        //
        // Hebrew's old `many` (multiples of ten) and all its ordinals were withdrawn from CLDR
        // before 46 — do not reintroduce them; a present-but-wrong branch isn't caught by fallback.
        "he" => o switch
        {
            { I: 1, V: 0 } or { I: 0, V: not 0 } => PluralCategory.One,
            { I: 2, V: 0 } => PluralCategory.Two,
            _ => PluralCategory.Other,
        },

        // zero: n = 0        one: n = 1        two: n = 2
        // few:  n % 100 = 3..10                many: n % 100 = 11..99
        //
        // Arabic is render-only (not a shipped locale); the full six-category rule is kept for
        // table correctness regardless.
        "ar" => o.N switch
        {
            0 => PluralCategory.Zero,
            1 => PluralCategory.One,
            2 => PluralCategory.Two,
            _ when !Whole(o) => PluralCategory.Other,
            _ => (o.I % 100) switch
            {
                >= 3 and <= 10 => PluralCategory.Few,
                >= 11 and <= 99 => PluralCategory.Many,
                _ => PluralCategory.Other,
            },
        },

        // No plural inflection at all — so these locales agree with any shape, including a wrong
        // one, and cannot validate a message's plural branches.
        "zh" or "ja" or "ko" or "th" or "vi" or "id" or "ms" => PluralCategory.Other,

        _ => PluralCategory.Other,
    };

    private static PluralCategory Ordinal(string language, PluralOperands o) => language switch
    {
        // one: n % 10 = 1 and n % 100 != 11     (1st, 21st, but 11th)
        // two: n % 10 = 2 and n % 100 != 12     (2nd, 22nd, but 12th)
        // few: n % 10 = 3 and n % 100 != 13     (3rd, 23rd, but 13th)
        "en" or "qps" => !Whole(o) ? PluralCategory.Other
            : (o.I % 10, o.I % 100) switch
            {
                (1, not 11) => PluralCategory.One,
                (2, not 12) => PluralCategory.Two,
                (3, not 13) => PluralCategory.Few,
                _ => PluralCategory.Other,
            },

        // one: n % 10 = 1,2 and n % 100 != 11,12     (1:a and 2:a, then 3:e — and 11:e, 12:e)
        "sv" => !Whole(o) ? PluralCategory.Other
            : (o.I % 10, o.I % 100) switch
            {
                (1 or 2, not (11 or 12)) => PluralCategory.One,
                _ => PluralCategory.Other,
            },

        // one: n = 1   (1er / 1re, then 2e, 3e …)
        "fr" => o.N == 1m ? PluralCategory.One : PluralCategory.Other,

        // few: n % 10 = 3 and n % 100 != 13
        "uk" => !Whole(o) ? PluralCategory.Other
            : (o.I % 10, o.I % 100) switch
            {
                (3, not 13) => PluralCategory.Few,
                _ => PluralCategory.Other,
            },

        // few: n % 10 = 2,3 and n % 100 != 12,13
        "be" => !Whole(o) ? PluralCategory.Other
            : (o.I % 10, o.I % 100) switch
            {
                (2 or 3, not (12 or 13)) => PluralCategory.Few,
                _ => PluralCategory.Other,
            },

        // many: n = 11,8,80,800
        "it" => Whole(o) && o.I is 11 or 8 or 80 or 800 ? PluralCategory.Many : PluralCategory.Other,

        // one: n = 1   two: n = 2,3   few: n = 4   many: n = 6
        "hi" => !Whole(o) ? PluralCategory.Other
            : o.I switch
            {
                1 => PluralCategory.One,
                2 or 3 => PluralCategory.Two,
                4 => PluralCategory.Few,
                6 => PluralCategory.Many,
                _ => PluralCategory.Other,
            },

        // one: n = 1
        "vi" or "ms" => o.N == 1m ? PluralCategory.One : PluralCategory.Other,

        // Every other covered language has one ordinal form, including Hebrew (its ordinal table
        // was withdrawn from CLDR before 46).
        _ => PluralCategory.Other,
    };

    /// <summary>The Romance millions rule (CLDR: <c>e = 0 and i != 0 and i % 1000000 = 0 and v = 0</c>).</summary>
    /// <remarks><c>e</c> (compact-decimal exponent) is always zero for what this site formats.</remarks>
    private static bool Millions(PluralOperands o) =>
        o is { E: 0, V: 0, I: not 0 } && o.I % 1_000_000 == 0;

    /// <summary>Whether the number is whole — a CLDR integer range (e.g. <c>n % 100 = 3..10</c>) never
    /// matches a fraction, even one whose integer part would fall in range.</summary>
    private static bool Whole(PluralOperands o) => o.N == o.I;

    /// <summary>Every category a locale can produce, which is what a message must cover.</summary>
    /// <remarks>
    /// A message declaring only <c>one</c>/<c>other</c> is complete in English but silently wrong in
    /// Russian, where a count of two needs a branch neither supplies.
    /// </remarks>
    public static IReadOnlyList<PluralCategory> CategoriesOf(
        string tag, PluralKind kind = PluralKind.Cardinal)
    {
        ArgumentNullException.ThrowIfNull(tag);

        // Derived by exercising the rule rather than a second table, so the two cannot disagree.
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

    /// <summary>The language subtag, which is what a plural rule is keyed on.</summary>
    /// <remarks>
    /// <c>zh-Hans</c>/<c>zh-Hant</c> and <c>ru</c>/<c>ru-x-canary</c> pluralise identically — script
    /// and private-use subtags never affect number agreement.
    /// </remarks>
    private static string Language(string tag)
    {
        var dash = tag.IndexOf('-', StringComparison.Ordinal);

        return (dash < 0 ? tag : tag[..dash]).ToLowerInvariant();
    }
}
