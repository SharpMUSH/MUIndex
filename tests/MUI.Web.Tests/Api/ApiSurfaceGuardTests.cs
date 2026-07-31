using System.Reflection;
using System.Text.Json;

using MUI.Web.Api;

namespace MUI.Web.Tests.Api;

/// <summary>
/// The rules the read surface must keep whatever else changes: the vocabulary, the absent figure,
/// the absent vote, and the fact that nothing here writes.
/// </summary>
/// <remarks>
/// Guards rather than behaviour. Every one of these is a thing somebody would add in good faith —
/// a percentage called the obvious word, a headline "players online across the hobby", a
/// <c>?sort=rating</c> — and each is a decision the design already took and wrote down.
/// </remarks>
public class ApiSurfaceGuardTests
{
    private static readonly string[] AllRoutes =
    [
        ApiRoutes.Base,
        ApiRoutes.Games,
        ApiRoutes.Games + "/m-u-s-h",
        ApiRoutes.Feeds,
        ApiRoutes.Dump,
    ];

    private static IEnumerable<MemberInfo> ApiMembers() =>
        typeof(MuiApi).Assembly.GetTypes()
            .Where(t => t.Namespace == "MUI.Web.Api" && t.IsPublic)
            .SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly));

    [Test]
    public async Task NothingOnThisSurfaceSaysTheWordWeDoNotUse()
    {
        // Reachable, never the other one (spec §5.8). We measured a socket from one vantage point at
        // intervals; the word claims we measured whether the game was running. The rule binds field
        // names, enum names and published copy, so this reads all three.
        const string Forbidden = "uptime";

        var names = ApiMembers().Select(m => m.Name).ToList();
        await Assert.That(names.Count).IsGreaterThan(0);
        foreach (var name in names)
        {
            await Assert.That(name.Contains(Forbidden, StringComparison.OrdinalIgnoreCase)).IsFalse();
        }

        await using var host = await ApiHost.StartAsync();
        foreach (var route in AllRoutes)
        {
            var body = await (await host.Client.GetAsync(route)).Content.ReadAsStringAsync();
            await Assert.That(body.Contains(Forbidden, StringComparison.OrdinalIgnoreCase)).IsFalse();
        }
    }

    [Test]
    public async Task NoAbsolutePopulationFigureIsPublishedAnywhere()
    {
        // Per-codebase and per-protocol shares survive the unclaimed and unreachable biases because
        // they are ratios over the same measured set. An absolute count does not, and would be
        // quoted for years (spec §15.7).
        foreach (var member in ApiMembers())
        {
            var name = member.Name.ToLowerInvariant();
            await Assert.That(name).DoesNotContain("population");
            await Assert.That(name).DoesNotContain("totalplayers");
            await Assert.That(name).DoesNotContain("playertotal");
        }

        await using var host = await ApiHost.StartAsync();

        foreach (var route in AllRoutes)
        {
            var element = await Json.ElementAsync(await host.Client.GetAsync(route));
            foreach (var name in Json.PropertyNames(element).Select(n => n.ToLowerInvariant()))
            {
                await Assert.That(name).DoesNotContain("population");
                await Assert.That(name).DoesNotContain("totalplayers");
            }
        }

        // The one field called "total" is a count of games, and this is what pins it there.
        await using var pinned = await ApiHost.StartAsync();
        var listing = await Json.ElementAsync(await pinned.Client.GetAsync(ApiRoutes.Games));
        await Assert.That(listing.GetProperty("total").GetInt32())
            .IsEqualTo(listing.GetProperty("games").GetArrayLength());
    }

    [Test]
    public async Task ThereIsNoVoteStarOrRatingAnywhere()
    {
        // The thing that reduced Top Mud Sites to a link graveyard. Rankings, when they come, are
        // computed from measured data; no user-supplied signal has a route to reach one.
        foreach (var member in ApiMembers())
        {
            var name = member.Name.ToLowerInvariant();
            foreach (var forbidden in new[] { "vote", "rating", "star", "recommend", "review" })
            {
                await Assert.That(name).DoesNotContain(forbidden);
            }
        }

        await using var host = await ApiHost.StartAsync();
        foreach (var route in AllRoutes)
        {
            var element = await Json.ElementAsync(await host.Client.GetAsync(route));
            foreach (var name in Json.PropertyNames(element).Select(n => n.ToLowerInvariant()))
            {
                foreach (var forbidden in new[] { "vote", "rating", "recommend", "review" })
                {
                    await Assert.That(name).DoesNotContain(forbidden);
                }
            }
        }
    }

    [Test]
    public async Task ThereIsNoWebhookSurface()
    {
        // Deferred deliberately (spec §14). RSS is the whole of v1's notification story, and a
        // callback registration is a write endpoint wearing a different coat.
        foreach (var member in ApiMembers())
        {
            await Assert.That(member.Name.ToLowerInvariant()).DoesNotContain("webhook");
        }

        await using var host = await ApiHost.StartAsync();
        foreach (var path in new[] { "/api/webhooks", "/api/hooks", "/api/subscriptions" })
        {
            var response = await host.Client.PostAsync(path, new StringContent("{}"));
            await Assert.That((int)response.StatusCode).IsGreaterThanOrEqualTo(400);
        }
    }

    [Test]
    public async Task TheApiWritesNothing()
    {
        await using var host = await ApiHost.StartAsync();

        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete })
        {
            foreach (var route in new[] { ApiRoutes.Games, ApiRoutes.Games + "/m-u-s-h" })
            {
                using var request = new HttpRequestMessage(method, route);
                var response = await host.Client.SendAsync(request);

                // 405, and specifically not a 404: the route exists and refuses the verb, which is
                // a different and more useful answer than pretending it is not there.
                await Assert.That((int)response.StatusCode).IsEqualTo(405);
            }
        }
    }

    [Test]
    public async Task TheIndexTellsAConsumerTheRulesWithoutMakingThemReadUs()
    {
        // Someone writing a MUD client is a first-class consumer, and the two rules most likely to
        // be got wrong — null is not zero, the id is the durable key — are stated on the front door.
        await using var host = await ApiHost.StartAsync();

        var index = await Json.ElementAsync(await host.Client.GetAsync(ApiRoutes.Base));
        var notes = string.Join(' ', index.GetProperty("notes").EnumerateArray()
            .Select(n => n.GetString())).ToLowerInvariant();

        await Assert.That(notes).Contains("never a zero");
        await Assert.That(notes).Contains("immutable guid");
        await Assert.That(notes).Contains("if-none-match");
        await Assert.That(index.GetProperty("routes").GetArrayLength()).IsGreaterThan(4);
        await Assert.That(index.GetProperty("licence").GetProperty("id").ValueKind)
            .IsEqualTo(JsonValueKind.String);
    }
}
