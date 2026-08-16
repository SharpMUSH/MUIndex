using System.Buffers.Binary;
using System.Net;

using MUI.Crawl;
using MUI.Discovery;
using MUI.Web.Icons;

namespace MUI.Web.Tests;

/// <summary>
/// Fetching an icon from a URL somebody else chose (spec §7.2, §11).
/// </summary>
/// <remarks>
/// <para>
/// Almost every test here is a refusal, which is the right shape for this component: it exists to
/// take a URL an owner typed and reach for it from our network, and the interesting question is what
/// it declines to do. The one positive case is the last.
/// </para>
/// <para>
/// No live network anywhere — the handler is stubbed and the resolver is stubbed — so a test that
/// passes because something on the internet was reachable is not possible here.
/// </para>
/// </remarks>
public class IconFetcherTests
{
    private static readonly Guid Game = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000c0000");

    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// §7.2, in a new place. The gate is on the resolved address and not on the name.
    /// </summary>
    /// <remarks>
    /// A name check would pass this: <c>icons.example.org</c> is a perfectly ordinary hostname, and
    /// publishing an A record pointing at the metadata service costs an attacker nothing. The socket
    /// is never opened, which is what the second assertion is for — a refusal that still made the
    /// request would be no refusal at all.
    /// </remarks>
    [Test]
    [Arguments("169.254.169.254")]
    [Arguments("127.0.0.1")]
    [Arguments("10.0.0.5")]
    public async Task AnIconUrlResolvingToAnAddressWeWillNotDialIsNotFetched(string address)
    {
        var http = new StubHandler();
        var fetcher = Fetcher(http, resolving: address);

        var icon = await fetcher.FetchAsync(Game, "https://icons.example.org/logo.png");

        await Assert.That(icon).IsNull();
        await Assert.That(http.Requests).IsEmpty();
    }

    /// <summary>
    /// A mixed answer refuses the whole target rather than proceeding on the address we liked.
    /// </summary>
    [Test]
    public async Task AMixedDnsAnswerRefusesTheWholeTarget()
    {
        var http = new StubHandler();
        var fetcher = Fetcher(http, resolving: ["203.0.113.10", "10.0.0.5"]);

        await Assert.That(await fetcher.FetchAsync(Game, "https://icons.example.org/logo.png")).IsNull();
        await Assert.That(http.Requests).IsEmpty();
    }

    /// <summary>A redirect is a second address the gate never ruled on, so it is not followed.</summary>
    [Test]
    public async Task ARedirectIsNotFollowed()
    {
        var http = new StubHandler().Responds(
            new HttpResponseMessage(HttpStatusCode.Found)
            {
                Headers = { Location = new Uri("http://10.0.0.5/logo.png") },
            });

        await Assert.That(await Fetcher(http).FetchAsync(Game, "https://icons.example.org/logo.png"))
            .IsNull();

        // One request, to the address that was ruled on, and no second one to the address that was not.
        await Assert.That(http.Requests.Count).IsEqualTo(1);
    }

    /// <summary>
    /// An oversized body is refused rather than truncated, and the ceiling is on the read.
    /// </summary>
    /// <remarks>
    /// <c>Content-Length</c> is what a far end <em>says</em> it will send, and this is a far end
    /// somebody else chose. The body here declares nothing and streams past the limit, which is the
    /// case a length check would miss entirely.
    /// </remarks>
    [Test]
    public async Task AnOversizedBodyIsRefusedRatherThanTruncated()
    {
        var http = new StubHandler().Responds(Ok(new byte[IconFetcher.MaxBytes + 1]));

        await Assert.That(await Fetcher(http).FetchAsync(Game, "https://icons.example.org/logo.png"))
            .IsNull();
    }

    /// <summary>An image at exactly the ceiling is kept: the limit is a ceiling, not a margin.</summary>
    [Test]
    public async Task AnImageOfExactlyTheCeilingIsKept()
    {
        var bytes = new byte[IconFetcher.MaxBytes];
        Png(64, 64).CopyTo(bytes.AsSpan());

        var http = new StubHandler().Responds(Ok(bytes));

        await Assert.That(await Fetcher(http).FetchAsync(Game, "https://icons.example.org/logo.png"))
            .IsNotNull();
    }

