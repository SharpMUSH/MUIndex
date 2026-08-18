using MUI.Catalog;
using MUI.Web.Components;
using MUI.Web.Fixtures;
using MUI.Web.Localization;

namespace MUI.Web.Tests;

/// <summary>
/// The three states an hour can be in, checked on the rendered grid and in the words beside it.
/// </summary>
/// <remarks>
/// Conflating any two of these is the worst bug this codebase can ship. A measured zero is a filled
/// cell — we got in and nobody was there. An unmeasurable hour is hatched — we got in and could not
/// count. An hour with no measurement at all is empty, and says only that: a failed probe writes no
/// presence row, so silence here cannot tell an outage of theirs from a gap of ours, and the strip
/// beside the grid is what answers that. They are three different elements in the markup and three
/// different sentences in the text, and no difference is carried by a colour.
/// </remarks>
public class ThreeStatesTests
{
    /// <summary>
    /// The listing's absent count says the count is absent, and does not name a cause.
    /// </summary>
    /// <remarks>
    /// <b>Three cases were wearing one word, and the word named a cause.</b> The count column
    /// printed <c>state.notCounted</c> — the glossary's phrase for a probe that answered without a
    /// number — whenever the window had no measurement, which also covers a window we never probed
    /// in and a game with no rows at all. Two of the three are our crawl schedule, and rule 5 says
    /// our schedule is never published as a fact about somebody's game. The same conflation was
    /// found and fixed on the game page's hero; this is the listing's half of it.
    /// </remarks>
    [Test]
    public async Task TheListingSaysACountIsAbsentWithoutSayingWhy()
    {
        var absent = Messages.For(Locales.SourceTag, "listing.count.none");
        var probed = Messages.For(Locales.SourceTag, "state.notCounted");

        // Two different facts, so two different strings — in every language, not just this one.
        //
        // Walked over every locale the site has a bundle for, not the choosable ones. The filtered
        // set can shrink to English alone — a locale that is not yet complete is not offered — and
        // then this loop proves nothing while still passing, which is the failure mode a
        // cross-locale claim is most prone to. The count is asserted so it cannot go quiet.
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
    /// <remarks>
    /// The rendered listing was localized and its <c>?plain=1</c> mirror was not: "-- from here:",
    /// "[archived]" and "[claimed]" were literals, so a German reader's text alternative carried
    /// three English words the page beside it does not. A mirror that answers in a different
    /// language from the page is not a mirror.
    /// </remarks>
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
        // The drawing keeps its row and column headers — they are what make the picture legible and
        // what the mouse tooltips hang off — but it is hidden from assistive tech, because announced
        // cell by cell it is 168 utterances that deliver one shape. The seven rows below it are the
        // text alternative, and they are a real table with a caption and headers of their own.
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
        // 168 cells with a word in each is the whole of finding B2: on a sparsely measured game a
        // screen reader was made to hear "not measured" 167 times before reaching the one number
        // that existed. The words stay in the tooltip, where a mouse reader gets them, and the facts
        // stay in the table below, where a listener gets them in seven rows.
        var html = Render.Words(await GridAsync(OneOfEach()));

        await Assert.That(html).Contains("title=\"Mon 03:00 — no measurement in this hour\"></td>");

        // Nothing announced anywhere in the grid's body — no cell text, and no sr-only span smuggling
        // one back in. The words are in the table below, seven rows of them.
        var body = html[html.IndexOf("<tbody", StringComparison.Ordinal)..];
        var grid = body[..body.IndexOf("</table>", StringComparison.Ordinal)];

        await Assert.That(grid).DoesNotContain("sr-only");
        await Assert.That(grid).DoesNotContain("</td>not");
    }

