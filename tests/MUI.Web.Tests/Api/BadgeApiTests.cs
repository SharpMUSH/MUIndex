using System.Net;
using System.Text.Json;

using MUI.Web.Api;

namespace MUI.Web.Tests.Api;

/// <summary>
/// The owner-published badge (spec §8.5), on a page we do not control.
/// </summary>
/// <remarks>
/// Every rule this site has, asserted at its hardest point. A badge is embedded where there is no
/// footnote, no provenance chip and no second sentence — so if a number can be published unlabelled
/// or a silence can be published as a zero, it happens here first and we never see it.
/// </remarks>
public class BadgeApiTests
{
    /// <summary>
    /// The count carries its own age, because there is nowhere else to put one.
    /// </summary>
    /// <remarks>
    /// "15" on somebody's front page is the incumbents' badge. "15 now · 4m ago" is this site's
    /// claim, in nine more characters.
    /// </remarks>
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
    /// <para>
    /// Ashen Court publishes <c>PLAYERS</c> in MSSP and answers no pre-login <c>WHO</c>, which is
    /// the commonest way a directory ends up quoting a game's own number as though it had counted
    /// it. This badge writes "N players measured 4m ago" and paints it in the accent that means
    /// measured on every other surface here — on a page we do not control and cannot correct — so
    /// the one thing it must never carry is somebody else's assertion.
    /// </para>
    /// <para>
    /// The badge and <c>/api/games/{slug}</c> are held to the same predicate rather than to two
    /// judgements that agree today: the JSON below says <c>unknown</c> where the API says
    /// <c>declared</c>, and both come from <c>ProvenanceChip.IsMeasured</c>. M*U*S*H is the control
    /// — a <c>WHO</c> count, and it still goes out as a number.
    /// </para>
    /// </remarks>
    [Test]
    public async Task ADeclaredCountIsNotPublishedAsAMeasuredOne()
    {
        await using var host = await ApiHost.StartAsync();

        var declared = await host.Client.GetStringAsync("/g/ashen-court/badge.svg");
        var json = await Json.ElementAsync(await host.Client.GetAsync("/g/ashen-court/badge.json"));
        var measured = await host.Client.GetStringAsync("/g/m-u-s-h/badge.svg");

        await Assert.That(declared).Contains("players unknown");
        await Assert.That(declared).DoesNotContain("9 now")
            .Because("nine is what the game says about itself, not what we counted");

        await Assert.That(json.GetProperty("state").GetString()).IsEqualTo("unknown");
        await Assert.That(json.GetProperty("count").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(json.GetProperty("measuredAt").ValueKind).IsEqualTo(JsonValueKind.Null)
            .Because("an instant beside no count of ours would name a measurement nobody took");

        await Assert.That(measured).Contains("15 now");
    }

    /// <summary>
    /// A measured zero is a measurement and says so; an unmeasured count never borrows its shape.
    /// </summary>
    /// <remarks>
    /// This is rule 4 at the point it would do the most damage. Eldertale was probed and nobody was
    /// there — a real fact, published. Midnight Sun answered and could not be counted, and renders
    /// as unknown; a "0" there would be our parser's limit printed as a fact about their game, on
    /// their own website, where we could not correct it.
    /// </remarks>
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
    /// <remarks>
    /// §7.5: archiving takes a game out of the listing and out of nothing else, so the badge still
    /// answers — but a live-count badge for a game that stopped answering in 2023 has no live count,
    /// and a stale one under a live label is the one thing it must not print.
    /// </remarks>
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
    /// <remarks>
    /// The name reaches the accessible title and nowhere else, escaped. An SVG is a document, so a
    /// game named <c>&lt;/text&gt;&lt;script&gt;</c> would otherwise be markup on every page that
    /// embeds the badge — a stored injection with a distribution mechanism.
    /// </remarks>
    [Test]
    public async Task AGamesOwnNameCannotBecomeMarkup()
    {
        var reading = new BadgeReading(BadgeState.Unknown, null, null, null);

        var svg = PlayerBadge.Svg(reading, "</text><script>alert(1)</script>");

        await Assert.That(svg).DoesNotContain("<script>");
        await Assert.That(svg).Contains("&lt;script&gt;");
    }

    /// <summary>A badge is cached longer than the API, and still well inside a count's own life.</summary>
    /// <remarks>
    /// Every reader of somebody's front page fetches this, so a minute is too little; a count is
    /// stale at two hours, so anything approaching that would serve a stale number under a live
    /// label.
    /// </remarks>
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

    /// <summary>
    /// A slug the game used to wear still serves its badge, permanently.
    /// </summary>
    /// <remarks>
    /// A badge is the likeliest thing here to outlive the URL it was copied from — pasted into a
    /// template once and left for years — so §5.7's forever-redirect matters more for this route
    /// than for any other.
    /// </remarks>
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

    /// <summary>
    /// The JSON publishes null and a named state, so a consumer cannot coerce silence to zero.
    /// </summary>
    /// <remarks>
    /// Both, because §5.4's middle case is the one every reimplementation loses. A consumer reading
    /// only <c>count</c> gets a null it has to handle; one reading <c>state</c> cannot mistake it.
    /// </remarks>
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

    /// <summary>
    /// A count and its age travel together or not at all.
    /// </summary>
    /// <remarks>
    /// A number we cannot label is a number this site may not publish, so a summary carrying a count
    /// with no provenance chip reads as unknown rather than as a bare figure. That combination
    /// should not occur — the query fills both from one row — and the badge does not rely on it not
    /// occurring.
    /// </remarks>
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
