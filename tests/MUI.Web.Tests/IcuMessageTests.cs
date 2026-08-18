using MUI.Web.Localization;

namespace MUI.Web.Tests;

/// <summary>
/// MessageFormat 1.0, against the cases that catch the usual mistakes.
/// </summary>
/// <remarks>Mostly ICU's own documentation examples, chosen to break a naive implementation.</remarks>
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
        // DOUBLE_OPTIONAL: an apostrophe starts a quote only immediately before { } | or, in a
        // plural, #. Anywhere else it's a literal.
        await Assert.That(Format(pattern)).IsEqualTo(expected);
    }

    [Test]
    public async Task AQuotedHashInsideAPluralIsALiteral()
    {
        await Assert.That(Format("{n, plural, other {'#' is # }}", "en", ("n", 3))).IsEqualTo("# is 3 ");
        await Assert.That(Format("channel #general")).IsEqualTo("channel #general");
    }

    [Test]
    public async Task ABraceInsideABranchIsQuotedTheSameWay()
    {
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
        // "1.0 stars", never "1.0 star" — equal quantities, distinguished only by visible fraction
        // digits (why PluralOperands exists).
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
        // selectordinal is a different rule set from plural; English's cardinal rule has two
        // categories, this needs four.
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
        // `=` matches the number as written; `#` and the category are taken after the offset is
        // subtracted.
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
        // One bundle may not print one quantity two ways — `#` and `{n, number}` must agree, grouped
        // by the culture's own separator.
        await Assert.That(Format("{n, plural, other {# games}}", "en", ("n", 1234))).IsEqualTo("1,234 games");
        await Assert.That(Format("{n, plural, other {# Spiele}}", "de", ("n", 1234))).IsEqualTo("1.234 Spiele");

        await Assert.That(Format("{n, plural, other {#}}", "en", ("n", 1_234_567)))
            .IsEqualTo(Format("{n, number, integer}", "en", ("n", 1_234_567)));
    }

    [Test]
    public async Task ANegativeCountKeepsItsSign()
    {
        // CLDR takes the absolute value to choose a category, never to print the number.
        const string Pattern = "{n, plural, one {# game} other {# games}}";

        await Assert.That(Format(Pattern, "en", ("n", -1))).IsEqualTo("-1 game");
        await Assert.That(Format(Pattern, "en", ("n", -3))).IsEqualTo("-3 games");
        await Assert.That(Format(Pattern, "en", ("n", -1234))).IsEqualTo("-1,234 games");
    }

    [Test]
    public async Task ChineseHasOneFormAndSoAgreesWithAnyShape()
    {
        // Which is why it can't be the locale a string architecture is validated against.
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
        // Breaks a parser that stops at the first closing brace: needs a counted scan.
        const string Pattern =
            "{kind, select, game {{n, plural, one {# game} other {# games}}} other {{n} things}}";

        await Assert.That(Format(Pattern, "en", ("kind", "game"), ("n", 1))).IsEqualTo("1 game");
        await Assert.That(Format(Pattern, "en", ("kind", "game"), ("n", 7))).IsEqualTo("7 games");
        await Assert.That(Format(Pattern, "en", ("kind", "other"), ("n", 7))).IsEqualTo("7 things");
    }

    [Test]
    public async Task AHashIsNotSpecialInsideANestedArgumentsSubMessage()
    {
        // # is a placeholder only in a plural argument's own sub-messages; nested inside a select
        // it's a literal hash and the number must be named explicitly.
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
        // Nothing on this site calls them directly (every date goes through Dates); tested so the
        // whole grammar is supported.
        var when = new DateTimeOffset(2026, 8, 17, 14, 2, 0, TimeSpan.Zero);

        await Assert.That(Format("{d, date}", "en", ("d", when))).IsEqualTo("17 Aug 2026");
        await Assert.That(Format("{d, date, short}", "en-GB", ("d", when))).Contains("2026");
        await Assert.That(Format("{d, time}", "en", ("d", when))).IsEqualTo("14:02:00");
    }

    // ── refusals ─────────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task ASelectorWithNoOtherBranchIsRefusedWhenItIsParsed()
    {
        // Refused at parse time, not when a reader's count happens to reach the gap.
        await Assert.That(() => MessagePattern.Parse("{n, plural, one {# game}}"))
            .Throws<FormatException>();

        await Assert.That(() => MessagePattern.Parse("{x, select, a {A}}"))
            .Throws<FormatException>();
    }

    [Test]
    public async Task ABranchKeywordNoCategoryUsesIsRefused()
    {
        // "much" is not a CLDR category — almost always a typo for "many" that a silent `other`
        // fallback would hide.
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
        // `=` alone is a branch nothing can ever select — a dead branch the parser refuses rather
        // than lets through.
        await Assert.That(() => MessagePattern.Parse(pattern)).Throws<FormatException>();
    }

    [Test]
    public async Task AnExplicitMatchStillTakesEveryNumberIcuWritesOneWith()
    {
        await Assert.That(Format("{n, plural, =0 {none} other {#}}", "en", ("n", 0))).IsEqualTo("none");
        await Assert.That(Format("{n, plural, =-1 {owed} other {#}}", "en", ("n", -1))).IsEqualTo("owed");
        await Assert.That(Format("{n, plural, =1.5 {half} other {#}}", "en", ("n", 1.5m))).IsEqualTo("half");
    }

    [Test]
    public async Task ADateWithNoZoneIsReadAsUtcAndNotAsTheServersOwnClock()
    {
        // `new DateTimeOffset(dt)` applies the machine's local offset to an Unspecified kind, which
        // would make the same message render two different times on two hosts.
        var unspecified = new DateTime(2026, 8, 17, 14, 2, 0, DateTimeKind.Unspecified);

        await Assert.That(Format("{d, time, medium}", "en", ("d", unspecified))).IsEqualTo("14:02:00");
        await Assert.That(Format("{d, date, zzz}", "en", ("d", unspecified))).IsEqualTo("+00:00");
    }

    [Test]
    public async Task ANumberArgumentThatIsNotANumberNamesItselfInTheRefusal()
    {
        // The documented contract is FormatException, not the raw InvalidCastException/
        // OverflowException Convert.ToDecimal would throw — neither names the failing argument.
        await Assert.That(() => Format("{n, number}", "en", ("n", new object())))
            .Throws<FormatException>();

        await Assert.That(() => Format("{n, number, integer}", "en", ("n", double.MaxValue)))
            .Throws<FormatException>();
    }

    // ── the parse, as a thing callers can read ───────────────────────────────────────────────

    [Test]
    public async Task APatternCanBeAskedWhatArgumentsItReads()
    {
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
        const string Pattern = "{n, plural, one {#} other {#}}";

        await Assert.That(IcuMessage.Compile(Pattern)).IsSameReferenceAs(IcuMessage.Compile(Pattern));
    }
}
