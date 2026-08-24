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
/// Almost every test here is a refusal, which is the right shape for this component — it exists to
/// take a URL an owner typed and reach for it from our network. No live network anywhere: both the
/// handler and the resolver are stubbed.
/// </remarks>
public class IconFetcherTests
{
    private static readonly Guid Game = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000c0000");

    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// §7.2, in a new place. The gate is on the resolved address and not on the name.
    /// </summary>
    /// <remarks>
    /// A name check would pass this: <c>icons.example.org</c> is an ordinary hostname, and pointing
    /// its A record at the metadata service costs an attacker nothing. The socket is never opened —
    /// the second assertion checks that.
    /// </remarks>
    [Test]
    [Arguments("169.254.169.254")]
    [Arguments("127.0.0.1")]
    [Arguments("10.0.0.5")]
    public async Task AnIconUrlResolvingToAnAddressWeWillNotDialIsNotFetched(string address)
    {
        var http = new StubHandler();
        var fetcher = Fetcher(http, resolving: address);

        await AssertNothing(await fetcher.FetchAsync(Game, "https://icons.example.org/logo.png"));
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

        await AssertNothing(await fetcher.FetchAsync(Game, "https://icons.example.org/logo.png"));
        await Assert.That(http.Requests).IsEmpty();
    }

    /// <summary>
    /// One redirect is followed, and the address it names is ruled on exactly as the first was.
    /// </summary>
    /// <remarks>
    /// A redirect used to be refused outright, on the true observation that it is a second address
    /// the gate never saw. What that cost, measured against the live catalogue: thirteen of the
    /// sixty-seven games with a declared <c>ICON</c> and no cached one answered 3xx, nearly all of
    /// them an <c>http</c> URL in <c>mush.cnf</c> and an <c>https</c> web server that has since been
    /// set up in front of it. The gate is not skipped here — it runs again, on the target, and this
    /// test asserts both requests happened rather than only that an icon came back.
    /// </remarks>
    [Test]
    [Arguments(HttpStatusCode.MovedPermanently)]
    [Arguments(HttpStatusCode.Found)]
    [Arguments(HttpStatusCode.PermanentRedirect)]
    public async Task OneRedirectIsFollowedAndItsAddressIsRuledOnToo(HttpStatusCode status)
    {
        var http = new StubHandler()
            .Responds(Moved(status, "https://icons.example.org/moved/logo.png"))
            .Responds(Ok(Gif(48, 24)));

        var result = await Fetcher(http).FetchAsync(Game, "https://icons.example.org/logo.png");

        await Assert.That(result.Outcome).IsEqualTo(IconFetchOutcome.Fetched);
        await Assert.That(http.Requests.Select(r => r.RequestUri!.AbsoluteUri)).IsEquivalentTo(
            new[] { "https://icons.example.org/logo.png", "https://icons.example.org/moved/logo.png" });

        // The URL the ICON field named, not the one that answered: this is compared against the
        // field next pass, and storing the target would make an unmoved field look moved every time.
        await Assert.That(result.Icon!.SourceUrl).IsEqualTo("https://icons.example.org/logo.png");
    }

    /// <summary>
    /// The point of running the gate again: a redirect into somewhere we will not dial is refused,
    /// and no socket is opened to it.
    /// </summary>
    /// <remarks>
    /// This is §7.2's whole hazard wearing a hat — the first address is a public one that passes,
    /// and the address the answer points at is the metadata service. A handler following redirects by
    /// itself would already have fetched it.
    /// </remarks>
    [Test]
    [Arguments("169.254.169.254")]
    [Arguments("127.0.0.1")]
    [Arguments("10.0.0.5")]
    public async Task ARedirectIntoAnAddressWeWillNotDialIsRefused(string address)
    {
        var http = new StubHandler()
            .Responds(Moved(HttpStatusCode.Found, "https://inside.example.org/logo.png"))
            .Responds(Ok(Gif(48, 24)));

        var fetcher = Fetcher(
            http,
            resolving: new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["icons.example.org"] = ["203.0.113.10"],
                ["inside.example.org"] = [address],
            });

