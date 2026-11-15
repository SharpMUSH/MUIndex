using System.Buffers.Binary;
using System.Net;

using Microsoft.Extensions.DependencyInjection;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Web.Data;
using MUI.Web.Fixtures;
using MUI.Web.Icons;

namespace MUI.Web.Tests;

/// <summary>
/// A connect screen's pictures, served from this origin and drawn on the game's page.
/// </summary>
/// <remarks>
/// Composed as a database-backed site, since that is the only composition that maps the route, with
/// the catalogue and the picture store stood in for so no Postgres is needed.
/// </remarks>
public class ScreenPictureEndpointTests
{
    private const string Slug = "gaslight-row";

    private const string Unreachable = "Host=127.0.0.1;Port=1;Database=mui;Username=mui;Timeout=2";

    [Test]
    public async Task APictureIsServedWithTheTypeItsBytesSayAndNosniff()
    {
        await using var site = await Start(suppressed: false);

        using var response = await site.Client.GetAsync($"/g/{Slug}/screen/0?v=abc");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("image/png")
            .Because("the store said image/gif, and the bytes are a PNG");
        await Assert.That(response.Headers.GetValues("X-Content-Type-Options")).Contains("nosniff");
    }

    [Test]
    public async Task APictureOfAScreenTheOwnerHidIsNotServed()
    {
        await using var site = await Start(suppressed: true);

        using var response = await site.Client.GetAsync($"/g/{Slug}/screen/0");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task APictureWeDoNotHoldIsNotFound()
    {
        await using var site = await Start(suppressed: false);

        using var response = await site.Client.GetAsync($"/g/{Slug}/screen/5");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    /// <summary>The game's page draws the picture over its screen, at the address served above.</summary>
    [Test]
    public async Task TheGamesPageDrawsThePictureOverItsScreen()
    {
        var html = await Page(suppressed: false);

        await Assert.That(html).Contains($"src=\"/g/{Slug}/screen/0?v=abc123\"");
        await Assert.That(html).Contains("--row:1;--col:2;--cols:10;--rows:3");
    }

    [Test]
    public async Task AHiddenScreensPageDrawsNoPicture()
    {
        var html = await Page(suppressed: true);

        await Assert.That(html).DoesNotContain("screen-picture");
    }

    /// <summary>The page over the fixture, as every page test renders it, with the picture store added.</summary>
    private static Task<string> Page(bool suppressed) =>
        Render.PageAsync<MUI.Web.Components.Pages.Game>(
            new() { ["Slug"] = Slug },
            string.Empty,
            queries: new Hiding(suppressed),
            services: services => services.AddSingleton<IScreenPictureStore>(new OnePicture()));

    private static Task<SiteHost> Start(bool suppressed) =>
        SiteHost.StartAsync(
            settings: new Dictionary<string, string?> { [CrawlerSettings.EnabledConfigurationKey] = "false" },
            connectionString: Unreachable,
            overrides: services =>
            {
                services.AddSingleton<IGameQueries>(new Hiding(suppressed));
                services.AddSingleton<IScreenPictureStore>(new OnePicture());
            });

    private static byte[] Png(uint width, uint height)
    {
        var bytes = new byte[24];

        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(bytes);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), height);

        return bytes;
    }

    /// <summary>One picture at ordinal 0, filed under the wrong type so the test can see it re-read.</summary>
    private sealed class OnePicture : IScreenPictureStore
    {
        public Task<ScreenPictureFile?> FileAsync(Guid gameId, int ordinal, CancellationToken cancellationToken = default) =>
            Task.FromResult(ordinal == 0
                ? new ScreenPictureFile("image/gif", Png(48, 48), DateTimeOffset.UnixEpoch)
                : null);

        public Task<IReadOnlyList<ScreenPicturePlacement>> ForGameAsync(Guid gameId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScreenPicturePlacement>>([new ScreenPicturePlacement(0, 1, 2, 10, 3, "abc123")]);

        public Task<IReadOnlyList<ScreenPictureDue>> DueAsync(int limit, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScreenPictureDue>>([]);

        public Task ReplaceAsync(Guid gameId, IReadOnlyList<ScreenPictureCapture> pictures, DateTimeOffset capturedAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StoreFetchedAsync(Guid gameId, int ordinal, string sourceUrl, string contentType, byte[] bytes, DateTimeOffset at, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RecordFailureAsync(Guid gameId, int ordinal, string sourceUrl, DateTimeOffset at, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    /// <summary>The fixture's game page, with the owner's choice about its screen set by the test.</summary>
    private sealed class Hiding(bool suppressed) : IGameQueries
    {
        private readonly FixtureGameQueries _fixture = new();

        public async Task<GamePage?> FindAsync(string slug, CancellationToken cancellationToken = default) =>
            await _fixture.FindAsync(slug, cancellationToken) is { } page
                ? page with { ConnectScreenSuppressed = suppressed }
                : null;

        public Task<GameListing> SearchAsync(GameFilter filter, CancellationToken cancellationToken = default) =>
            _fixture.SearchAsync(filter, cancellationToken);
        public Task<IReadOnlyList<GameSummary>> ListAsync(GameFilter filter, CancellationToken cancellationToken = default) =>
            _fixture.ListAsync(filter, cancellationToken);
        public Task<GamePage?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
            _fixture.FindAsync(id, cancellationToken);
        public Task<GameSummary?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            _fixture.FindByIdAsync(id, cancellationToken);
        public Task<LivenessFeeds> FeedsAsync(CancellationToken cancellationToken = default) =>
            _fixture.FeedsAsync(cancellationToken);
        public Task<EcosystemDashboard> EcosystemAsync(CancellationToken cancellationToken = default) =>
            _fixture.EcosystemAsync(cancellationToken);
        public Task<Rankings> RankingsAsync(RankingSpan span = RankingSpan.Week, CancellationToken cancellationToken = default) =>
            _fixture.RankingsAsync(span, cancellationToken);
        public Task<IReadOnlyList<RecentGameChange>> RecentFieldChangesAsync(int limit, int perGameLimit = 3, CancellationToken cancellationToken = default) =>
            _fixture.RecentFieldChangesAsync(limit, perGameLimit, cancellationToken);
    }
}
