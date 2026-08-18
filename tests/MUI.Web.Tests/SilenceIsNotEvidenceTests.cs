using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Web.Components;
using MUI.Web.Components.Pages;
using MUI.Web.Fixtures;
using MUI.Web.Localization;

namespace MUI.Web.Tests;

/// <summary>
/// Three claims a page must not make about a game it has barely measured.
/// </summary>
/// <remarks>
/// All three are the same mistake: a surface stating <em>our</em> absence of data as a
/// <em>measurement</em> of the game. Watch for it when adding a surface: a denominator that's a
/// window rather than what was observed, a rendering of silence that names a cause, or an internal
/// row escaping into a panel that says a game claimed something.
/// </remarks>
public class SilenceIsNotEvidenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// One successful probe an hour ago is not ninety days of evidence.
    /// </summary>
    [Test]
    public async Task AFractionOverAPartlyMeasuredWindowNamesTheMeasuredDenominator()
    {
        var summary = ReachSeries.Build(
            [Reachable(from: Now.AddHours(-1), to: null)],
            Now);

        // FractionReachable divides by observed time, not the window.
        var sentence = summary.Sentence(Locales.SourceTag);

        await Assert.That(sentence).Contains(Say("reach.fraction.measured", ("percent", "100.0%"), ("days", 1)));
        await Assert.That(sentence).DoesNotContain(Say("reach.fraction.window", ("percent", "100.0%"), ("days", 90)));
        await Assert.That(sentence).Contains(Say("reach.predate", ("count", 89)));
    }

    /// <summary>A window we watched all of may say so, and says it the short way.</summary>
    [Test]
    public async Task AFractionOverAFullyMeasuredWindowStillNamesTheWindow()
    {
        var summary = ReachSeries.Build(
            [Reachable(from: Now.AddDays(-120), to: null)],
            Now);

        await Assert.That(summary.Sentence(Locales.SourceTag)).IsEqualTo(
            Say("reach.fraction.window", ("percent", "100.0%"), ("days", 90))
            + " " + Messages.For(Locales.SourceTag, "reach.unreachable.noneInWindow"));
    }

    private static string Say(string id, params (string Key, object? Value)[] args) =>
        Messages.For(
            Locales.SourceTag,
            id,
            args.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal));

    /// <summary>
    /// An hour with no presence row is not an hour the game was down.
    /// </summary>
    /// <remarks>A failed probe writes no presence row (it goes to the availability writer), so an empty cell can't distinguish an outage of theirs from a gap of ours.</remarks>
    [Test]
    public async Task AnHourWithNoSampleIsNotReportedAsUnreachable()
    {
        List<ActivityCell> week = [new(6, 20, 14, Probed: true)];
        for (var day = 0; day < 7; day++)
        {
            for (var hour = 0; hour < 24; hour++)
            {
                if (day != 6 || hour != 20)
                {
                    week.Add(new ActivityCell(day, hour, null, Probed: false));
                }
            }
        }

        var sentence = ActivitySummary.Sentence(Locales.SourceTag, week);

        await Assert.That(sentence).Contains("have no measurement yet");
        await Assert.That(sentence).DoesNotContain("not reachable");
        await Assert.That(sentence).DoesNotContain("unreachable");

        var cell = new ActivityCell(3, 4, null, Probed: false);
        await Assert.That(ActivitySummary.CellLabel(Locales.SourceTag, cell)).DoesNotContain("reachable");

        // The locked id: the hour must be called not-measured, never given a cause.
        await Assert.That(ActivitySummary.CellValue(Locales.SourceTag, cell))
            .IsEqualTo(Messages.For(Locales.SourceTag, "state.notMeasured"));

        await Assert.That(ActivitySummary.PerDay(Locales.SourceTag, week).First()).Contains("not measured");

        foreach (var locale in Locales.All)
        {
            await Assert.That(ActivitySummary.CellValue(locale.Tag, cell))
                .IsNotEqualTo(Messages.For(locale.Tag, "state.unreachable"))
                .Because($"{locale.Tag} calls an unmeasured hour unreachable");
        }
    }

    /// <summary>
    /// Working state is not a self-report, whatever panel it would otherwise land in.
    /// </summary>
    /// <remarks><c>banner_hash</c> is a digest we computed, not a claim the game made — it used to render as 64 hex characters in "what the game says about itself".</remarks>
    [Test]
    public async Task InternalFieldsAreNotThingsAGameSaidAboutItself()
    {
        await Assert.That(InternalFields.IsInternal(InternalFields.BannerHash)).IsTrue();
        await Assert.That(InternalFields.IsInternal(InternalFields.ConnectScreen)).IsTrue();
        await Assert.That(InternalFields.IsInternal("connect_screen_suppressed")).IsTrue();

        await Assert.That(InternalFields.IsInternal("NAME")).IsFalse();
        await Assert.That(InternalFields.IsInternal("CODEBASE")).IsFalse();

        // Not in the registry — a registry-shaped guard would have missed it.
        await Assert.That(FieldRegistry.Instance.Find(InternalFields.BannerHash)).IsNull();
    }

    /// <summary>
    /// A missing count is not a count somebody failed to read.
    /// </summary>
    /// <remarks>The hero previously said <c>state.notCounted</c> whenever <c>PlayersNow</c> was null, but that string is reserved for rule 2's middle state — an hour we reached but couldn't read a count from.</remarks>
    [Test]
    public async Task AGamePageWithNoCountNamesNoCauseForNotHavingOne()
    {
        var page = await new FixtureGameQueries().FindAsync("gaslight-row");

        await Assert.That(page!.Summary.PlayersNow).IsNull();

        // The strip and grid below are derived from intervals/presence rows and can tell the states
        // apart; this figure can't, so it may not use "unreachable" or "uncounted" either.
        var raw = await Render.PageAsync<Game>(new() { ["Slug"] = "gaslight-row" });
        var opens = raw.IndexOf("class=\"game-figure\"", StringComparison.Ordinal);

        await Assert.That(opens).IsGreaterThanOrEqualTo(0);

        var figure = Render.Words(raw[opens..raw.IndexOf("</div>", opens, StringComparison.Ordinal)]);

        var count = PlainText.Render(page, Now)
            .ReplaceLineEndings("\n")
            .Split('\n')
            .First(line => line.StartsWith("Players now:", StringComparison.Ordinal));

        foreach (var text in new[] { figure, count })
        {
            await Assert.That(text).DoesNotContain(Messages.For(Locales.SourceTag, "state.notCounted"));
            await Assert.That(text).DoesNotContain(Messages.For(Locales.SourceTag, "state.uncounted"));
            await Assert.That(text).DoesNotContain(Messages.For(Locales.SourceTag, "state.unreachable"));

            // Never a nought either — the other direction of the same mistake.
            await Assert.That(text).DoesNotContain("0");
        }

        await Assert.That(figure).Contains(Messages.For(Locales.SourceTag, "game.count.none"));
        await Assert.That(figure).Contains(Messages.For(Locales.SourceTag, "game.count.none.why"));
        await Assert.That(count).IsEqualTo(Messages.For(Locales.SourceTag, "game.plain.playersNoCount"));
    }

    private static AvailabilityInterval Reachable(DateTimeOffset from, DateTimeOffset? to) => new()
    {
        GameId = Guid.NewGuid(),
        State = AvailabilityState.Reachable,
        FromAt = from,
        ToAt = to,
    };
}
