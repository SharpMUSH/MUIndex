namespace MUI.Web.Localization;

/// <summary>The CLDR plural categories, in the order a message declares them.</summary>
public enum PluralCategory
{
    Zero,
    One,
    Two,
    Few,
    Many,

    /// <summary>The category every language has, and the only one a message must declare.</summary>
    Other,
}

/// <summary>
/// Which plural form a number takes, per locale.
/// </summary>
/// <remarks>
/// <para>
/// <b>English has two forms and that is the whole reason this file exists.</b> Every count on this
/// site was a number glued to an English fragment in English word order — "23 games", "168 probes",
/// "5 counts" — and the panel shipped "1 games" and "0 games" for months because two forms is easy
/// enough to get wrong even in the language the site is written in. Russian needs three categories
/// and Arabic six; several languages put the unit before the number; Chinese needs a measure word
/// and has no plural at all. There is nowhere in a concatenation for a translator to intervene.
/// </para>
/// <para>
/// <b>Hand-written, and only for the locales this site commits to.</b> .NET exposes CLDR collation
/// and formatting but not plural rules, and the alternative — a dependency carrying every rule for
/// every language on earth — is a great deal of machinery for a list that is nine entries long and
/// changes when somebody decides to add a language. The rules below are transcribed from CLDR 46's
/// plural chart and each one names its source category so it can be checked against it. Anything
/// unlisted falls back to <see cref="PluralCategory.Other"/>, which is the correct answer for a
/// language with one form and a safe one for a language whose rule we have not written: the message
/// still renders, and the test that walks every shipped locale is what stops it staying that way.
/// </para>
/// </remarks>
public static class PluralRules
{
    /// <summary>
    /// The category <paramref name="count"/> takes in <paramref name="tag"/>.
    /// </summary>
    /// <remarks>
    /// Integers only, deliberately. Every number this site pluralises is a count of things it
    /// measured — games, probes, samples, rows — and there is no such thing as 1.5 games. CLDR's
    /// fractional rules exist and are not needed here, and implementing them unused would be code
    /// nothing exercises.
    /// </remarks>
    public static PluralCategory Of(string tag, int count)
    {
        var n = Math.Abs(count);

        return Language(tag) switch
        {
            // one → n = 1. Everything else is other.
            //
            // `qps` is the pseudolocale, and it belongs here rather than in the fallback: it is
            // English with its letters accented, so it has to select the same forms English does or
            // it exercises the wrong branch and proves nothing. It rendered "1 games" until it did.
            "en" or "de" or "hi" or "qps" => n == 1 ? PluralCategory.One : PluralCategory.Other,

            // No plural inflection at all. This is exactly why Chinese cannot be the locale a
            // string architecture is validated against — it agrees with any shape, including a
            // wrong one.
            "zh" or "ja" or "ko" or "th" or "vi" or "id" => PluralCategory.Other,

            // one   → n % 10 = 1 and n % 100 ≠ 11
            // few   → n % 10 in 2..4 and n % 100 not in 12..14
            // many  → everything else, including 0, 11..14 and every multiple of ten
            "ru" or "uk" or "be" => (n % 10, n % 100) switch
            {
                (1, not 11) => PluralCategory.One,
                (2 or 3 or 4, not (12 or 13 or 14)) => PluralCategory.Few,
                _ => PluralCategory.Many,
            },

            // one → n in 0..1
            "fr" or "pt" => n is 0 or 1 ? PluralCategory.One : PluralCategory.Other,

            // one → n = 1; few → n in 2..4; many → n = 0 or n % 100 in 5..21ish. CLDR's Czech rule,
            // kept short because the site commits to no Czech locale — it is here as the shape a
            // fourth category takes, and the test that walks the offered locales does not read it.
            "cs" or "sk" => n switch
            {
                1 => PluralCategory.One,
                2 or 3 or 4 => PluralCategory.Few,
                _ => PluralCategory.Other,
            },

            // A language whose rule is not written here still renders, in its one guaranteed form.
            _ => PluralCategory.Other,
        };
    }

    /// <summary>Every category a locale can actually produce, which is what a message must cover.</summary>
    /// <remarks>
    /// This is the assertion the Russian canary is for. A message declaring only <c>one</c> and
    /// <c>other</c> is complete in English and silently wrong in Russian, where a count of two takes
    /// a form neither branch supplies — and a wrong plural does not read as a typo to a native
    /// speaker, it reads as illiterate.
    /// </remarks>
    public static IReadOnlyList<PluralCategory> CategoriesOf(string tag) => Language(tag) switch
    {
        "en" or "de" or "hi" or "qps" or "fr" or "pt" => [PluralCategory.One, PluralCategory.Other],
        "zh" or "ja" or "ko" or "th" or "vi" or "id" => [PluralCategory.Other],
        "ru" or "uk" or "be" => [PluralCategory.One, PluralCategory.Few, PluralCategory.Many],
        "cs" or "sk" => [PluralCategory.One, PluralCategory.Few, PluralCategory.Other],
        _ => [PluralCategory.Other],
    };

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
        ArgumentNullException.ThrowIfNull(tag);

        var dash = tag.IndexOf('-', StringComparison.Ordinal);

        return (dash < 0 ? tag : tag[..dash]).ToLowerInvariant();
    }
}
