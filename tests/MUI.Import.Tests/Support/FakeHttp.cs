using System.Net;

namespace MUI.Import.Tests.Support;

/// <summary>
/// The only <see cref="HttpMessageHandler"/> in this suite. It serves a dictionary of canned
/// responses and records what was asked for, in order, with the user agent each request carried.
/// </summary>
/// <remarks>
/// An unlisted URI is a 404 rather than an exception, deliberately: that is what the internet does,
/// and one of the behaviours under test is that a missing <c>robots.txt</c> means allow-all.
/// </remarks>
public static class FakeHttp
{
    public sealed class Handler(IReadOnlyDictionary<string, (HttpStatusCode Status, string Body)> responses)
        : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<string, (HttpStatusCode Status, string Body)> _responses =
            responses ?? throw new ArgumentNullException(nameof(responses));

        public List<(string Uri, string? UserAgent)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            var userAgent = request.Headers.TryGetValues("User-Agent", out var values)
                ? string.Join(' ', values)
                : null;

            Requests.Add((uri, userAgent));

            var (status, body) = _responses.TryGetValue(uri, out var found)
                ? found
                : (HttpStatusCode.NotFound, string.Empty);

            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    public static (Handler Handler, HttpClient Client) Serving(params (string Uri, string Body)[] responses)
    {
        ArgumentNullException.ThrowIfNull(responses);

        var handler = new Handler(responses.ToDictionary(r => r.Uri, r => (HttpStatusCode.OK, r.Body)));

        return (handler, new HttpClient(handler));
    }
}
