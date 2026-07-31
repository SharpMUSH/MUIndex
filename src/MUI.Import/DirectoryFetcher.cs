namespace MUI.Import;

/// <summary>
/// One directory's HTTP surface, as an importer sees it.
/// </summary>
/// <remarks>
/// An interface so an importer's parsing can be tested against a recorded fixture with no etiquette
/// configuration in the way, and so nothing in this assembly holds an <c>HttpClient</c> except the
/// one implementation below.
/// </remarks>
public interface IDirectoryFetcher
{
    ImportEtiquette Etiquette { get; }

    /// <summary>Reads <c>robots.txt</c> and adopts it. Must precede the first content fetch.</summary>
    Task PrimeRobotsAsync(CancellationToken cancellationToken);

    Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken);
}

/// <summary>
/// The only place in this assembly an HTTP request is made, so the etiquette rules of spec §7.6 and
/// §11 are enforced once rather than remembered by each importer.
/// </summary>
/// <remarks>
/// Four refusals live here, in order: <c>robots.txt</c> has not been read yet; the source has no
/// permitted route at all; this URI is not under the route the planner chose — which is what makes a
/// configured <see cref="ImportEtiquette.ScrapeUri"/> unreachable while a bulk export or an API
/// exists; and <c>robots.txt</c> forbids the path. Only after all four does the rate limit run.
/// </remarks>
public sealed class DirectoryFetcher : IDirectoryFetcher
{
    private readonly HttpClient _http;

    public DirectoryFetcher(HttpClient http, ImportEtiquette etiquette, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(etiquette);
        ArgumentNullException.ThrowIfNull(time);

        _http = http;
        Etiquette = etiquette;
        Gate = new PolitenessGate(etiquette, time);
    }

    public ImportEtiquette Etiquette { get; }

    public PolitenessGate Gate { get; }

    public FetchDecision Decision => EtiquettePlanner.Decide(Etiquette);

    /// <summary>
    /// Reads <c>robots.txt</c> and hands it to the gate.
    /// </summary>
    /// <remarks>
    /// A missing or unreadable file is <see cref="RobotsPolicy.AllowAll"/> rather than a refusal —
    /// that is what the standard means by absence, and treating a 404 as a prohibition would make us
    /// unable to read the majority of small sites in this hobby.
    /// </remarks>
    public async Task PrimeRobotsAsync(CancellationToken cancellationToken)
    {
        using var request = Request(Etiquette.RobotsUri);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Gate.AdoptRobots(RobotsPolicy.Parse(body));

            return;
        }

        Gate.AdoptRobots(RobotsPolicy.AllowAll);
    }

    public async Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!Gate.RobotsAdopted)
        {
            throw new EtiquetteViolationException(
                $"{Etiquette.SourceName}: robots.txt has not been read. "
                + "Call PrimeRobotsAsync before the first content fetch.");
        }

        var decision = Decision;
        if (decision.Route is FetchRoute.None)
        {
            throw new EtiquetteViolationException($"{Etiquette.SourceName}: {decision.RefusedReason}.");
        }

        if (!EtiquettePlanner.MayFetch(Etiquette, uri))
        {
            throw new EtiquetteViolationException(
                $"{Etiquette.SourceName}: refusing {uri} — the permitted route is {decision.Route} "
                + $"rooted at {decision.Uri}.");
        }

        if (!Gate.MayFetch(uri.AbsolutePath))
        {
            throw new EtiquetteViolationException(
                $"{Etiquette.SourceName}: robots.txt disallows {uri.AbsolutePath} for {Etiquette.UserAgent}.");
        }

        await Gate.EnterAsync(cancellationToken).ConfigureAwait(false);

        using var request = Request(uri);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private HttpRequestMessage Request(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);

        // TryAddWithoutValidation because the parenthesised info URL is a comment in RFC 9110 terms
        // and the strict parser rejects it. The header is ours and its shape is tested.
        request.Headers.TryAddWithoutValidation("User-Agent", Etiquette.UserAgent);

        return request;
    }
}
