using MUI.Catalog;
using MUI.Web.Components;
using MUI.Web.Fixtures;
using MUI.Web.Localization;

namespace MUI.Web.Tests;

/// <summary>
/// The three states an hour can be in, checked on the rendered grid and in the words beside it.
/// </summary>
/// <remarks>
/// Conflating any two of these is the worst bug this codebase can ship (rule 2). A measured zero is
/// a filled cell; an unmeasurable hour is hatched; a never-probed hour is empty and names no cause —
/// a failed probe writes no presence row at all. Three different elements and three different
/// sentences, never carried by colour alone.
/// </remarks>
public class ThreeStatesTests
{
    /// <summary>
    /// The listing's absent count says the count is absent, and does not name a cause.
    /// </summary>
    /// <remarks>
    /// Three different cases were wearing one word: the count column printed <c>state.notCounted</c>
    /// (a probe that answered without a number) for any window with no measurement, which also covers
    /// a window we never probed. Two of the three are our crawl schedule, and rule 5 forbids
    /// publishing that as a fact about the game.
    /// </remarks>
    [Test]
    public async Task TheListingSaysACountIsAbsentWithoutSayingWhy()
    {
        var absent = Messages.For(Locales.SourceTag, "listing.count.none");
        var probed = Messages.For(Locales.SourceTag, "state.notCounted");

        // Two different facts, so two different strings, in every language, not just this one.
        // Walked over every locale the site has a bundle for; the count is asserted so a filtered
        // set shrinking to nothing can't make this loop pass vacuously.
        var locales = Locales.All.Where(l => l.Tag != Locales.SourceTag).ToList();

        await Assert.That(locales.Count).IsGreaterThan(1)
            .Because("a claim about every language needs more than one language to be a claim");

        foreach (var locale in locales.Append(Locales.Source))
        {
            await Assert.That(Messages.For(locale.Tag, "listing.count.none"))
                .IsNotEqualTo(Messages.For(locale.Tag, "state.notCounted"))
                .Because($"{locale.Tag} must not print a probed-but-unreadable count for an unmeasured window");
        }

        // And it names nothing: no cause, no zero.
        await Assert.That(absent).DoesNotContain("0");
        await Assert.That(absent).IsNotEqualTo(probed);
    }

    /// <summary>The plain listing marks and its break line are the bundle's words, not English.</summary>
    /// <remarks>The plain mirror once shipped these as English literals while the rendered listing was localized — a mirror that answers in a different language from the page isn't a mirror.</remarks>
    [Test]
    public async Task ThePlainListingMarksAreTranslatedLikeTheRestOfIt()
    {
        foreach (var id in new[]
        {
            "listing.plain.fromHere", "listing.plain.archived", "listing.plain.claimed",
        })
        {
            await Assert.That(Messages.Ids).Contains(id);
            await Assert.That(Messages.For(Locales.SourceTag, id)).IsNotEmpty();
        }
    }

    private static readonly DateTimeOffset Now = FixtureGameQueries.Now;

    /// <summary>The language these assertions are written in, named rather than assumed.</summary>
    private const string English = Locales.SourceTag;

    /// <summary>
    /// The four states on one day — and, because the grid is only drawn once every day of the week
    /// carries a measurement, a measured hour on each of the other six so there is a grid to look at.
    /// </summary>
    private static ActivityCell[] OneOfEach() =>
    [
        new(0, 0, 4, Probed: true),
        new(0, 1, 0, Probed: true),
        new(0, 2, null, Probed: true),
        new(0, 3, null, Probed: false),
        .. Enumerable.Range(1, 6).Select(day => new ActivityCell(day, 20, 3, Probed: true)),
    ];

    private static Task<string> GridAsync(IReadOnlyList<ActivityCell> cells) =>
        Render.ComponentAsync<ActivityHeatmap>(new() { ["Cells"] = cells });

