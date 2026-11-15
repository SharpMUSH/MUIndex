using System.Buffers.Binary;
using System.Net;

using Microsoft.Extensions.Logging.Abstractions;

using MUI.Catalog.Persistence;
using MUI.Crawl;
using MUI.Discovery;
using MUI.Web.Icons;

namespace MUI.Web.Tests;

/// <summary>
/// The loop that fetches the pictures a connect screen names by address: the icon's gate and the
/// icon's back-off, with room for a picture larger than an icon.
/// </summary>
public class ScreenPictureRefresherTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Game = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000d0000");

    [Test]
    public async Task AFetchedPictureIsStoredAndAskedAgainAWeekLater()
    {
        // Wider than an icon may be, which is the point of the picture's own ceilings.
        var store = new RecordingStore(new ScreenPictureDue(Game, 2, "https://ravenmoor.example/castle.png", 0));
        var refresher = Refresher(store, new Answering(HttpStatusCode.OK, Png(960, 384)), "203.0.113.10");

        await Assert.That(await refresher.PassAsync(CancellationToken.None)).IsEqualTo(1);

        var stored = await Assert.That(store.Stored).HasSingleItem();
        await Assert.That(stored.Ordinal).IsEqualTo(2);
        await Assert.That(stored.ContentType).IsEqualTo("image/png");
        await Assert.That(stored.Next).IsEqualTo(Now + IconRefresher.Stale);
        await Assert.That(store.Failed).IsEmpty();
    }

    /// <summary>
    /// An address that resolves somewhere we do not dial is never asked, and what is written is that
    /// we tried, never anything about the game.
    /// </summary>
    [Test]
    public async Task ARefusedAddressIsNotDialledAndIsWrittenDownWithTheIconsBackOff()
    {
        var store = new RecordingStore(new ScreenPictureDue(Game, 0, "https://ravenmoor.example/castle.png", 3));
        var http = new Answering(HttpStatusCode.OK, Png(64, 64));
        var refresher = Refresher(store, http, "169.254.169.254");

        await Assert.That(await refresher.PassAsync(CancellationToken.None)).IsEqualTo(0);

        await Assert.That(http.Asked).IsEqualTo(0);
        await Assert.That(store.Stored).IsEmpty();
        var failed = await Assert.That(store.Failed).HasSingleItem();
        await Assert.That(failed).IsEqualTo(Now + IconRefresher.Backoff(4));
    }

    private static ScreenPictureRefresher Refresher(RecordingStore store, Answering http, string resolvesTo) =>
        new(
            store,
            new IconFetcher(new HttpClient(http), new HostScopeGuard(new Resolving(resolvesTo)), new Frozen(Now)),
            new Frozen(Now),
            NullLogger<ScreenPictureRefresher>.Instance);

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

    private sealed class RecordingStore(params ScreenPictureDue[] due) : IScreenPictureStore
    {
        public List<(int Ordinal, string ContentType, DateTimeOffset Next)> Stored { get; } = [];

        public List<DateTimeOffset> Failed { get; } = [];

        public Task<IReadOnlyList<ScreenPictureDue>> DueAsync(
            int limit, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScreenPictureDue>>(due);

        public Task StoreFetchedAsync(
            Guid gameId, int ordinal, string sourceUrl, string contentType, byte[] bytes,
            DateTimeOffset at, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken = default)
        {
            Stored.Add((ordinal, contentType, nextAttemptAt));
            return Task.CompletedTask;
        }

        public Task RecordFailureAsync(
            Guid gameId, int ordinal, string sourceUrl, DateTimeOffset at, DateTimeOffset nextAttemptAt,
            CancellationToken cancellationToken = default)
        {
            Failed.Add(nextAttemptAt);
            return Task.CompletedTask;
        }

        public Task ReplaceAsync(
            Guid gameId, IReadOnlyList<ScreenPictureCapture> pictures, DateTimeOffset capturedAt,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<ScreenPicturePlacement>> ForGameAsync(
            Guid gameId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ScreenPictureFile?> FileAsync(
            Guid gameId, int ordinal, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class Answering(HttpStatusCode status, byte[] body) : HttpMessageHandler
    {
        public int Asked { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
        }
    }

    private sealed class Resolving(string address) : IHostResolver
    {
        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse(address)]);
    }

    private sealed class Frozen(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }
}