    /// <summary>
    /// An SVG is refused however it is labelled, and so is anything else we cannot read.
    /// </summary>
    /// <remarks>
    /// It is a document that can carry script; served from our own origin it is a cross-site
    /// scripting hole with an image tag in front of it. The response here claims <c>image/svg+xml</c>
    /// and it makes no difference, because the claim is not what is consulted.
    /// </remarks>
    [Test]
    public async Task AnSvgIsRefusedHoweverItIsLabelled()
    {
        var http = new StubHandler().Responds(
            Ok("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray(), "image/svg+xml"));

        await Assert.That(await Fetcher(http).FetchAsync(Game, "https://icons.example.org/logo.svg"))
            .IsNull();
    }

    /// <summary>A picture too large to be an icon is refused rather than rescaled.</summary>
    /// <remarks>
    /// Rescaling would mean serving a different image from the one its owner published, under their
    /// name, with nothing on the page to say we had changed it.
    /// </remarks>
    [Test]
    public async Task AnImageLargerThanAnIconIsRefusedRatherThanRescaled()
    {
        var http = new StubHandler().Responds(Ok(Png(IconFetcher.MaxDimension + 1, 32)));

        await Assert.That(await Fetcher(http).FetchAsync(Game, "https://icons.example.org/poster.png"))
            .IsNull();
    }

    /// <summary>
    /// A far end saying nothing has changed is an ordinary answer, and we keep what we hold.
    /// </summary>
    [Test]
    public async Task NotModifiedLeavesWhatWeAlreadyHold()
    {
        var http = new StubHandler().Responds(new HttpResponseMessage(HttpStatusCode.NotModified));

        var icon = await Fetcher(http).FetchAsync(
            Game, "https://icons.example.org/logo.png", etag: "\"abc\"");

        await Assert.That(icon).IsNull();
        await Assert.That(http.Requests[0].Headers.TryGetValues("If-None-Match", out _)).IsTrue();
    }

    /// <summary>
    /// A URL that is not something we could dial is declined before anything is resolved.
    /// </summary>
    [Test]
    [Arguments("javascript:alert(1)")]
    [Arguments("file:///etc/passwd")]
    [Arguments("data:image/png;base64,iVBOR")]
    [Arguments("not a url at all")]
    public async Task AUrlWeCouldNotDialIsDeclinedOutright(string url)
    {
        var http = new StubHandler();

        await Assert.That(await Fetcher(http).FetchAsync(Game, url)).IsNull();
        await Assert.That(http.Requests).IsEmpty();
    }

    /// <summary>
    /// Nothing about a failed fetch is a fact about the game, so nothing is returned to store.
    /// </summary>
    /// <remarks>
    /// Rule 5, applied to a picture. A server that is down, slow or angry is our afternoon and not
    /// their game — there is no row, no marker and no attempt counter, and the page renders no
    /// element rather than a broken image.
    /// </remarks>
    [Test]
    [Arguments(HttpStatusCode.InternalServerError)]
    [Arguments(HttpStatusCode.NotFound)]
    [Arguments(HttpStatusCode.Forbidden)]
    public async Task AFailedFetchYieldsNothingToRecord(HttpStatusCode status)
    {
        var http = new StubHandler().Responds(new HttpResponseMessage(status));

        await Assert.That(await Fetcher(http).FetchAsync(Game, "https://icons.example.org/logo.png"))
            .IsNull();
    }

    /// <summary>A network that throws is the same answer, and never an exception to the caller.</summary>
    [Test]
    public async Task AThrowingNetworkIsTheSameAnswerAsAMissingIcon()
    {
        var http = new StubHandler().Throwing(new HttpRequestException("no route to host"));

        await Assert.That(await Fetcher(http).FetchAsync(Game, "https://icons.example.org/logo.png"))
            .IsNull();
    }

    /// <summary>And the one that works: the bytes, and the type we determined from them.</summary>
    [Test]
    public async Task AGoodIconComesBackWithTheTypeWeReadRatherThanTheOneClaimed()
    {
        // The response claims PNG and the bytes are a GIF. A site echoing the claim would be serving
        // a content type it had never checked from its own origin.
        var http = new StubHandler().Responds(Ok(Gif(48, 24), "image/png"));

        var icon = await Fetcher(http).FetchAsync(Game, "https://icons.example.org/logo.png");

        await Assert.That(icon!.ContentType).IsEqualTo("image/gif");
        await Assert.That(icon.Width).IsEqualTo(48);
        await Assert.That(icon.Height).IsEqualTo(24);
        await Assert.That(icon.SourceUrl).IsEqualTo("https://icons.example.org/logo.png");
        await Assert.That(icon.FetchedAt).IsEqualTo(Now);
    }

    private static IconFetcher Fetcher(StubHandler http, params string[] resolving)
    {
        var addresses = resolving.Length == 0 ? ["203.0.113.10"] : resolving;
        var resolver = new FakeResolver(addresses);

        return new IconFetcher(
            new HttpClient(http) { BaseAddress = null },
            new HostScopeGuard(resolver),
            new Frozen(Now));
    }

    private static HttpResponseMessage Ok(byte[] body, string contentType = "image/png")
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body),
        };

        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        return response;
    }

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

    private static byte[] Gif(ushort width, ushort height)
    {
        var bytes = new byte[10];

        "GIF89a"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), width);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), height);

        return bytes;
    }

    /// <summary>A handler that answers from a script and records what it was asked.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private HttpResponseMessage? _response;

        private Exception? _throws;

        public List<HttpRequestMessage> Requests { get; } = [];

        public StubHandler Responds(HttpResponseMessage response)
        {
            _response = response;

            return this;
        }

        public StubHandler Throwing(Exception error)
        {
            _throws = error;

            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            return _throws is not null
                ? Task.FromException<HttpResponseMessage>(_throws)
                : Task.FromResult(_response ?? new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    /// <summary>A resolver with a fixed answer, so no test here performs a live lookup.</summary>
    private sealed class FakeResolver(IReadOnlyList<string> addresses) : IHostResolver
    {
        public Task<IReadOnlyList<IPAddress>> ResolveAsync(
            string host, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IPAddress>>([.. addresses.Select(IPAddress.Parse)]);
    }

    private sealed class Frozen(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }
}
