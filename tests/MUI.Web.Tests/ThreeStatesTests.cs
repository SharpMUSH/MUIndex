using MUI.Catalog;
using MUI.Web.Components;
using MUI.Web.Fixtures;

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
    private static readonly DateTimeOffset Now = FixtureGameQueries.Now;

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
        await Assert.That(ActivitySummary.CellValue(new ActivityCell(0, 0, 4, true))).IsEqualTo("4");
        await Assert.That(ActivitySummary.CellValue(new ActivityCell(0, 1, 0, true))).IsEqualTo("0");
        await Assert.That(ActivitySummary.CellValue(new ActivityCell(0, 2, null, true))).IsEqualTo("not counted");
        await Assert.That(ActivitySummary.CellValue(new ActivityCell(0, 3, null, false))).IsEqualTo("not measured");
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
        await Assert.That(ActivitySummary.Sentence(page.Activity)).Contains("no measurement yet");
        await Assert.That(ActivitySummary.Sentence(page.Activity)).DoesNotContain("reachable");
        await Assert.That(ActivitySummary.Sentence(page.Activity)).DoesNotContain("answered but produced");
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
        var sentence = ActivitySummary.Sentence(FixtureActivity());

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

        var sentence = ActivitySummary.Sentence(cells);

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