        await AssertNothing(await fetcher.FetchAsync(Game, "https://icons.example.org/logo.png"));

        // One request, to the address that was ruled on, and no second one to the address that was not.
        await Assert.That(http.Requests.Count).IsEqualTo(1);
    }

    /// <summary>
    /// A second redirect is where following stops.
    /// </summary>
    /// <remarks>
    /// Each further hop is another address to clear for a decoration, and a chain longer than one is
    /// somebody's tracker or somebody's loop far more often than it is a moved logo. The bound is a
    /// hop count rather than a seen-set, so a redirect loop terminates for the same reason.
    /// </remarks>
    [Test]
    public async Task ASecondRedirectIsNotFollowed()
    {
        var http = new StubHandler()
            .Responds(Moved(HttpStatusCode.Found, "https://icons.example.org/one.png"))
            .Responds(Moved(HttpStatusCode.Found, "https://icons.example.org/two.png"))
            .Responds(Ok(Gif(48, 24)));

        await AssertNothing(await Fetcher(http).FetchAsync(Game, "https://icons.example.org/logo.png"));
        await Assert.That(http.Requests.Count).IsEqualTo(2);
    }

    /// <summary>
    /// A <c>Location</c> we could not dial is declined here rather than by whatever would have tried.
    /// </summary>
    [Test]
    [Arguments("javascript:alert(1)")]
    [Arguments("file:///etc/passwd")]
    public async Task ARedirectToSomethingWeCouldNotDialIsRefused(string location)
    {
        var http = new StubHandler().Responds(Moved(HttpStatusCode.Found, location));

        await AssertNothing(await Fetcher(http).FetchAsync(Game, "https://icons.example.org/logo.png"));
        await Assert.That(http.Requests.Count).IsEqualTo(1);
    }

    /// <summary>A relative <c>Location</c> is resolved against the address that answered.</summary>
    /// <remarks>
    /// Which is what the specification allows and what web servers actually send — an absolute-only
    /// reading would decline the commonest redirect there is.
    /// </remarks>
    [Test]
    public async Task ARelativeLocationIsResolvedAgainstWhatAnswered()
    {
        var http = new StubHandler()
            .Responds(Moved(HttpStatusCode.Found, "/images/logo.png"))
            .Responds(Ok(Gif(48, 24)));

        var result = await Fetcher(http).FetchAsync(Game, "https://icons.example.org/a/logo.png");

        await Assert.That(result.Outcome).IsEqualTo(IconFetchOutcome.Fetched);
        await Assert.That(http.Requests[1].RequestUri!.AbsoluteUri)
            .IsEqualTo("https://icons.example.org/images/logo.png");
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

        await AssertNothing(await Fetcher(http).FetchAsync(Game, "https://icons.example.org/logo.png"));
    }

    /// <summary>An image at exactly the ceiling is kept: the limit is a ceiling, not a margin.</summary>
    [Test]
    public async Task AnImageOfExactlyTheCeilingIsKept()
    {
        var bytes = new byte[IconFetcher.MaxBytes];
        Png(64, 64).CopyTo(bytes.AsSpan());

        var http = new StubHandler().Responds(Ok(bytes));

        await Assert.That((await Fetcher(http).FetchAsync(Game, "https://icons.example.org/logo.png")).Icon)
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

        await AssertNothing(await Fetcher(http).FetchAsync(Game, "https://icons.example.org/logo.svg"));
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

        await AssertNothing(await Fetcher(http).FetchAsync(Game, "https://icons.example.org/poster.png"));
    }

    /// <summary>
    /// A far end saying nothing has changed is an ordinary answer, and its own answer.
    /// </summary>
    /// <remarks>
    /// Not <see cref="IconFetchOutcome.Nothing"/>: the refresher counts that as a failure and backs
    /// the URL off, so a server honouring the ETag we sent it would be treated as one that had gone
    /// away — and since a 304 writes no row, its icon would stay stale for ever while being punished
    /// for saying so.
    /// </remarks>
    [Test]
    public async Task NotModifiedIsItsOwnAnswerAndNotAFailure()
    {
        var http = new StubHandler().Responds(new HttpResponseMessage(HttpStatusCode.NotModified));

        var result = await Fetcher(http).FetchAsync(
            Game, "https://icons.example.org/logo.png", etag: "\"abc\"");

        await Assert.That(result.Outcome).IsEqualTo(IconFetchOutcome.Unchanged);
        await Assert.That(result.Icon).IsNull();
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

        await AssertNothing(await Fetcher(http).FetchAsync(Game, url));
        await Assert.That(http.Requests).IsEmpty();
    }

    /// <summary>
    /// Nothing about a failed fetch is a fact about the game, so nothing is returned to store.
    /// </summary>
    /// <remarks>
    /// Rule 5, applied to a picture: a server that's down, slow or angry is our afternoon and not
    /// their game — no row, no marker, no attempt counter.
    /// </remarks>
    [Test]
    [Arguments(HttpStatusCode.InternalServerError)]
    [Arguments(HttpStatusCode.NotFound)]
    [Arguments(HttpStatusCode.Forbidden)]
    public async Task AFailedFetchYieldsNothingToRecord(HttpStatusCode status)
    {
        var http = new StubHandler().Responds(new HttpResponseMessage(status));

        await AssertNothing(await Fetcher(http).FetchAsync(Game, "https://icons.example.org/logo.png"));
    }

    /// <summary>A network that throws is the same answer, and never an exception to the caller.</summary>
    [Test]
    public async Task AThrowingNetworkIsTheSameAnswerAsAMissingIcon()
    {
        var http = new StubHandler().Throwing(new HttpRequestException("no route to host"));

        await AssertNothing(await Fetcher(http).FetchAsync(Game, "https://icons.example.org/logo.png"));
    }

    /// <summary>
    /// A web server that goes quiet past the client's own timeout is a missing icon, not an
    /// exception thrown at whoever called us.
    /// </summary>
    /// <remarks>
    /// <c>HttpClient</c> reports its own <c>Timeout</c> elapsing as a
    /// <see cref="TaskCanceledException"/>, which <em>is</em> an <see cref="OperationCanceledException"/>
    /// — so a filter reading "everything except a cancellation" lets it through as though our host
    /// were stopping. It escaped <see cref="IconRefresher"/>, and .NET's default
    /// <c>BackgroundServiceExceptionBehavior.StopHost</c> then killed the whole process — crawler
    /// included — whenever one stranger's server stalled. The handler here stalls and the client gets
    /// a short timeout, so this is the real exception on the real path.
    /// </remarks>
    [Test]
    public async Task AServerThatStallsPastOurOwnTimeoutIsAMissingIconAndNotAnException()
    {
        var http = new StubHandler().Stalling();

        await AssertNothing(await Impatient(http).FetchAsync(Game, "https://icons.example.org/logo.png"));
    }

    /// <summary>
    /// And the distinction the broken filter was reaching for, kept: a caller that is stopping
    /// stops the fetch, and hears about it.
    /// </summary>
    /// <remarks>
    /// A host shutting down is not an icon that could not be fetched — swallowing it here would leave
    /// <see cref="IconRefresher"/> looping through twenty more addresses on the way out.
    /// </remarks>
    [Test]
    public async Task AHostThatIsStoppingStopsTheFetch()
    {
        var http = new StubHandler().Stalling();

        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        await Assert.That(async () => await Fetcher(http).FetchAsync(
                Game, "https://icons.example.org/logo.png", cancellationToken: stopping.Token))
            .Throws<OperationCanceledException>();
    }

    /// <summary>And the one that works: the bytes, and the type we determined from them.</summary>
    [Test]
    public async Task AGoodIconComesBackWithTheTypeWeReadRatherThanTheOneClaimed()
    {
        // The response claims PNG and the bytes are a GIF. A site echoing the claim would be serving
        // a content type it had never checked from its own origin.
        var http = new StubHandler().Responds(Ok(Gif(48, 24), "image/png"));

        var icon = (await Fetcher(http).FetchAsync(Game, "https://icons.example.org/logo.png")).Icon;

        await Assert.That(icon!.ContentType).IsEqualTo("image/gif");
        await Assert.That(icon.Width).IsEqualTo(48);
        await Assert.That(icon.Height).IsEqualTo(24);
        await Assert.That(icon.SourceUrl).IsEqualTo("https://icons.example.org/logo.png");
        await Assert.That(icon.FetchedAt).IsEqualTo(Now);
    }

    /// <summary>Nothing came back and nothing is to be written — said once, for the many refusals.</summary>
    private static async Task AssertNothing(IconFetchResult result)
    {
        await Assert.That(result.Outcome).IsEqualTo(IconFetchOutcome.Nothing);
        await Assert.That(result.Icon).IsNull();
    }

    private static IconFetcher Fetcher(StubHandler http, params string[] resolving)
    {
        var addresses = resolving.Length == 0 ? ["203.0.113.10"] : resolving;

        return Fetcher(http, new FakeResolver(addresses));
    }

    /// <summary>
    /// The same, where the test needs two hosts to resolve differently — the redirect cases, where
    /// the first address is one we will dial and the second is the point.
    /// </summary>
    private static IconFetcher Fetcher(StubHandler http, IReadOnlyDictionary<string, string[]> resolving) =>
        Fetcher(http, new FakeResolver(resolving));

    private static IconFetcher Fetcher(StubHandler http, FakeResolver resolver) =>
        new(
            new HttpClient(http) { BaseAddress = null },
            new HostScopeGuard(resolver),
            new Frozen(Now));

    private static HttpResponseMessage Moved(HttpStatusCode status, string location)
    {
        var response = new HttpResponseMessage(status);

        // Assigned through the header collection rather than the typed property: the typed one
        // validates, and two of these tests are about a Location that should not survive validation.
        response.Headers.TryAddWithoutValidation("Location", location);

        return response;
    }

    /// <summary>The same fetcher, on a client whose patience runs out while a test still can.</summary>
    private static IconFetcher Impatient(StubHandler http) =>
        new(
            new HttpClient(http) { Timeout = TimeSpan.FromMilliseconds(100) },
            new HostScopeGuard(new FakeResolver(["203.0.113.10"])),
            new Frozen(Now));

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
    /// <remarks>
    /// The script is a queue rather than one response, because a redirect is two answers and the
    /// second is where the interesting half of it lives. A queue that runs dry keeps handing back its
    /// last answer, so the many single-response tests read exactly as they did.
    /// </remarks>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();

        private HttpResponseMessage? _response;

        private Exception? _throws;

        private bool _stalls;

        public List<HttpRequestMessage> Requests { get; } = [];

        public StubHandler Responds(HttpResponseMessage response)
        {
            _responses.Enqueue(response);

            return this;
        }

        public StubHandler Throwing(Exception error)
        {
            _throws = error;

            return this;
        }

        /// <summary>A far end that accepted the connection and then said nothing.</summary>
        public StubHandler Stalling()
        {
            _stalls = true;

            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            if (_throws is not null)
            {
                throw _throws;
            }

            if (_stalls)
            {
                // Longer than any client here waits, and cancellable so the timeout is the client's
                // own rather than this handler deciding to give up.
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (_responses.TryDequeue(out var next))
            {
                _response = next;
            }

            return _response ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    /// <summary>A resolver with a scripted answer, so no test here performs a live lookup.</summary>
    private sealed class FakeResolver : IHostResolver
    {
        private readonly IReadOnlyDictionary<string, string[]>? _byHost;

        private readonly IReadOnlyList<string> _fallback;

        public FakeResolver(IReadOnlyList<string> addresses) => _fallback = addresses;

        public FakeResolver(IReadOnlyDictionary<string, string[]> byHost)
        {
            _byHost = byHost;
            _fallback = ["203.0.113.10"];
        }

        public Task<IReadOnlyList<IPAddress>> ResolveAsync(
            string host, CancellationToken cancellationToken = default)
        {
            var addresses = _byHost is not null && _byHost.TryGetValue(host, out var scripted)
                ? scripted
                : _fallback;

            return Task.FromResult<IReadOnlyList<IPAddress>>([.. addresses.Select(IPAddress.Parse)]);
        }
    }

    private sealed class Frozen(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }
}
