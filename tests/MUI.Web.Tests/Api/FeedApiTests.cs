using System.Text.Json;
using System.Xml.Linq;

using MUI.Catalog;
using MUI.Web.Api;
using MUI.Web.Fixtures;

namespace MUI.Web.Tests.Api;

/// <summary>
/// <c>/api/feeds</c> and the three RSS registers — the differentiator, and the whole of v1's
/// notification surface (spec §10, §14).
/// </summary>
public class FeedApiTests
{
    [Test]
    public async Task TheThreeRegistersAreThreeLists()
    {
        await using var host = await ApiHost.StartAsync();

        var feeds = await Json.ElementAsync(await host.Client.GetAsync(ApiRoutes.Feeds));

        await Assert.That(feeds.GetProperty("newlyDiscovered").GetArrayLength()).IsGreaterThan(0);
        await Assert.That(feeds.GetProperty("wentDark").GetArrayLength()).IsGreaterThan(0);
        await Assert.That(feeds.GetProperty("cameBack").GetArrayLength()).IsGreaterThan(0);

        // Each register says where its RSS is, so a reader who found the JSON never has to guess.
        var rss = feeds.GetProperty("rss");
        await Assert.That(rss.GetProperty("went-dark").GetString()).IsEqualTo(ApiRoutes.WentDarkRss);
    }

    [Test]
    public async Task AFeedEntryCarriesTheStableIdAndNotOnlyTheMutableSlug()
    {
        await using var host = await ApiHost.StartAsync();

        var feeds = await Json.ElementAsync(await host.Client.GetAsync(ApiRoutes.Feeds));
        var entry = feeds.GetProperty("cameBack")[0];

        var expected = (await new FixtureGameQueries().ListAsync(
                new GameFilter { IncludeArchived = true }))
            .Single(g => g.Slug == entry.GetProperty("slug").GetString());

        await Assert.That(entry.GetProperty("id").GetGuid()).IsEqualTo(expected.Id);
        await Assert.That(entry.GetProperty("apiUrl").GetString())
            .IsEqualTo(ApiRoutes.Game(expected.Id));
    }

    [Test]
    public async Task TheFeedKnowsEachGamesIdWithoutReadingTheWholeListingToFindIt()
    {
        // §10.1's second gap. FeedEntry carried a slug and no id, so answering /api/feeds meant
        // reading the entire catalogue to join identifiers onto ten rows. The id belongs to the
        // query that already had the game in hand — and a listing this endpoint never asks for
        // cannot be the thing it is answering from.
        var expected = (await new FixtureGameQueries().ListAsync(new GameFilter { IncludeArchived = true }))
            .ToDictionary(g => g.Slug, g => g.Id, StringComparer.Ordinal);

        await using var host = await ApiHost.StartAsync(
            queries: new ListingRefusingQueries(new FixtureGameQueries()));

        var feeds = await Json.ElementAsync(await host.Client.GetAsync(ApiRoutes.Feeds));

        foreach (var register in new[] { "newlyDiscovered", "wentDark", "cameBack" })
        {
            foreach (var entry in feeds.GetProperty(register).EnumerateArray())
            {
                var slug = entry.GetProperty("slug").GetString()!;
                await Assert.That(entry.GetProperty("id").GetGuid()).IsEqualTo(expected[slug]);
                await Assert.That(entry.GetProperty("apiUrl").GetString())
                    .IsEqualTo(ApiRoutes.Game(expected[slug]));
            }
        }
    }

    [Test]
    public async Task EachRegisterIsAlsoRssAndItsItemGuidsAreStable()
    {
        await using var host = await ApiHost.StartAsync();

        var response = await host.Client.GetAsync(ApiRoutes.WentDarkRss);
        await Assert.That(response.Content.Headers.ContentType!.MediaType)
            .IsEqualTo("application/rss+xml");

        var xml = XDocument.Parse(await response.Content.ReadAsStringAsync());
        var channel = xml.Root!.Element("channel")!;

        await Assert.That(channel.Element("title")!.Value).Contains("went dark");
        var item = channel.Elements("item").First();
        await Assert.That(item.Element("guid")!.Attribute("isPermaLink")!.Value).IsEqualTo("false");

        // Same event, same GUID, or every reader re-notifies on every poll.
        var again = XDocument.Parse(
            await (await host.Client.GetAsync(ApiRoutes.WentDarkRss)).Content.ReadAsStringAsync());
        await Assert.That(again.Descendants("guid").First().Value)
            .IsEqualTo(item.Element("guid")!.Value);

        // And it names the event rather than only the game, so a game that goes dark twice is two
        // items rather than one that a reader has already dismissed.
        await Assert.That(item.Element("guid")!.Value).Contains("went-dark");
        await Assert.That(item.Element("guid")!.Value).Contains("verdigris");
    }

    [Test]
    public async Task TheRssIsETaggedLikeEverythingElse()
    {
        await using var host = await ApiHost.StartAsync();

        var first = await host.Client.GetAsync(ApiRoutes.DiscoveredRss);
        await Assert.That(first.Headers.ETag).IsNotNull();

        using var conditional = new HttpRequestMessage(HttpMethod.Get, ApiRoutes.DiscoveredRss);
        conditional.Headers.TryAddWithoutValidation("If-None-Match", first.Headers.ETag!.ToString());
        var second = await host.Client.SendAsync(conditional);

        await Assert.That((int)second.StatusCode).IsEqualTo(304);
    }

    [Test]
    public async Task AllThreeRegistersAreServedAndNoneOfThemIsACallback()
    {
        // RSS is the whole notification story in v1. Webhooks are deferred (spec §14) and there is
        // deliberately nothing here that takes a URL to call back.
        await using var host = await ApiHost.StartAsync();

        foreach (var path in new[]
                 {
                     ApiRoutes.DiscoveredRss, ApiRoutes.WentDarkRss, ApiRoutes.CameBackRss,
                 })
        {
            var response = await host.Client.GetAsync(path);
            await Assert.That((int)response.StatusCode).IsEqualTo(200);
        }

        var index = await Json.ElementAsync(await host.Client.GetAsync(ApiRoutes.Base));
        var text = string.Join(' ', Json.StringValues(index)).ToLowerInvariant();
        await Assert.That(text).DoesNotContain("subscribe to");
        await Assert.That(index.GetProperty("routes").EnumerateArray()
                .All(r => r.GetProperty("method").GetString() == "GET"))
            .IsTrue();
    }

    [Test]
    public async Task NothingAboutAPlayerLeavesTheProcess()
    {
        // WHO is parsed in memory and player names are never persisted (spec §11). Counts and shares
        // are the only thing presence produces, so nothing name-shaped can reach a payload.
        await using var host = await ApiHost.StartAsync();

        var game = await Json.ElementAsync(
            await host.Client.GetAsync($"{ApiRoutes.Games}/m-u-s-h"));

        var names = Json.PropertyNames(game).Select(n => n.ToLowerInvariant()).ToList();
        foreach (var forbidden in new[] { "players", "playernames", "who", "accounts", "characters" })
        {
            await Assert.That(names).DoesNotContain(forbidden);
        }

        await Assert.That(game.GetProperty("presence").GetProperty("cells")[0].ValueKind)
            .IsEqualTo(JsonValueKind.Object);
    }
}
