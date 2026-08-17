using MUI.Web.Localization;

namespace MUI.Web.Tests;

/// <summary>
/// MessageFormat 1.0, against the cases that catch the usual mistakes.
/// </summary>
/// <remarks>
/// Most of these are the examples ICU's own documentation uses, because they are the ones chosen to
/// break a naive implementation: the apostrophe that is not a quote, the plural offset that changes
/// <c>#</c> but not <c>=0</c>, the ordinal categories English has and its cardinal rule does not,
/// and the fraction that makes 1 and 1.0 different words.
/// </remarks>
public class IcuMessageTests
{
    private static string Format(string pattern, string tag = "en", params (string Key, object? Value)[] args) =>
        IcuMessage.Format(pattern, tag, args.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal));

    // ── quoting ──────────────────────────────────────────────────────────────────────────────

    [Test]
    [Arguments("doesn't", "doesn't")]                       // a lone apostrophe is an apostrophe
    [Arguments("it''s", "it's")]                            // doubled is always one
    [Arguments("'{'literal'}'", "{literal}")]               // quotes a brace
    [Arguments("'{0}'", "{0}")]                             // and everything up to the closing quote
    [Arguments("O'Brien's MUSH", "O'Brien's MUSH")]
    [Arguments("5 o''clock", "5 o'clock")]
    public async Task ApostrophesFollowIcusDefaultMode(string pattern, string expected)
    {
        // DOUBLE_OPTIONAL: an apostrophe only starts a quote when it immediately precedes { } | or,
        // inside a plural, #. Anywhere else it is a literal — which is what makes English possessives
        // and contractions safe to type without thinking about it.
        await Assert.That(Format(pattern)).IsEqualTo(expected);
    }

    [Test]
    public async Task AQuotedHashInsideAPluralIsALiteral()
    {
        // And an unquoted one is the number. Both matter: a game called "#1 MUSH" would otherwise
        // print its own count in the middle of its name.
        await Assert.That(Format("{n, plural, other {'#' is # }}", "en", ("n", 3))).IsEqualTo("# is 3 ");
        await Assert.That(Format("channel #general")).IsEqualTo("channel #general");
    }

    [Test]
    public async Task ABraceInsideABranchIsQuotedTheSameWay()
    {
        // The doubled-brace escape an earlier version carried could not do this: a branch ending
        // "...}}" is indistinguishable from a literal brace followed by the argument's own close.
        await Assert.That(Format("{n, plural, other {'{'#'}'}}", "en", ("n", 2))).IsEqualTo("{2}");
    }

    // ── plural ───────────────────────────────────────────────────────────────────────────────

    [Test]
    [Arguments(0, "0 games")]
    [Arguments(1, "1 game")]
    [Arguments(2, "2 games")]
    public async Task EnglishCardinalHasTwoForms(int count, string expected)
    {
        await Assert.That(Format("{n, plural, one {# game} other {# games}}", "en", ("n", count)))
            .IsEqualTo(expected);
    }

    [Test]
    public async Task AVisibleFractionIsNotOneEvenWhenTheValueIsOne()
    {
        // "1.0 stars", never "1.0 star" — and the two quantities are equal, so nothing but the
        // operands can tell them apart. This is the case an integer-only implementation cannot get
        // right, and the reason PluralOperands exists.
        const string Pattern = "{n, plural, one {# star} other {# stars}}";

        await Assert.That(Format(Pattern, "en", ("n", 1))).IsEqualTo("1 star");
        await Assert.That(Format(Pattern, "en", ("n", 1.0m))).IsEqualTo("1.0 stars");
    }

    [Test]
    [Arguments(1, "1 игра")]
    [Arguments(2, "2 игры")]
    [Arguments(5, "5 игр")]
    [Arguments(11, "11 игр")]      // ends in 1 and is not `one`
    [Arguments(12, "12 игр")]      // ends in 2 and is not `few`
    [Arguments(21, "21 игра")]
    [Arguments(22, "22 игры")]
    public async Task RussianCardinalHasThreeFormsAndTheTeensAreTheTrap(int count, string expected)
    {
        // A rule written from the first three examples anybody tries is wrong for 11 and 12, and
        // wrong in a way no English-speaking reviewer notices.
        await Assert.That(Format(
            "{n, plural, one {# игра} few {# игры} many {# игр} other {# игры}}", "ru", ("n", count)))
            .IsEqualTo(expected);
    }

    [Test]
    [Arguments(1, "1st")]
    [Arguments(2, "2nd")]
    [Arguments(3, "3rd")]
    [Arguments(4, "4th")]
    [Arguments(11, "11th")]
    [Arguments(12, "12th")]
    [Arguments(13, "13th")]
    [Arguments(21, "21st")]
    [Arguments(102, "102nd")]
    public async Task EnglishOrdinalHasFourFormsAndTheCardinalRuleCannotProduceThem(int n, string expected)
    {
        // selectordinal is a different rule set, not a different spelling of plural. English's
        // cardinal rule has two categories and this needs four.
        await Assert.That(Format(
            "{n, selectordinal, one {#st} two {#nd} few {#rd} other {#th}}", "en", ("n", n)))
            .IsEqualTo(expected);
    }

    [Test]
    public async Task AnExactMatchBeatsACategory()
    {
        const string Pattern = "{n, plural, =0 {nobody} one {# person} other {# people}}";

        await Assert.That(Format(Pattern, "en", ("n", 0))).IsEqualTo("nobody");
        await Assert.That(Format(Pattern, "en", ("n", 1))).IsEqualTo("1 person");
        await Assert.That(Format(Pattern, "en", ("n", 4))).IsEqualTo("4 people");
    }

    [Test]
    public async Task OffsetChangesTheHashAndTheCategoryButNotTheExactMatch()
    {
        // ICU's own example. `=` matches the number as written; `#` and the category are both taken
        // after the offset is subtracted — which is what makes "Bob and 2 others" come out of a
        // party of three.
        const string Pattern =
            "{n, plural, offset:1 =0 {nobody} =1 {just them} one {them and # other} other {them and # others}}";

        await Assert.That(Format(Pattern, "en", ("n", 0))).IsEqualTo("nobody");
        await Assert.That(Format(Pattern, "en", ("n", 1))).IsEqualTo("just them");
        await Assert.That(Format(Pattern, "en", ("n", 2))).IsEqualTo("them and 1 other");
        await Assert.That(Format(Pattern, "en", ("n", 3))).IsEqualTo("them and 2 others");
    }

    [Test]
    public async Task AHashPrintsANumberTheWayANumberArgumentPrintsIt()
    {
        // One bundle may not print one quantity two ways. `#` was the raw integer digits and
        // `{n, number}` was the culture's grouped form, so a listing past a thousand said "1234
        // games" in a sentence and "1,234" in the column beside it. The separator is the culture's:
        // en groups with a comma and de with a full stop.
        await Assert.That(Format("{n, plural, other {# games}}", "en", ("n", 1234))).IsEqualTo("1,234 games");
        await Assert.That(Format("{n, plural, other {# Spiele}}", "de", ("n", 1234))).IsEqualTo("1.234 Spiele");

        await Assert.That(Format("{n, plural, other {#}}", "en", ("n", 1_234_567)))
            .IsEqualTo(Format("{n, number, integer}", "en", ("n", 1_234_567)));
    }

    [Test]
    public async Task ANegativeCountKeepsItsSign()
    {
        // CLDR takes the absolute value to choose a category and never to print the number. The
        // operands are absolute because the chart says so; the rendering is not, and printing "3
        // games" for minus three is a measurement stated backwards.
        const string Pattern = "{n, plural, one {# game} other {# games}}";

        await Assert.That(Format(Pattern, "en", ("n", -1))).IsEqualTo("-1 game");
        await Assert.That(Format(Pattern, "en", ("n", -3))).IsEqualTo("-3 games");
        await Assert.That(Format(Pattern, "en", ("n", -1234))).IsEqualTo("-1,234 games");
    }

    [Test]
    public async Task ChineseHasOneFormAndSoAgreesWithAnyShape()
    {
        // Which is exactly why it cannot be the locale a string architecture is validated against.
        foreach (var count in new[] { 0, 1, 2, 5, 11 })
        {
            await Assert.That(Format("{n, plural, other {#个游戏}}", "zh-Hans", ("n", count)))
                .IsEqualTo($"{count}个游戏");
        }
    }

    // ── select and nesting ───────────────────────────────────────────────────────────────────

    [Test]
    public async Task SelectMatchesAKeywordAndFallsBackToOther()
    {
        const string Pattern = "{state, select, measured {we watched it} declared {they said so} other {unknown}}";

        await Assert.That(Format(Pattern, "en", ("state", "measured"))).IsEqualTo("we watched it");
        await Assert.That(Format(Pattern, "en", ("state", "declared"))).IsEqualTo("they said so");
        await Assert.That(Format(Pattern, "en", ("state", "derived"))).IsEqualTo("unknown");
    }

    [Test]
    public async Task ASelectorMayHoldAnotherSelector()
    {
        // The case that breaks a parser which stops at the first closing brace: a branch containing
        // an argument ends with two of them, and only a counted scan gets the boundary right.
        const string Pattern =
            "{kind, select, game {{n, plural, one {# game} other {# games}}} other {{n} things}}";

        await Assert.That(Format(Pattern, "en", ("kind", "game"), ("n", 1))).IsEqualTo("1 game");
        await Assert.That(Format(Pattern, "en", ("kind", "game"), ("n", 7))).IsEqualTo("7 games");
        await Assert.That(Format(Pattern, "en", ("kind", "other"), ("n", 7))).IsEqualTo("7 things");
    }

    [Test]
    public async Task AHashIsNotSpecialInsideANestedArgumentsSubMessage()
    {
        // ICU's rule, and a genuinely surprising one: # is a placeholder in the plural argument's
        // own sub-messages and nowhere else. Inside a select nested in that plural it is a literal
        // hash, and a message that wants the number there has to name the argument.
        await Assert.That(Format(
            "{n, plural, other {{k, select, a {# apples} other {# other}}}}",
            "en", ("n", 4), ("k", "a")))
            .IsEqualTo("# apples");

        await Assert.That(Format(
            "{n, plural, other {{k, select, a {{n} apples} other {other}}}}",
            "en", ("n", 4), ("k", "a")))
            .IsEqualTo("4 apples");
    }

    // ── number, date and time ────────────────────────────────────────────────────────────────

    [Test]
    public async Task NumberCarriesItsStyles()
    {
        await Assert.That(Format("{n, number}", "en", ("n", 1234.5m))).IsEqualTo("1,234.5");
        await Assert.That(Format("{n, number, integer}", "en", ("n", 1234.6m))).IsEqualTo("1,235");
        await Assert.That(Format("{n, number, percent}", "en", ("n", 0.42m))).IsEqualTo("42%");
        await Assert.That(Format("{n, number, ::.00}", "en", ("n", 3.14159m))).IsEqualTo("3.14");
    }

    [Test]
    public async Task DateAndTimeAreAcceptedRatherThanRefused()
    {
        // Nothing on this site calls them — every date goes through Dates, which states UTC and
        // never prints a numeric month. They are here so the grammar is supported rather than the
        // half of it we happen to use.
        var when = new DateTimeOffset(2026, 8, 17, 14, 2, 0, TimeSpan.Zero);

        await Assert.That(Format("{d, date}", "en", ("d", when))).IsEqualTo("17 Aug 2026");
        await Assert.That(Format("{d, date, short}", "en-GB", ("d", when))).Contains("2026");
        await Assert.That(Format("{d, time}", "en", ("d", when))).IsEqualTo("14:02:00");
    }

    // ── refusals ─────────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task ASelectorWithNoOtherBranchIsRefusedWhenItIsParsed()
    {
        // Not when a reader's count happens to reach the gap. Every bundle is parsed by a test, so
        // this fails a build.
        await Assert.That(() => MessagePattern.Parse("{n, plural, one {# game}}"))
            .Throws<FormatException>();

        await Assert.That(() => MessagePattern.Parse("{x, select, a {A}}"))
            .Throws<FormatException>();
    }

    [Test]
    public async Task ABranchKeywordNoCategoryUsesIsRefused()
    {
        // "much" is not a CLDR category, so nothing would ever select it. It is almost always a typo
        // for "many", and silently rendering `other` instead is how that typo survives review.
        await Assert.That(() => MessagePattern.Parse("{n, plural, much {#} other {#}}"))
            .Throws<FormatException>();
    }

    [Test]
    public async Task DeprecatedAndUnknownTypesAreRefusedRatherThanGuessed()
    {
        await Assert.That(() => MessagePattern.Parse("{n, choice, 0#none|1#one}"))
            .Throws<FormatException>();

        await Assert.That(() => MessagePattern.Parse("{n, spellout}"))
            .Throws<FormatException>();
    }

    [Test]
    public async Task AMissingArgumentIsAnErrorAndNotAnEmptyString()
    {
        // A message that silently rendered nothing for a value nobody passed would be a fact
        // quietly missing from a page whose whole claim is that its facts are there.
        await Assert.That(() => Format("{missing}")).Throws<FormatException>();
        await Assert.That(() => Format("{n, plural, other {#}}", "en", ("n", "not a number")))
            .Throws<FormatException>();
    }

    [Test]
    public async Task AnUnmatchedBraceIsRefused()
    {
        await Assert.That(() => MessagePattern.Parse("a } b")).Throws<FormatException>();
        await Assert.That(() => MessagePattern.Parse("{n, plural, other {#}")).Throws<FormatException>();
    }

    [Test]
    [Arguments("{n, plural, = {none} other {#}}")]           // a bare '=' matched nothing at all
    [Arguments("{n, plural, =x {none} other {#}}")]          // and neither does a word after it
    [Arguments("{n, plural, =- {none} other {#}}")]
    [Arguments("{n, plural, =. {none} other {#}}")]
    public async Task AnExplicitMatchWithNoNumberAfterItIsRefused(string pattern)
    {
        // An `=` selector is stored under a key compared against the number as written, so `=` on
        // its own is a branch nothing can ever select. The category check at the line above skips
        // anything starting with `=`, so this was the one dead branch the parser let through — and
        // a dead branch is precisely what this parser refuses rather than guesses at.
        await Assert.That(() => MessagePattern.Parse(pattern)).Throws<FormatException>();
    }

    [Test]
    public async Task AnExplicitMatchStillTakesEveryNumberIcuWritesOneWith()
    {
        // Refusing the empty case must not also refuse the legal ones: ICU parses an explicit value
        // as a number, which may carry a sign and a fraction.
        await Assert.That(Format("{n, plural, =0 {none} other {#}}", "en", ("n", 0))).IsEqualTo("none");
        await Assert.That(Format("{n, plural, =-1 {owed} other {#}}", "en", ("n", -1))).IsEqualTo("owed");
        await Assert.That(Format("{n, plural, =1.5 {half} other {#}}", "en", ("n", 1.5m))).IsEqualTo("half");
    }

    [Test]
    public async Task ADateWithNoZoneIsReadAsUtcAndNotAsTheServersOwnClock()
    {
        // This site states UTC on every date it prints, and `new DateTimeOffset(dt)` applies the
        // machine's local offset to an Unspecified kind — so the same message rendered on two
        // hosts said two different times, and neither said which.
        var unspecified = new DateTime(2026, 8, 17, 14, 2, 0, DateTimeKind.Unspecified);

        await Assert.That(Format("{d, time, medium}", "en", ("d", unspecified))).IsEqualTo("14:02:00");
        await Assert.That(Format("{d, date, zzz}", "en", ("d", unspecified))).IsEqualTo("+00:00");
    }

    [Test]
    public async Task ANumberArgumentThatIsNotANumberNamesItselfInTheRefusal()
    {
        // The documented contract is FormatException. Convert.ToDecimal raises InvalidCastException
        // for a value with no conversion and OverflowException for one out of range, and neither
        // says which argument in a forty-message page was the one that failed.
        await Assert.That(() => Format("{n, number}", "en", ("n", new object())))
            .Throws<FormatException>();

        await Assert.That(() => Format("{n, number, integer}", "en", ("n", double.MaxValue)))
            .Throws<FormatException>();
    }

    // ── the parse, as a thing callers can read ───────────────────────────────────────────────

    [Test]
    public async Task APatternCanBeAskedWhatArgumentsItReads()
    {
        // Which is what lets a test check a bundle against the values the site actually supplies,
        // without rendering every message against every plausible count.
        var parsed = MessagePattern.Parse(
            "{value}, {count, plural, one {# game} other {# games}}, {state, select, a {A} other {B}}");

        var names = parsed.Arguments().Select(a => a.Name).ToList();

        await Assert.That(names).Contains("value");
        await Assert.That(names).Contains("count");
        await Assert.That(names).Contains("state");

        var plural = parsed.Arguments().Single(a => a.Kind is ArgumentKind.Plural);

        await Assert.That(plural.Branches.Keys).Contains("one");
        await Assert.That(plural.Branches.Keys).Contains("other");
    }

    [Test]
    public async Task ThePatternCacheReturnsTheSameParseForTheSameText()
    {
        // Forty messages render per facet panel, and re-parsing a string that has not changed since
        // startup is work nobody asked for.
        const string Pattern = "{n, plural, one {#} other {#}}";

        await Assert.That(IcuMessage.Compile(Pattern)).IsSameReferenceAs(IcuMessage.Compile(Pattern));
    }
}