    [Test]
    public async Task TheGridIsDrawnWithHeadersAndIsNotWhatAScreenReaderIsGiven()
    {
        // The drawing is hidden from assistive tech — 168 cells announced individually is 168
        // utterances for one shape. The seven rows below are the real text alternative.
        var html = await GridAsync(OneOfEach());

        await Assert.That(html).Contains("<div class=\"heat-wrap\" aria-hidden=\"true\"");
        await Assert.That(html).Contains("<table class=\"heat\"");
        await Assert.That(html).Contains("<table class=\"perday\"");
        await Assert.That(html).Contains("scope=\"col\"");
        await Assert.That(html).Contains("scope=\"row\"");
        await Assert.That(html).Contains("<caption");
    }

    [Test]
    public async Task ACellCarriesNoAnnouncedTextOfItsOwn()
    {
        // A sparsely measured game once made a screen reader hear "not measured" 167 times before
        // the one real number. Words stay in the tooltip; facts stay in the table below.
        var html = Render.Words(await GridAsync(OneOfEach()));

        await Assert.That(html).Contains("title=\"Mon 03:00 — no measurement in this hour\"></td>");

        // Nothing announced in the grid's body — no cell text, no sr-only span smuggling one back in.
        var body = html[html.IndexOf("<tbody", StringComparison.Ordinal)..];
        var grid = body[..body.IndexOf("</table>", StringComparison.Ordinal)];

        await Assert.That(grid).DoesNotContain("sr-only");
        await Assert.That(grid).DoesNotContain("</td>not");
    }

    [Test]
    public async Task BelowSevenMeasuredDaysThereIsNoGridAtAll()
    {
        // A 7×24 grid holding one probe is 167 empty cells — reads as broken rather than sparse.
        var html = Render.Words(await GridAsync(
        [
            new(0, 0, 42, Probed: true),
        ]));

        await Assert.That(html).Contains("not enough measurements yet");
        await Assert.That(html).Contains("the busiest 42 on Monday at 00:00 UTC");
        await Assert.That(html).DoesNotContain("<table");

        // And no sentence about a week — one measurement can't have a weekly shape.
        await Assert.That(html).DoesNotContain("Busiest");
    }

    /// <summary>
    /// The threshold itself, from both sides.
    /// </summary>
    /// <remarks>Six against seven is the only pair that pins <see cref="ActivitySummary.MeasuredDaysForGrid"/> down exactly; both the grid's gate and the plain surface's read it.</remarks>
    [Test]
    public async Task SixMeasuredDaysIsNotAWeekAndTheSeventhIsWhatDrawsTheGrid()
    {
        var six = Enumerable.Range(0, ActivitySummary.MeasuredDaysForGrid - 1)
            .Select(day => new ActivityCell(day, 20, 3, Probed: true))
            .ToList();

        var withoutGrid = Render.Words(await GridAsync(six));

        await Assert.That(withoutGrid).Contains("not enough measurements yet");
        await Assert.That(withoutGrid).DoesNotContain("<table");

        var seven = six
            .Append(new ActivityCell(ActivitySummary.MeasuredDaysForGrid - 1, 20, 3, Probed: true))
            .ToList();

        await Assert.That(Render.Words(await GridAsync(seven))).Contains("<table class=\"perday\"");

        // The plain surface must turn on the same day, or the two disagree about how much was measured.
        await Assert.That(ActivitySummary.MeasuredDays(six))
            .IsLessThan(ActivitySummary.MeasuredDaysForGrid);
        await Assert.That(ActivitySummary.MeasuredDays(seven))
            .IsEqualTo(ActivitySummary.MeasuredDaysForGrid);
    }

    [Test]
    public async Task TheThreeStatesAreThreeDifferentCellsInTheMarkup()
    {
        var html = await GridAsync(OneOfEach());

        await Assert.That(html).Contains("class=\"counted\"");
        await Assert.That(html).Contains("class=\"counted zero\"");
        await Assert.That(html).Contains("class=\"unmeasurable\"");
        await Assert.That(html).Contains("class=\"gap\"");
    }

