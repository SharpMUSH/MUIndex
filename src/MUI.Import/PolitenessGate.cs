namespace MUI.Import;

/// <summary>
/// One directory's rate limit and its <c>robots.txt</c>, held together because they answer the same
/// question: may we fetch this, and not before when?
/// </summary>
/// <remarks>
/// <see cref="MayFetch"/> answers <c>false</c> until <see cref="AdoptRobots"/> has been called. That
/// is deliberate and is spec §7.6's "honour <c>robots.txt</c>" made unskippable: the gate is closed
/// by default and reading the file is what opens it, so an importer that forgets refuses rather than
/// proceeds.
/// </remarks>
public sealed class PolitenessGate(ImportEtiquette etiquette, TimeProvider time)
{
    private readonly ImportEtiquette _etiquette = etiquette ?? throw new ArgumentNullException(nameof(etiquette));
    private readonly TimeProvider _time = time ?? throw new ArgumentNullException(nameof(time));

    private RobotsPolicy? _robots;
    private DateTimeOffset? _lastFetchAt;

    public bool RobotsAdopted => _robots is not null;

    public DateTimeOffset? LastFetchAt => _lastFetchAt;

    /// <summary>
    /// The configured minimum, or the site's own <c>Crawl-delay</c> when that asks for more.
    /// </summary>
    /// <remarks>
    /// One-directional on purpose, and the same shape as spec §7.7's <c>max(CRAWL DELAY, backoff)</c>
    /// on the telnet side: a site asking for more room gets it, and a site asking for less — or
    /// naming <c>Crawl-delay: 0</c>, which is common — does not licence us to go faster than we
    /// decided to.
    /// </remarks>
    public TimeSpan EffectiveInterval
    {
        get
        {
            var declared = _robots?.CrawlDelayFor(_etiquette.UserAgent);

            return declared is { } delay && delay > _etiquette.MinimumInterval
                ? delay
                : _etiquette.MinimumInterval;
        }
    }

    public void AdoptRobots(RobotsPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        _robots = policy;
    }

    public bool MayFetch(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return _robots is not null && _robots.Allows(path, _etiquette.UserAgent);
    }

    /// <summary>How long a fetch at <paramref name="now"/> would have to wait. Pure, and the test seam.</summary>
    public TimeSpan WaitFor(DateTimeOffset now)
    {
        if (_lastFetchAt is not { } last)
        {
            return TimeSpan.Zero;
        }

        var remaining = EffectiveInterval - (now - last);

        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public async Task EnterAsync(CancellationToken cancellationToken)
    {
        var wait = WaitFor(_time.GetUtcNow());
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, _time, cancellationToken).ConfigureAwait(false);
        }

        _lastFetchAt = _time.GetUtcNow();
    }
}
