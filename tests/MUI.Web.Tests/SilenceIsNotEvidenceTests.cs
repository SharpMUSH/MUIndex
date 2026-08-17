using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Web.Components;
using MUI.Web.Localization;

namespace MUI.Web.Tests;

/// <summary>
/// Three claims a page must not make about a game it has barely measured.
/// </summary>
/// <remarks>
/// <para>
/// All three were live, all three were found the first time the site rendered a real crawl rather
/// than the fixture, and all three are the same mistake: a surface stating <em>our</em> absence of
/// data as a <em>measurement</em> of the game. The fixture could not have caught any of them,
/// because a fixture is written by someone who already knows what each panel is supposed to say.
/// </para>
/// <para>
/// The shape to watch for when adding a surface: a denominator that is a window rather than what was
/// observed, a rendering of silence that names a cause, and an internal row escaping into a panel
/// that says a game claimed something.
/// </para>
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

        // The number itself was always right — FractionReachable divides by observed time. It was
        // the sentence that widened it to the window.
        await Assert.That(summary.Sentence).Contains("we have measured");
        await Assert.That(summary.Sentence).DoesNotContain("of the last 90 days");
        await Assert.That(summary.Sentence).Contains("89 days predate anything we measured");
    }

    /// <summary>A window we watched all of may say so, and says it the short way.</summary>
    [Test]
    public async Task AFractionOverAFullyMeasuredWindowStillNamesTheWindow()
    {
        var summary = ReachSeries.Build(
            [Reachable(from: Now.AddDays(-120), to: null)],
            Now);

        await Assert.That(summary.Sentence).Contains("Reachable 100.0% of the last 90 days.");
        await Assert.That(summary.Sentence).DoesNotContain("predate");
    }

    /// <summary>
    /// An hour with no presence row is not an hour the game was down.
    /// </summary>
    /// <remarks>
    /// A failed probe writes no presence row at all — it goes to the availability writer — so an
    /// empty cell cannot distinguish an outage of theirs from a gap of ours. The live version of
    /// this described a game measured once, and found perfectly reachable, as unreachable for 167
    /// hours of the week.
    /// </remarks>
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

        // The locked id, not the English spelling of it: what this test guards is that the hour is
        // called not-measured rather than given a cause, in whatever language the reader asked for.
        await Assert.That(ActivitySummary.CellValue(Locales.SourceTag, cell))
            .IsEqualTo(Messages.For(Locales.SourceTag, "state.notMeasured"));

        await Assert.That(ActivitySummary.PerDay(Locales.SourceTag, week).First()).Contains("not measured");

        // And no locale names a cause for it either. "unreachable" is a different locked id with a
        // different translation in every bundle, and an hour with no presence row is not one.
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
    /// <remarks>
    /// <c>banner_hash</c> is a digest we computed, not a claim the game made, and it rendered as
    /// sixty-four hex characters in "what the game says about itself" — off the edge of the column.
    /// It is deliberately absent from the field registry, so nothing but this list excludes it.
    /// </remarks>
    [Test]
    public async Task InternalFieldsAreNotThingsAGameSaidAboutItself()
    {
        await Assert.That(InternalFields.IsInternal(InternalFields.BannerHash)).IsTrue();
        await Assert.That(InternalFields.IsInternal(InternalFields.ConnectScreen)).IsTrue();
        await Assert.That(InternalFields.IsInternal("connect_screen_suppressed")).IsTrue();

        await Assert.That(InternalFields.IsInternal("NAME")).IsFalse();
        await Assert.That(InternalFields.IsInternal("CODEBASE")).IsFalse();

        // Not in the registry, which is exactly why a registry-shaped guard would have missed it.
        await Assert.That(FieldRegistry.Instance.Find(InternalFields.BannerHash)).IsNull();
    }

    private static AvailabilityInterval Reachable(DateTimeOffset from, DateTimeOffset? to) => new()
    {
        GameId = Guid.NewGuid(),
        State = AvailabilityState.Reachable,
        FromAt = from,
        ToAt = to,
    };
}
