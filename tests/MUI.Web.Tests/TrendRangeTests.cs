using MUI.Web.Components;

namespace MUI.Web.Tests;

/// <summary>
/// The calendar range in the address, which is what makes seeking work without a line of script.
/// </summary>
/// <remarks>
/// Everything is clamped rather than refused — a mistyped date isn't worth a 400 in front of a page
/// that has an answer to give. It must never draw a range different from the one the caption prints,
/// so the resolved range is what both the graphic and plain rendering are built from.
/// </remarks>
public class TrendRangeTests
{
    private static readonly DateOnly Today = new(2026, 8, 16);

    [Test]
    public async Task NoRangeIsTheLastNinetyDaysEndingToday()
    {
        // Matches the reachability strip beside it, so both graphics read the same stretch of calendar.
        var range = TrendRange.Parse(null, null, Today);

        await Assert.That(range.To).IsEqualTo(Today);
        await Assert.That(range.Days).IsEqualTo(90);
    }

    [Test]
    public async Task OneEndGivenIsARealRequest()
    {
        var since = TrendRange.Parse("2026-08-01", null, Today);

        await Assert.That(since.From).IsEqualTo(new DateOnly(2026, 8, 1));
        await Assert.That(since.To).IsEqualTo(Today);

        var until = TrendRange.Parse(null, "2026-03-31", Today);

        await Assert.That(until.To).IsEqualTo(new DateOnly(2026, 3, 31));
        await Assert.That(until.Days).IsEqualTo(90);
    }

    [Test]
    public async Task NonsenseResolvesRatherThanErrors()
    {
        var backwards = TrendRange.Parse("2026-08-10", "2026-08-01", Today);

        await Assert.That(backwards.From).IsEqualTo(new DateOnly(2026, 8, 1));
        await Assert.That(backwards.To).IsEqualTo(new DateOnly(2026, 8, 10));

        var rubbish = TrendRange.Parse("last tuesday", "soon", Today);

        await Assert.That(rubbish.Days).IsEqualTo(90);
        await Assert.That(rubbish.To).IsEqualTo(Today);

        var future = TrendRange.Parse("2026-08-01", "2027-01-01", Today);

        await Assert.That(future.To).IsEqualTo(Today);
    }

    [Test]
    public async Task ARangeIsNeverWiderThanOneResponseAnswers()
    {
        // The ceiling is the API's own bound at this grain.
        var enormous = TrendRange.Parse("1990-01-01", "2026-08-16", Today);

        await Assert.That(enormous.Days).IsEqualTo(TrendSeries.MaximumDays);
        await Assert.That(enormous.To).IsEqualTo(Today);
    }

    [Test]
    public async Task SeekingMovesAWholeWidthAndStopsAtToday()
    {
        var range = TrendRange.Preset(30, Today);

        await Assert.That(range.Days).IsEqualTo(30);
        await Assert.That(range.Covers(30, Today)).IsTrue();

        var earlier = range.Previous();

        await Assert.That(earlier.To).IsEqualTo(range.From.AddDays(-1));
        await Assert.That(earlier.Days).IsEqualTo(30);

        // No "later" from a range that already ends today.
        await Assert.That(range.Next(Today)).IsNull();

        var next = earlier.Next(Today);

        await Assert.That(next).IsNotNull();
        await Assert.That(next!.To).IsEqualTo(Today);
    }

    [Test]
    public async Task TheQueryRoundTrips()
    {
        // The address is the state; it must survive being written and read back.
        var range = TrendRange.Preset(45, Today).Previous();
        var parts = range.Query.Split('&');
        var from = parts[0]["from=".Length..];
        var to = parts[1]["to=".Length..];

        await Assert.That(TrendRange.Parse(from, to, Today)).IsEqualTo(range);
    }
}
