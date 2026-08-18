using System.Reflection;
using System.Text.Json;

using MUI.Web.Api;

namespace MUI.Web.Tests.Api;

/// <summary>
/// The rules the read surface must keep whatever else changes: the vocabulary, the absent figure,
/// the absent vote, and the fact that nothing here writes.
/// </summary>
/// <remarks>Guards rather than behaviour tests — each asserts against a thing that would be added in good faith but that the design already ruled out.</remarks>
public class ApiSurfaceGuardTests
{
    private static readonly string[] AllRoutes =
    [
        ApiRoutes.Base,
        ApiRoutes.Games,
        ApiRoutes.Games + "/m-u-s-h",
        ApiRoutes.Games + "/m-u-s-h" + ApiRoutes.PresenceSuffix,
        ApiRoutes.Games + "/m-u-s-h" + ApiRoutes.AvailabilitySuffix,
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
        // Reachable, never uptime (spec §5.8) — binds field names, enum names, and published copy.
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
        // Shares survive measurement bias as ratios; an absolute count doesn't (spec §15.7).
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

        await using var pinned = await ApiHost.StartAsync();
        var listing = await Json.ElementAsync(await pinned.Client.GetAsync(ApiRoutes.Games));
        await Assert.That(listing.GetProperty("total").GetInt32())
            .IsEqualTo(listing.GetProperty("games").GetArrayLength());
    }

    [Test]
    public async Task ThereIsNoVoteStarOrRatingAnywhere()
    {
        // Rankings, when they come, are computed from measured data — no user-supplied signal reaches one.
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
        // Deferred deliberately (spec §14) — a callback registration is a write endpoint in disguise.
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

                // 405, not 404: the route exists and refuses the verb.
                await Assert.That((int)response.StatusCode).IsEqualTo(405);
            }
        }
    }

    [Test]
    public async Task TheIndexTellsAConsumerTheRulesWithoutMakingThemReadUs()
    {
        // The two rules most likely to be gotten wrong — null is not zero, id is the durable key —
        // are stated on the front door for a MUD-client author who never reads our docs.
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
