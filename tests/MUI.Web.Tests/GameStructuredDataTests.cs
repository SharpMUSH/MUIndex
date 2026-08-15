using System.Text.Json;

using MUI.Catalog;
using MUI.Web.Components;

namespace MUI.Web.Tests;

/// <summary>
/// The graph a search engine reads, which is the one surface with no room for a chip.
/// </summary>
/// <remarks>
/// <para>
/// Structured data is where this project's central rule is easiest to break by accident, because
/// the vocabulary invites it: <c>userInteractionCount</c> is a bare integer with nowhere to put
/// "and we read it four minutes ago". A number published there without its timestamp is precisely
/// the unlabelled fact §10.1 called the one place the API could contradict the whole project — so
/// the rule enforced here is that no measured value enters the graph unless something beside it
/// carries when it was taken.
/// </para>
/// <para>
/// <b>And a game's name is a stranger's bytes.</b> It comes off MSSP or a connect screen on a host
/// we do not control, and it lands inside a <c>&lt;script&gt;</c> element, which is the one context
/// in HTML where ordinary attribute escaping does not save you.
/// </para>
/// </remarks>
public class GameStructuredDataTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 20, 0, 0, TimeSpan.Zero);

    private static readonly Uri Origin = new("https://muindex.test");

    [Test]
    public async Task AMeasuredCountIsPublishedWithTheInstantItWasTaken()
    {
        var page = Page(count: 15, source: FieldSource.Who, at: Now.AddMinutes(-4));

        var json = GameStructuredData.For(page, Origin);
        using var document = JsonDocument.Parse(json);

        var counter = Counter(document);

        await Assert.That(counter).IsNotNull();
        await Assert.That(counter!.Value.GetProperty("userInteractionCount").GetInt32()).IsEqualTo(15);
        await Assert.That(counter.Value.GetProperty("endTime").GetString())
            .IsEqualTo(Now.AddMinutes(-4).ToUniversalTime().ToString("o"));
    }

    [Test]
    public async Task ACountTheGameDeclaredDoesNotEnterTheGraphAtAll()
    {
        // There is no schema.org property for "they said so and we could not check", and the
        // nearest one reads as an observation. Rule 5 says our inability to verify a number may not
        // be republished as our having verified it — so the number stays on the page, where it is
        // labelled, and out of the graph, where it could not be.
        var page = Page(count: 9, source: FieldSource.Mssp, at: Now.AddMinutes(-9));

        var json = GameStructuredData.For(page, Origin);
        using var document = JsonDocument.Parse(json);

        await Assert.That(Counter(document)).IsNull();
        await Assert.That(json).DoesNotContain("\"9\"");
    }

    [Test]
    public async Task AnUncountableGameContributesNoNumber()
    {
        var page = Page(count: null, source: FieldSource.Who, at: Now.AddHours(-1));

        var json = GameStructuredData.For(page, Origin);

        await Assert.That(json).DoesNotContain("userInteractionCount");
        await Assert.That(json).DoesNotContain("interactionStatistic");
    }

    [Test]
    public async Task AMeasuredZeroIsPublished()
    {
        var page = Page(count: 0, source: FieldSource.Who, at: Now.AddHours(-3));

        var json = GameStructuredData.For(page, Origin);
        using var document = JsonDocument.Parse(json);

        await Assert.That(Counter(document)!.Value.GetProperty("userInteractionCount").GetInt32())
            .IsEqualTo(0);
    }

    [Test]
    public async Task AGameNameCannotCloseTheScriptElementItIsWrittenInto()
    {
        // A stranger's server names itself. If that name reaches the document unescaped, every
        // reader of every page for that game is running whatever it said.
        var page = Page(
            count: 1,
            source: FieldSource.Who,
            at: Now,
            name: "</script><script>alert(1)</script>");

        var json = GameStructuredData.For(page, Origin);

        await Assert.That(json).DoesNotContain("</script>");
        await Assert.That(json).DoesNotContain("<script>");
    }

    [Test]
    public async Task TheGraphNamesThePageItDescribes()
    {
        var page = Page(count: 15, source: FieldSource.Who, at: Now);

        var json = GameStructuredData.For(page, Origin);

        await Assert.That(json).Contains("https://muindex.test/g/m-u-s-h");
    }

    [Test]
    public async Task AnOriginWithAPathBaseKeepsIt()
    {
        // Uri's relative-reference rules would throw the path base away — "/g/x" against
        // https://h/mui resolves to https://h/g/x, which is a page that exists only where the site
        // is mounted at the root. Every URL in this graph is one a search engine will fetch.
        var page = Page(count: 15, source: FieldSource.Who, at: Now);

        var json = GameStructuredData.For(page, new Uri("https://muindex.test/mui"));

        await Assert.That(json).Contains("https://muindex.test/mui/g/m-u-s-h");
        await Assert.That(json).Contains("https://muindex.test/mui/games");
        await Assert.That(json).DoesNotContain("\"https://muindex.test/g/");
    }

    private static JsonElement? Counter(JsonDocument document)
    {
        foreach (var node in document.RootElement.GetProperty("@graph").EnumerateArray())
        {
            if (node.TryGetProperty("interactionStatistic", out var statistic))
            {
                return statistic;
            }
        }

        return null;
    }

    private static GamePage Page(
        int? count,
        FieldSource source,
        DateTimeOffset at,
        string name = "M*U*S*H")
    {
        var summary = new GameSummary(
            Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
            "m-u-s-h",
            name,
            "The PennMUSH development server.",
            LifecycleState.Active,
            IsClaimed: false,
            PlayersNow: count,
            Codebase: "PennMUSH 1.8.8p0",
            MeasuredProtocols: ["MSSP"],
            LastReachableAt: at,
            PlayersNowProvenance: count is { } n
                ? new ProvenanceChip(n.ToString(System.Globalization.CultureInfo.InvariantCulture), source, at, IsStale: false)
                : null,
            CodebaseProvenance: new ProvenanceChip("PennMUSH 1.8.8p0", FieldSource.Mssp, at, IsStale: false));

        return new GamePage(
            summary,
            Description: "A server for developing PennMUSH.",
            Endpoints: [new GameEndpointView("mush.example.org", 4201, "telnet", TlsMeasured: false, at, at, "active")],
            ConnectScreen: null,
            ConnectScreenSuppressed: false,
            ReachableFraction: null,
            LongestOutage: null,
            Capabilities: [],
            Activity: [],
            Declared: new Dictionary<string, ProvenanceChip>(),
            Changes: []);
    }
}