    [Test]
    public async Task BelowSevenMeasuredDaysThereIsNoGridAtAll()
    {
        // A 7×24 grid holding one probe is 167 empty cells, and it reads as a broken page to a
        // sighted reader and as a wall to a listening one. What we have, and what has to arrive
        // before there is a week to draw.
        var html = Render.Words(await GridAsync(
        [
            new(0, 0, 42, Probed: true),
        ]));

        await Assert.That(html).Contains("not enough measurements yet");
        await Assert.That(html).Contains("the busiest 42 on Monday at 00:00 UTC");
        await Assert.That(html).DoesNotContain("<table");

        // And no sentence about a week: "busiest Monday, small hours" off one Monday morning is a
        // claim about a shape one measurement cannot have.
        await Assert.That(html).DoesNotContain("Busiest");
    }

    /// <summary>
    /// The threshold itself, from both sides.
    /// </summary>
    /// <remarks>
    /// The test above uses one measured day, which passes whether the comparison is <c>&gt;=</c>,
    /// <c>&gt;</c> or off by one. Six against seven is the only pair that pins
    /// <see cref="ActivitySummary.MeasuredDaysForGrid"/> down, and two surfaces read it — the grid's
    /// own gate and the plain surface's.
    /// </remarks>
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

        // And the plain surface turns on the same day, or the two disagree about how much was
        // measured — which is the failure mode a shared constant with one caller invites.
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
        // The distinction has to survive with no cell shape and no colour at all, so it is in words
        // as well as in the cell's class: the whole sentence in the tooltip, and a column each in
        // the table below, where "not measured" and "no count" are two headings and never one.
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
        // The headers already say which hour of which day a cell is. Repeating that in every cell
        // turns 168 cells into 168 paragraphs, which is what the summary and the disclosure exist
        // to prevent.
        //
        // The two words are read from the glossary rather than written out here: they are locked ids
        // and this test's business is that the cell says the right *state*, not that English spells
        // it a particular way. A number is a number in every locale and stays written out.
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
    /// An hour that answered and could not be counted and an hour nobody has a measurement for are
    /// two facts, and a translation engine reaches for one phrase — <em>nicht verfügbar</em> — for
    /// both. A reader who cannot tell them apart cannot tell a game that answered from one that did
    /// not, which is the single thing this site exists to say. Asserted through the cell rather than
    /// through the glossary, because the cell is where a reader meets them.
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

