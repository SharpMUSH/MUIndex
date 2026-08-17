using MUI.Web.Components;

namespace MUI.Web.Tests;

/// <summary>
/// One date format, one age ladder, and an age a reader can resolve to a time.
/// </summary>
/// <remarks>
/// The site printed three absolute formats — <c>2026-08-17</c> on a game page, <c>31 July 2026</c>
/// in the rankings, <c>Aug 2026</c> beside an address — and its relative ages carried no absolute
/// value at all, so a reader arriving from a cached page or a search result could not tell what
/// "19m ago" was 19 minutes before.
/// </remarks>
public class TimeSurfaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 14, 21, 0, TimeSpan.Zero);

    [Test]
    public async Task ThereIsOneAbsoluteFormatAndItNamesItsZone()
    {
        var at = new DateTimeOffset(2026, 8, 17, 14, 2, 0, TimeSpan.Zero);

        await Assert.That(Dates.Absolute(at)).IsEqualTo("17 Aug 2026");
        await Assert.That(Dates.Stamp(at)).IsEqualTo("17 Aug 2026 14:02 UTC");
        await Assert.That(Dates.Machine(at)).IsEqualTo("2026-08-17T14:02:00Z");
    }

    // There is no test that a host culture cannot bend these, because this deployment runs in
    // globalization-invariant mode and a test would have to leave it to assert anything: the
    // CultureInfo it needed to construct does not exist here. Dates asks for InvariantCulture by
    // name anyway — the day somebody turns ICU on, the dates stay the shape the site declares.

    [Test]
    public async Task TheAgeLadderRunsMinutesToNinetyThenHoursToFortyEight()
    {
        await Assert.That(Relative.Format(TimeSpan.FromSeconds(30))).IsEqualTo("now");
        await Assert.That(Relative.Format(TimeSpan.FromMinutes(84))).IsEqualTo("84m");
        await Assert.That(Relative.Format(TimeSpan.FromMinutes(95))).IsEqualTo("1h");
        await Assert.That(Relative.Format(TimeSpan.FromHours(47))).IsEqualTo("47h");
        await Assert.That(Relative.Format(TimeSpan.FromHours(49))).IsEqualTo("2d");
    }

    [Test]
    public async Task AnAgeCarriesTheInstantItIsRelativeTo()
    {
        var html = await Render.ComponentAsync<Moment>(new()
        {
            ["At"] = new DateTimeOffset(2026, 8, 17, 14, 2, 0, TimeSpan.Zero),
            ["Now"] = Now,
        });

        await Assert.That(html).Contains("<time datetime=\"2026-08-17T14:02:00Z\"");
        await Assert.That(html).Contains("title=\"19m ago, 17 Aug 2026 14:02 UTC\"");
        await Assert.That(Render.Words(html)).Contains(">19m</time>");
    }

    [Test]
    public async Task TheAbsoluteTimeIsSpokenWhereAReaderIsWeighingOneFactAndNotWhereTheyAreScanning()
    {
        // A listing row that announced the absolute time of every age would be the wall of
        // repetition this whole pass exists to remove; a game page's field, where the age is the
        // fact being weighed, is exactly where it belongs.
        var parameters = new Dictionary<string, object?>
        {
            ["At"] = new DateTimeOffset(2026, 8, 17, 14, 2, 0, TimeSpan.Zero),
            ["Now"] = Now,
        };

        var scanning = await Render.ComponentAsync<Moment>(parameters);
        var weighing = await Render.ComponentAsync<Moment>(
            new(parameters) { ["Spoken"] = true });

        await Assert.That(scanning).DoesNotContain("sr-only");
        await Assert.That(Render.Words(weighing)).Contains("17 Aug 2026 14:02 UTC");
    }
}
