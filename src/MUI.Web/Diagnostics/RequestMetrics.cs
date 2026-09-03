namespace MUI.Web.Diagnostics;

/// <summary>
/// How much this site was asked for, and how it answered.
/// </summary>
/// <remarks>
/// <para>
/// <b>By status class and by nothing else.</b> A label whose values come from the request decides
/// how many series this process holds, and the listing's facets are combinable — the URL space is
/// their product, and an automated reader walking it generates a stream of URLs each of which is
/// unique. A per-path counter would grow a series per URL and never release one, which would be an
/// unbounded allocation inside the very process whose memory growth this endpoint exists to explain.
/// Five buckets is the whole vocabulary, and that bound is the design rather than an omission.
/// </para>
/// <para>
/// Hand-counted rather than read off ASP.NET Core's own meter, for the same reason the exposition is
/// hand-written: a <c>MeterListener</c> subscribing to <c>http.server.request.duration</c> would
/// bring its own subscription lifetime and its own per-tag storage to a component whose entire
/// purpose is to be beyond suspicion about memory.
/// </para>
/// </remarks>
public sealed class RequestMetrics
{
    // 1xx through 5xx, plus a bucket for anything outside that — indexed by the hundreds digit, so
    // observing is an array increment and no allocation at all.
    private readonly long[] _byClass = new long[6];

    private long _inFlight;

    public void Observe(int statusCode)
    {
        var bucket = statusCode / 100;

        Interlocked.Increment(
            ref _byClass[bucket is >= 1 and <= 5 ? bucket : 0]);
    }

    public void Entered() => Interlocked.Increment(ref _inFlight);

    public void Left() => Interlocked.Decrement(ref _inFlight);

    public void WriteTo(PrometheusText text)
    {
        ArgumentNullException.ThrowIfNull(text);

        const string Help =
            "Requests answered, by status class. Deliberately carries no path label: the listing's "
            + "URL space is unbounded and a series per URL would never be released.";

        for (var bucket = 1; bucket <= 5; bucket++)
        {
            text.Counter(
                "mui_http_requests_total",
                Help,
                Interlocked.Read(ref _byClass[bucket]),
                ("status", $"{bucket}xx"));
        }

        text.Counter(
            "mui_http_requests_total",
            Help,
            Interlocked.Read(ref _byClass[0]),
            ("status", "other"));

        text.Gauge(
            "mui_http_requests_in_flight",
            "Requests being answered right now. A number that climbs while the request rate does not "
            + "is requests that are not finishing, which is what a wedged dependency looks like.",
            Interlocked.Read(ref _inFlight));
    }
}
