using System.Text.RegularExpressions;

using MUI.Web.Components;

namespace MUI.Web.Tests;

/// <summary>
/// The two things a reader chooses about the trend — how far back, and which drawing — and the one
/// that stopped either from working.
/// </summary>
/// <remarks>
/// Every selector on this page is a query with no path in front of it, meaning <em>this page, asked
/// differently</em>. With <c>&lt;base href="/"&gt;</c> in the template, a query-only href instead
/// resolved against the base, sending every range link and the plain-text link to the home page.
/// </remarks>
public class TrendSelectorTests
{
    [Test]
    public async Task NoBaseElementSendsAQueryOnlyLinkOffThePageItIsOn()
    {
        await using var site = await SiteHost.StartAsync();

        var page = await site.Client.GetStringAsync("/g/aardwolf");

        await Assert.That(page).DoesNotContain("<base ")
            .Because("a base element resolves '?from=…' against itself and lands on the home page");

        // A relative "app.css" under /g/aardwolf would ask for /g/app.css.
        foreach (var asset in new[] { "favicon.svg", "favicon.ico", "apple-touch-icon.png", "site.webmanifest" })
        {
            await Assert.That(page).Contains($"href=\"/{asset}\"");
        }
    }

    [Test]
    public async Task EachSelectorKeepsWhatTheOtherOneChose()
    {
        await using var site = await SiteHost.StartAsync();

        var page = await site.Client.GetStringAsync("/g/aardwolf?from=2026-01-01&to=2026-03-31&chart=bar");
        var hrefs = Regex.Matches(page, "<a href=\"(\\?[^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToList();

        await Assert.That(hrefs.Any(h => h.Contains("from=") && h.Contains("chart=bar"))).IsTrue()
            .Because("a range link has to carry the shape the reader is already on");
        await Assert.That(hrefs.Any(h => h.Contains("from=2026-01-01") && !h.Contains("chart="))).IsTrue()
            .Because("the link back to the line has to carry the range the reader is already on");
    }

    [Test]
    public async Task TheDefaultIsTheLineAndTheColumnsAreAChoice()
    {
        var series = Series(90, i => i is 73 or 88 or 89);

        var line = await Render.ComponentAsync<PresenceTrend>(
            new Dictionary<string, object?> { ["Series"] = series });
        var bars = await Render.ComponentAsync<PresenceTrend>(
            new Dictionary<string, object?> { ["Series"] = series, ["Shape"] = TrendShape.Bar });

        await Assert.That(line).Contains("class=\"dot\"")
            .Because("two of the three measured days have no neighbour, so the line draws them as points");
        await Assert.That(line).DoesNotContain("class=\"peak\"");

        await Assert.That(bars).Contains("class=\"mean\"");
        await Assert.That(bars).DoesNotContain("class=\"dot\"");
    }

    [Test]
    public async Task AShapeNobodyOffersIsTheLineRatherThanAnError()
    {
        // A mistyped shape on a browsable URL is a reader's typo, not a fault.
        await Assert.That(TrendShapes.Parse(null)).IsEqualTo(TrendShape.Line);
        await Assert.That(TrendShapes.Parse("")).IsEqualTo(TrendShape.Line);
        await Assert.That(TrendShapes.Parse("pie")).IsEqualTo(TrendShape.Line);
        await Assert.That(TrendShapes.Parse("BAR")).IsEqualTo(TrendShape.Bar);
        await Assert.That(TrendShapes.Parse("bars")).IsEqualTo(TrendShape.Bar);

        await Assert.That(TrendShapes.Parse(TrendShape.Bar.Slug())).IsEqualTo(TrendShape.Bar);
        await Assert.That(TrendShapes.Parse(TrendShape.Line.Slug())).IsEqualTo(TrendShape.Line);
    }

    private static TrendSeries Series(int days, Func<int, bool> counted)
    {
        var from = new DateOnly(2026, 5, 19);
        var list = Enumerable.Range(0, days)
            .Select(i => counted(i)
                ? new TrendDay(from.AddDays(i), 4, 0, 600, 731, 666.1m)
                : new TrendDay(from.AddDays(i), 0, 0, null, null, null))
            .ToList();

        return new TrendSeries(from, from.AddDays(days - 1), list);
    }
}
