using System.Net;
using System.Text.Json;

using MUI.Web.Api;

namespace MUI.Web.Tests.Api;

/// <summary>
/// The owner-published badge (spec §8.5), on a page we do not control.
/// </summary>
/// <remarks>The hardest point to assert these rules at: a badge has no footnote, no provenance chip, no second sentence to fall back on.</remarks>
public class BadgeApiTests
{
    /// <summary>The count carries its own age, because there is nowhere else to put one.</summary>
    [Test]
    public async Task AMeasuredCountIsPublishedWithItsAge()
    {
        await using var host = await ApiHost.StartAsync();

        var svg = await host.Client.GetStringAsync("/g/m-u-s-h/badge.svg");

        await Assert.That(svg).Contains("15 now");
        await Assert.That(svg).Contains("4m");
        await Assert.That(svg).Contains("mu*index");
    }

    /// <summary>
    /// A count the game asserted about itself never goes out as a measurement of ours.
    /// </summary>
    /// <remarks>
    /// Ashen Court publishes <c>PLAYERS</c> in MSSP and answers no pre-login <c>WHO</c>; the badge
    /// must not launder that into "measured". Aardwolf's count, read off the connect screen
    /// ourselves, is the control showing the line is drawn on <em>source</em>, not on who authored
    /// the number.
    /// </remarks>
    [Test]
    public async Task ADeclaredCountIsNotPublishedAsAMeasuredOne()
    {
        await using var host = await ApiHost.StartAsync();

        var declared = await host.Client.GetStringAsync("/g/ashen-court/badge.svg");
        var json = await Json.ElementAsync(await host.Client.GetAsync("/g/ashen-court/badge.json"));
        var measured = await host.Client.GetStringAsync("/g/m-u-s-h/badge.svg");
        var banner = await host.Client.GetStringAsync("/g/aardwolf/badge.svg");

        await Assert.That(declared).Contains("players unknown");
        await Assert.That(declared).DoesNotContain("9 now")
            .Because("nine is what the game says about itself, not what we counted");

        await Assert.That(json.GetProperty("state").GetString()).IsEqualTo("unknown");
        await Assert.That(json.GetProperty("count").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(json.GetProperty("measuredAt").ValueKind).IsEqualTo(JsonValueKind.Null)
            .Because("an instant beside no count of ours would name a measurement nobody took");

        await Assert.That(measured).Contains("15 now");

        await Assert.That(banner).Contains("219 now")
            .Because("we read that number off the connect screen ourselves, which is a measurement");
    }

    /// <summary>
    /// A measured zero is a measurement and says so; an unmeasured count never borrows its shape.
    /// </summary>
    /// <remarks>Rule 4: Eldertale was probed and empty (a real zero); Midnight Sun couldn't be counted and renders as unknown, never "0".</remarks>
    [Test]
    public async Task AMeasuredZeroIsAZeroAndAnUnknownCountIsNever()
    {
        await using var host = await ApiHost.StartAsync();

        var zero = await host.Client.GetStringAsync("/g/eldertale/badge.svg");
        var unknown = await host.Client.GetStringAsync("/g/midnight-sun/badge.svg");

        await Assert.That(zero).Contains("0 now");

        await Assert.That(unknown).Contains("players unknown");
        await Assert.That(unknown).DoesNotContain("0 now");
        await Assert.That(unknown).DoesNotContain(">0<");
    }

    /// <summary>
    /// Unknown is grey. The accent means measured everywhere on this site, and a badge is no place
    /// to start spending it on something we did not measure.
    /// </summary>
    [Test]
    public async Task OnlyAMeasuredBadgeWearsTheMeasuredColour()
    {
        await using var host = await ApiHost.StartAsync();

        var measured = await host.Client.GetStringAsync("/g/m-u-s-h/badge.svg");
        var unknown = await host.Client.GetStringAsync("/g/midnight-sun/badge.svg");

        await Assert.That(measured).Contains("#35d29a");
        await Assert.That(unknown).DoesNotContain("#35d29a");
    }

    /// <summary>
    /// An archived game's badge says archived rather than showing the last number it had.
    /// </summary>
    /// <remarks>§7.5: archiving doesn't stop the badge answering, but a live-count label on a stale number is what it must not print.</remarks>
    [Test]
    public async Task AnArchivedGameGetsABadgeThatSaysSo()
    {
        await using var host = await ApiHost.StartAsync();

        var svg = await host.Client.GetStringAsync("/g/gaslight-row/badge.svg");

        await Assert.That(svg).Contains("archived");
        await Assert.That(svg).DoesNotContain("now ·");
    }

    /// <summary>
    /// A game's name is MSSP text, and MSSP text is attacker-controlled.
    /// </summary>
    /// <remarks>An SVG is a document; an unescaped name would be a stored XSS with a distribution mechanism (every page embedding the badge).</remarks>
    [Test]
    public async Task AGamesOwnNameCannotBecomeMarkup()
    {
        var reading = new BadgeReading(BadgeState.Unknown, null, null, null);

        var svg = PlayerBadge.Svg(reading, "</text><script>alert(1)</script>");

        await Assert.That(svg).DoesNotContain("<script>");
        await Assert.That(svg).Contains("&lt;script&gt;");
    }

    /// <summary>A badge is cached longer than the API, and still well inside a count's own two-hour staleness window.</summary>
    [Test]
    public async Task ABadgeIsCacheableAndRevalidates()
    {
        await using var host = await ApiHost.StartAsync();

        var first = await host.Client.GetAsync("/g/m-u-s-h/badge.svg");

        await Assert.That(first.Headers.CacheControl!.ToString()).IsEqualTo("public, max-age=300");
        await Assert.That(first.Headers.ETag).IsNotNull();

        var request = new HttpRequestMessage(HttpMethod.Get, "/g/m-u-s-h/badge.svg");
        request.Headers.Add("If-None-Match", first.Headers.ETag!.ToString());

        var second = await host.Client.SendAsync(request);

        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.NotModified);
        await Assert.That((await second.Content.ReadAsByteArrayAsync()).Length).IsEqualTo(0);
    }