    [Test]
    public async Task AMeasuredZeroSaysMeasuredAndAnUnmeasuredHourSaysSo()
    {
        // Must survive with no cell shape and no colour: the sentence in the tooltip, and its own column below.
        var html = await GridAsync(OneOfEach());

        await Assert.That(html).Contains("0 players, measured");
        await Assert.That(html).Contains("no measurement in this hour");
        await Assert.That(html).Contains("probed, no count could be read");
        await Assert.That(html).Contains(">not measured</th>");
        await Assert.That(html).Contains(">no count</th>");
    }

    [Test]
    public async Task AnAnnouncedCellIsAValueRatherThanARepeatedSentence()
    {
        // Headers already say which hour of which day a cell is; repeating that per cell would turn
        // 168 cells into 168 paragraphs. Words read from the glossary, not written out — this test's
        // business is that the cell says the right *state*.
        await Assert.That(ActivitySummary.CellValue(English, new ActivityCell(0, 0, 4, true))).IsEqualTo("4");
        await Assert.That(ActivitySummary.CellValue(English, new ActivityCell(0, 1, 0, true))).IsEqualTo("0");

        await Assert.That(ActivitySummary.CellValue(English, new ActivityCell(0, 2, null, true)))
            .IsEqualTo(Messages.For(English, "state.notCounted"));

        await Assert.That(ActivitySummary.CellValue(English, new ActivityCell(0, 3, null, false)))
            .IsEqualTo(Messages.For(English, "state.notMeasured"));
    }

    /// <summary>
    /// The worst bug this codebase can ship, checked in every language the site answers in.
    /// </summary>
    /// <remarks>
    /// Two different facts that a translation engine collapses to one phrase (<em>nicht verfügbar</em>).
    /// A reader who can't tell them apart can't tell a game that answered from one that didn't.
    /// </remarks>
    [Test]
    public async Task NotMeasuredAndNotCountedAreTwoDifferentWordsInEveryLocale()
    {
        var gap = new ActivityCell(0, 3, null, Probed: false);
        var uncountable = new ActivityCell(0, 2, null, Probed: true);

        foreach (var locale in Locales.All)
        {
            var notMeasured = ActivitySummary.CellValue(locale.Tag, gap);
            var notCounted = ActivitySummary.CellValue(locale.Tag, uncountable);

            await Assert.That(notMeasured)
                .IsNotEqualTo(notCounted)
                .Because($"{locale.Tag} says the same thing about two different hours");

            // Neither is a nought: a zero is a count we took, and both these are the absence of one.
            await Assert.That(notMeasured).IsNotEqualTo("0");
            await Assert.That(notCounted).IsNotEqualTo("0");
        }
    }

    /// <summary>
    /// A German page names the days in German, and it does not get them from us.
    /// </summary>
    /// <remarks>Day names come from CLDR, not a compiled-in array, so a translator can reach them. The mapping can still break: Monday is 0 here (how the store keys a week), .NET's arrays start at Sunday.</remarks>
    [Test]
    [Arguments("en", 0, "Monday", "Mon")]
    [Arguments("en", 6, "Sunday", "Sun")]
    [Arguments("de", 0, "Montag", "Mo")]
    [Arguments("de", 4, "Freitag", "Fr")]
    [Arguments("de", 6, "Sonntag", "So")]
    [Arguments("nl", 0, "maandag", "ma")]
    public async Task ADayIsNamedInTheReadersLanguageAndTheWeekStartsOnMonday(
        string tag, int day, string expected, string expectedShort)
    {
        await Assert.That(ActivitySummary.DayName(tag, day)).IsEqualTo(expected);
        await Assert.That(ActivitySummary.ShortDayName(tag, day)).IsEqualTo(expectedShort);
    }