            // And neither is a nought. A zero is a count we took, and both of these are the absence
            // of one — in every language, including the ones that have not been translated yet.
            await Assert.That(notMeasured).IsNotEqualTo("0");
            await Assert.That(notCounted).IsNotEqualTo("0");
        }
    }

    /// <summary>
    /// A German page names the days in German, and it does not get them from us.
    /// </summary>
    /// <remarks>
    /// Seven weekday strings compiled into a component are seven strings no translator is ever sent,
    /// and the symptom was a fully German sentence with "Monday" in the middle of it. They come from
    /// CLDR now, so the mapping is what can break: Monday is 0 in this codebase because that is how
    /// the store keys a week, and .NET's arrays start at Sunday.
    /// </remarks>
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

        // Every id here is untranslated today, so the German sentence is the English one — with the
        // day names, which are CLDR's rather than ours, already in German. That is the fallback
        // behaving: an English phrase in a German page says truthfully that this claim has not been
        // translated, and a day name has no excuse, because nobody had to translate it.
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
    /// <para>
    /// The whole reason the counts are ICU arguments rather than numbers glued to English nouns.
    /// English distinguishes one hour from two and Chinese distinguishes nothing, so an English
    /// message with a <c>one</c> branch must not hand that branch to a Chinese reader merely because
    /// it is there — which is precisely what a fallback does if the formatter picks branches by the
    /// source language rather than by the target.
    /// </para>
    /// <para>
    /// Counted as shapes rather than compared to expected strings: with the number blanked out, the
    /// number of distinct renderings a message can produce is the number of forms it emits, and that
    /// may never exceed the count of categories CLDR gives the language.
    /// </para>
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
    /// <remarks>
    /// A message that names <c>{day}</c> where the caller passes none is a <c>FormatException</c> on
    /// somebody's page rather than a wrong word, and the branch it hides in is the one that only a
    /// particular week reaches. This walks the shapes: a week measured at zero, a week nobody
    /// counted, a week of gaps, and a week with one hour in it.
    /// </remarks>
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
        // Hatched means "we got in and could not count". A game that has not answered the door
        // since 2023 did not get in, and saying it did is the same conflation one square over.
        //
        // Empty says only that no measurement exists for the hour — not that the game was down. A
        // presence row is written only when a probe got far enough to try counting, so silence here
        // cannot tell an outage of theirs from a gap of ours; that question belongs to the strip,
        // which is derived from intervals that can.
        var page = await new FixtureGameQueries().FindAsync("gaslight-row");

        await Assert.That(page!.Activity.All(c => c.IsGap)).IsTrue();
        await Assert.That(ActivitySummary.Sentence(English, page.Activity)).Contains("no measurement yet");
        await Assert.That(ActivitySummary.Sentence(English, page.Activity)).DoesNotContain("reachable");
        await Assert.That(ActivitySummary.Sentence(English, page.Activity)).DoesNotContain("answered but produced");
    }

    [Test]
    public async Task TheSummarySentenceArrivesBeforeTheGrid()
    {
        // The answer is a sentence, not a picture, and a reader should not have to reach the
        // picture to get it.
        var html = await GridAsync(FixtureActivity());

        var sentence = html.IndexOf("Busiest", StringComparison.Ordinal);
        var table = html.IndexOf("<table", StringComparison.Ordinal);

        await Assert.That(sentence).IsGreaterThanOrEqualTo(0);
        await Assert.That(sentence).IsLessThan(table);
    }

    [Test]
    public async Task AReadAsTextDisclosureGivesOneRowPerDayRatherThanAHundredAndSixtyEightCells()
    {
        // Seven rows — day, quietest, busiest, the hour the peak was in, and the two kinds of hour
        // that produced no number — behind one keystroke rather than a wall to scroll past.
        var html = Render.Words(await GridAsync(FixtureActivity()));

        await Assert.That(html).Contains("read as text — 7 rows");
        await Assert.That(html).Contains("<th scope=\"row\">Mon</th>");
        await Assert.That(html).Contains("<th scope=\"row\">Sun</th>");
        await Assert.That(html).Contains("Players on by day, in UTC.");
    }

    [Test]
    public async Task ThePlainSurfaceSaysTheSameThingAboutASparselyMeasuredGame()
    {
        // Plain mode is the mirror people actually rely on, so a change that removes a graphic has
        // to reach it too — otherwise the two surfaces disagree about how much was measured.
        var page = await new FixtureGameQueries().FindAsync("gaslight-row");
        var text = Render.Words(PlainText.Render(page!, Now));

        await Assert.That(text).Contains("The grid appears once every day of the week has one.");
        await Assert.That(text).DoesNotContain("Mon —");
    }

    [Test]
    public async Task TheSentenceNamesUnmeasuredAndUncountableHoursSeparately()
    {
        // The design's own specimen sentence says only "could not be measured", which covers both.
        // They are different facts about a game and the summary keeps them apart — an hour nobody
        // has a measurement for, and an hour we reached and could not count.
        var sentence = ActivitySummary.Sentence(English, FixtureActivity());

        await Assert.That(sentence).Contains("have no measurement yet");
        await Assert.That(sentence).Contains("answered but produced no count");
    }

    [Test]
    public async Task AGameCountedAtZeroAllWeekIsNotDescribedAsUnmeasured()
    {
        // A week of measured zeros is a strong measurement, not an absence of one.
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
        // Midnight Sun II answers on every probe and offers nothing countable. Rendering that as a
        // week of zeros — or as darkness — would be the reported bug in both directions.
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
