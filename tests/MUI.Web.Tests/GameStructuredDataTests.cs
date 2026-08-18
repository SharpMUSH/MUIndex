using System.Text.Json;

using MUI.Catalog;
using MUI.Web.Components;

namespace MUI.Web.Tests;

/// <summary>
/// The graph a search engine reads, which is the one surface with no room for a chip.
/// </summary>
/// <remarks>
/// <c>userInteractionCount</c> is a bare integer with nowhere for "and we read it four minutes ago",
/// so no measured value may enter the graph unless something beside it carries when it was taken.
/// A game's name is a stranger's bytes (from MSSP or a connect screen) landing inside a
/// <c>&lt;script&gt;</c> element — the one HTML context ordinary attribute escaping doesn't save you from.
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
        // No schema.org property for "they said so and we couldn't check" exists; rule 5 forbids
        // republishing an unverified claim as a verified one, so it stays on the page and out of the graph.
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
        // If a game's own self-reported name reaches the document unescaped, every reader runs whatever it said.
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
        // Uri's relative-reference rules would throw the path base away — "/g/x" against https://h/mui
        // resolves to https://h/g/x, a page that only exists when mounted at the root.
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