    /// <summary>
    /// The sentence about a week reaches a reader in their own language, day names and all.
    /// </summary>
    [Test]
    public async Task TheSummarySentenceIsAnsweredInTheLocaleItWasAskedIn()
    {
        var cells = FixtureActivity();

        var english = ActivitySummary.Sentence(English, cells);
        var german = ActivitySummary.Sentence("de", cells);

        // Every id here is untranslated today, so the German sentence is the English one, but with
        // CLDR's day names (not ours) already in German — a day name has no excuse either way.
        await Assert.That(german).IsNotEqualTo(english);

        var named = 0;

        foreach (var day in Enumerable.Range(0, 7))
        {
            if (!english.Contains(ActivitySummary.DayName(English, day), StringComparison.Ordinal))
            {
                continue;
            }

            named++;

            await Assert.That(german).Contains(ActivitySummary.DayName("de", day));
            await Assert.That(german).DoesNotContain(ActivitySummary.DayName(English, day));
        }

        await Assert.That(named).IsGreaterThan(0).Because("the sentence named no day at all");
    }

    /// <summary>
    /// No count is ever said in a plural form the reader's language does not have.
    /// </summary>
    /// <remarks>
    /// A fallback formatter that picks plural branches by source language rather than target would
    /// hand an English <c>one</c> branch to Chinese, which has none. Counted as shapes (number blanked
    /// out, distinct renderings) rather than compared to expected strings, and checked against the
    /// count of plural categories CLDR actually gives the language.
    /// </remarks>
    [Test]
    public async Task ACountIsSaidInAFormItsOwnLanguageHas()
    {
        string[] counted = ["activity.gap.week", "activity.day.notMeasured", "activity.sparse.uncounted"];

        foreach (var locale in Locales.All)
        {
            var forms = PluralRules.CategoriesOf(locale.Tag).Count;

            foreach (var id in counted)
            {
                var shapes = Enumerable.Range(1, 25)
                    .Select(n => Messages
                        .For(locale.Tag, id, new Dictionary<string, object?> { ["count"] = n })
                        .Replace(n.ToString(), "#", StringComparison.Ordinal))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                await Assert.That(shapes.Count)
                    .IsLessThanOrEqualTo(forms)
                    .Because($"{locale.Tag} / {id} says {shapes.Count} things and the language has "
                        + $"{forms} plural {(forms == 1 ? "form" : "forms")}");
            }
        }

        // And English, where a bug of this kind is invisible, still says both of its two.
        await Assert.That(Messages.For(English, "activity.gap.week", new Dictionary<string, object?> { ["count"] = 1 }))
            .Contains("1 hour across");
        await Assert.That(Messages.For(English, "activity.gap.week", new Dictionary<string, object?> { ["count"] = 2 }))
            .Contains("2 hours across");
    }

    /// <summary>
    /// Every sentence the panel can write renders in every locale, with no argument left unsupplied.
    /// </summary>
    /// <remarks>A message naming an argument the caller doesn't pass is a <c>FormatException</c>, not a wrong word — walks the shapes a particular week's branch could hide in.</remarks>
    [Test]
    public async Task EverySentenceShapeRendersInEveryLocale()
    {
        IReadOnlyList<ActivityCell>[] weeks =
        [
            FixtureActivity(),
            OneOfEach(),
            [.. Week((d, h) => new ActivityCell(d, h, 0, Probed: true))],
            [.. Week((d, h) => new ActivityCell(d, h, null, Probed: true))],
            [.. Week((d, h) => new ActivityCell(d, h, null, Probed: false))],
            [new ActivityCell(0, 0, 42, Probed: true)],
            [],
        ];

        foreach (var locale in Locales.All)
        {
            foreach (var week in weeks)
            {
                await Assert.That(() => ActivitySummary.Sentence(locale.Tag, week)).ThrowsNothing();
                await Assert.That(() => ActivitySummary.Sparse(locale.Tag, week)).ThrowsNothing();
                await Assert.That(() => ActivitySummary.PerDay(locale.Tag, week)).ThrowsNothing();
            }
        }

        static IEnumerable<ActivityCell> Week(Func<int, int, ActivityCell> cell) =>
            Enumerable.Range(0, 7).SelectMany(d => Enumerable.Range(0, 24).Select(h => cell(d, h)));
    }