    /// <summary>The JSON route gets the same five minutes as the SVG one — previously only the SVG route was asserted on, and the JSON route silently shipped the 60s API default.</summary>
    [Test]
    public async Task TheJsonBadgeIsCachedForAsLongAsTheImage()
    {
        await using var host = await ApiHost.StartAsync();

        var response = await host.Client.GetAsync("/g/m-u-s-h/badge.json");

        await Assert.That(response.Headers.CacheControl!.ToString()).IsEqualTo("public, max-age=300");
        await Assert.That(response.Headers.ETag).IsNotNull();
    }

    /// <summary>It is embedded cross-origin by definition, and served as an image.</summary>
    [Test]
    public async Task ABadgeIsServedAsAnImageAnybodyMayEmbed()
    {
        await using var host = await ApiHost.StartAsync();

        var response = await host.Client.GetAsync("/g/m-u-s-h/badge.svg");

        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("image/svg+xml");
        await Assert.That(response.Headers.GetValues("Access-Control-Allow-Origin").Single())
            .IsEqualTo("*");
        await Assert.That(response.Headers.GetValues("X-Content-Type-Options").Single())
            .IsEqualTo("nosniff");
    }

    /// <summary>A slug the game used to wear still serves its badge, permanently — §5.7's forever-redirect, tested where it matters most (a badge pasted into a template and forgotten).</summary>
    [Test]
    public async Task ABadgeUrlSurvivesARename()
    {
        await using var host = await ApiHost.StartAsync(new Dictionary<string, string?>
        {
            ["SlugAliases:tidewater-nights"] = "m-u-s-h",
        });

        var response = await host.Client.GetAsync("/g/tidewater-nights/badge.svg");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
        await Assert.That(response.Headers.Location!.ToString()).IsEqualTo("/g/m-u-s-h/badge.svg");
    }

    /// <summary>A slug naming nothing gets a badge that says so, and is never cached.</summary>
    [Test]
    public async Task AnUnknownSlugSaysSoOnTheBadgeItself()
    {
        await using var host = await ApiHost.StartAsync();

        var response = await host.Client.GetAsync("/g/no-such-game/badge.svg");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains("unknown game");
        await Assert.That(response.Headers.CacheControl!.NoStore).IsTrue();
    }

    /// <summary>The JSON publishes null and a named state, so a consumer cannot coerce silence to zero.</summary>
    [Test]
    public async Task TheJsonNamesTheStateAsWellAsCarryingTheNumber()
    {
        await using var host = await ApiHost.StartAsync();

        var counted = await Json.ElementAsync(await host.Client.GetAsync("/g/m-u-s-h/badge.json"));
        var unknown = await Json.ElementAsync(
            await host.Client.GetAsync("/g/midnight-sun/badge.json"));

        await Assert.That(counted.GetProperty("count").GetInt32()).IsEqualTo(15);
        await Assert.That(counted.GetProperty("state").GetString()).IsEqualTo("measured");
        await Assert.That(counted.GetProperty("measuredAt").ValueKind).IsNotEqualTo(JsonValueKind.Null);
        await Assert.That(counted.GetProperty("ageSeconds").GetDouble()).IsGreaterThanOrEqualTo(0);

        await Assert.That(unknown.GetProperty("count").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(unknown.GetProperty("state").GetString()).IsEqualTo("unknown");
        await Assert.That(unknown.GetProperty("measuredAt").ValueKind).IsEqualTo(JsonValueKind.Null);
    }

    /// <summary>The JSON points back at the page and at the image, so one fetch finds the rest.</summary>
    [Test]
    public async Task TheJsonLinksToThePageAndTheBadge()
    {
        await using var host = await ApiHost.StartAsync();

        var badge = await Json.ElementAsync(await host.Client.GetAsync("/g/ashen-court/badge.json"));

        await Assert.That(badge.GetProperty("pageUrl").GetString()).IsEqualTo("/g/ashen-court");
        await Assert.That(badge.GetProperty("badgeUrl").GetString())
            .IsEqualTo("/g/ashen-court/badge.svg");
    }

    /// <summary>A count and its age travel together or not at all — a count with no provenance chip reads as unknown, not as a bare figure.</summary>
    [Test]
    public async Task ACountWithNoAgeIsNotPublishedAsACount()
    {
        var unlabelled = new MUI.Catalog.GameSummary(
            Guid.CreateVersion7(),
            "orphan",
            "Orphan",
            null,
            MUI.Catalog.LifecycleState.Active,
            IsClaimed: false,
            PlayersNow: 12,
            Codebase: null,
            MeasuredProtocols: [],
            LastReachableAt: DateTimeOffset.UtcNow,
            PlayersNowProvenance: null);

        var reading = PlayerBadge.Read(unlabelled, DateTimeOffset.UtcNow);

        await Assert.That(reading.State).IsEqualTo(BadgeState.Unknown);
        await Assert.That(reading.Count).IsNull();
        await Assert.That(reading.Text).IsEqualTo("players unknown");
    }
}