    [Test]
    public async Task AnArchivedGamesGridIsEmptyRatherThanHatched()
    {
        // Hatched means "we got in and could not count" — a game that hasn't answered since 2023
        // didn't get in. Empty says only that no measurement exists for the hour, never that the
        // game was down; that question belongs to the strip, derived from intervals.
        var page = await new FixtureGameQueries().FindAsync("gaslight-row");

        await Assert.That(page!.Activity.All(c => c.IsGap)).IsTrue();
        await Assert.That(ActivitySummary.Sentence(English, page.Activity)).Contains("no measurement yet");
        await Assert.That(ActivitySummary.Sentence(English, page.Activity)).DoesNotContain("reachable");
        await Assert.That(ActivitySummary.Sentence(English, page.Activity)).DoesNotContain("answered but produced");
    }

    [Test]
    public async Task TheSummarySentenceArrivesBeforeTheGrid()
    {
        // The answer is a sentence, not a picture; a reader shouldn't have to reach the picture for it.
        var html = await GridAsync(FixtureActivity());

        var sentence = html.IndexOf("Busiest", StringComparison.Ordinal);
        var table = html.IndexOf("<table", StringComparison.Ordinal);

        await Assert.That(sentence).IsGreaterThanOrEqualTo(0);
        await Assert.That(sentence).IsLessThan(table);
    }

    [Test]
    public async Task AReadAsTextDisclosureGivesOneRowPerDayRatherThanAHundredAndSixtyEightCells()
    {
        // Seven rows behind one keystroke rather than a wall to scroll past.
        var html = Render.Words(await GridAsync(FixtureActivity()));

        await Assert.That(html).Contains("read as text — 7 rows");
        await Assert.That(html).Contains("<th scope=\"row\">Mon</th>");
        await Assert.That(html).Contains("<th scope=\"row\">Sun</th>");
        await Assert.That(html).Contains("Players on by day, in UTC.");
    }

    [Test]
    public async Task ThePlainSurfaceSaysTheSameThingAboutASparselyMeasuredGame()
    {
        // Plain mode must stay in sync with the graphic surface's claim about how much was measured.
        var page = await new FixtureGameQueries().FindAsync("gaslight-row");
        var text = Render.Words(PlainText.Render(page!, Now));

        await Assert.That(text).Contains("The grid appears once every day of the week has one.");
        await Assert.That(text).DoesNotContain("Mon —");
    }

    [Test]
    public async Task TheSentenceNamesUnmeasuredAndUncountableHoursSeparately()
    {
        // Two different facts the summary keeps apart: no measurement at all, vs. reached but uncountable.
        var sentence = ActivitySummary.Sentence(English, FixtureActivity());

        await Assert.That(sentence).Contains("have no measurement yet");
        await Assert.That(sentence).Contains("answered but produced no count");
    }

    [Test]
    public async Task AGameCountedAtZeroAllWeekIsNotDescribedAsUnmeasured()
    {
        // A week of measured zeros is a measurement, not an absence of one.
        var cells = Enumerable.Range(0, 7)
            .SelectMany(d => Enumerable.Range(0, 24).Select(h => new ActivityCell(d, h, 0, Probed: true)))
            .ToList();

        var sentence = ActivitySummary.Sentence(English, cells);

        await Assert.That(sentence).Contains("Measured every hour");
        await Assert.That(sentence).DoesNotContain("not reachable");
    }

    [Test]
    public async Task AGameThatAnswersAndCannotBeCountedIsNeverDescribedAsQuietOrDark()
    {
        // Answers on every probe and offers nothing countable — must render as neither zeros nor darkness.
        var page = await new FixtureGameQueries().FindAsync("midnight-sun");
        var text = Render.Words(PlainText.Render(page!, Now));

        await Assert.That(text).Contains("No hour of the week has produced a player count");
        await Assert.That(text).Contains("answered but produced no count");
        await Assert.That(text).DoesNotContain("0 players");
    }

    private static ActivityCell[] FixtureActivity()
    {
        var page = new FixtureGameQueries().FindAsync("m-u-s-h").GetAwaiter().GetResult();
        return [.. page!.Activity];
    }
}
