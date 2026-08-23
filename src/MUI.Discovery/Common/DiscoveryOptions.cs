namespace MUI.Discovery;

/// <summary>
/// How busy a game looked the last time we got in. Spec §7.7 tightens the interval "for games with
/// recent activity"; this is that input, derived from the probe rather than stored, because it is a
/// fact about the observation we are rescheduling from.
/// </summary>
/// <remarks>
/// <b>Not <see cref="MUI.Catalog.ActivityBand"/>.</b> That one is the listing facet of spec §5.2 and
/// describes a game as a reader filters on it. This one describes one probe as the scheduler reads
/// it, with only the three members a single observation can tell us.
/// </remarks>
public enum ActivityBand
{
    /// <summary>
    /// No usable reading — a failed probe, or a <c>WHO</c> the parser could not read. Parsers never
    /// fabricate, and neither does this: an unreadable count is not a quiet game.
    /// </summary>
    Unknown,

    /// <summary>We got in and nobody was there. A measured zero is a real fact, not an absence (spec §5.4).</summary>
    Quiet,

    /// <summary>Somebody was connected.</summary>
    Busy,
}

/// <summary>
/// Everything a crawl run is allowed to do, with defaults chosen to be conservative rather than fast.
/// </summary>
/// <remarks>
/// MSSP's <c>REFERRAL</c> is a documented invitation to crawl, but an invitation is not a licence to be
/// expensive. Every default here errs toward the operator on the other end.
/// </remarks>
public sealed record DiscoveryOptions
{
    /// <summary>How many referral hops from an originally seeded game a target may be discovered at (spec §7.2).</summary>
    public int MaxDepth { get; init; } = 4;

    /// <summary>The most referrals one game's <c>REFERRAL</c> list may contribute in one probe (spec §7.2).</summary>
    public int MaxFanOutPerSource { get; init; } = 50;

    /// <summary>Off makes this a status checker for a known list; on — the default — makes it a crawler.</summary>
    public bool FollowReferrals { get; init; } = true;

    /// <summary>
    /// How many probes may be in flight at once. Enforced by a semaphore in the crawl loop and
    /// deliberately not by <see cref="CrawlRateLimiter"/>: it is a fact about connections in flight
    /// rather than about time, and folding the two together would make neither testable.
    /// </summary>
    public int MaxConcurrency { get; init; } = 8;

    /// <summary>The minimum gap between the starts of any two connections, anywhere.</summary>
    public TimeSpan GlobalInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>The minimum gap between two connections to the same host — the floor under a retry.</summary>
    public TimeSpan PerHostInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How many due targets one cycle claims.</summary>
    public int BatchSize { get; init; } = 200;

    /// <summary>How long the loop rests between cycles when it holds the lease.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a replica that lost the advisory lock waits before asking again (spec §12).</summary>
    public TimeSpan LeaseRetryInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The crawl loop's own hard bound on one probe, applied on top of whatever the probe promises.
    /// The crawler shares a process with the web tier, so a wedged probe must not be able to starve
    /// request threads (spec §12) — and the loop does not get to trust a collaborator for that.
    /// </summary>
    public TimeSpan ProbeTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How many extra dials a failing target gets before the failure is believed.
    /// </summary>
    /// <remarks>
    /// <b>One dial is a measurement of one dial.</b> Publishing the first failure as the game's
    /// reachability turned out to mostly capture transient faults (a slow DNS lookup, a connection
    /// reset) rather than real outages — one extra dial is enough to tell the two apart. Zero
    /// restores the old behaviour and is what the CLI's single-shot mode wants; it costs nothing on
    /// the common path, since only a failing target ever retries.
    /// </remarks>
    public int ConfirmationAttempts { get; init; } = 1;

    /// <summary>How long to wait before a confirming dial.</summary>
    /// <remarks>
    /// The floor, not the gap: <see cref="PerHostInterval"/> still applies to the confirming dial, so
    /// the real pause is the larger of the two. Long enough to outlast a momentary fault, short
    /// enough that a game which really has gone is published within the same cycle.
    /// </remarks>
    public TimeSpan ConfirmationDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>At or above this score the probe is merged into the candidate game (spec §7.3).</summary>
    public double AutoMergeThreshold { get; init; } = IdentityWeights.AutoMergeThreshold;

    /// <summary>At or above this score a suspected-duplicate pair is opened instead (spec §7.3).</summary>
    public double ReviewThreshold { get; init; } = IdentityWeights.ReviewThreshold;

    /// <summary>
    /// How many separate listings must publish one connect screen before it stops being read as
    /// identity at all — see <c>IdentityMatcher.IdentifiesOneGameAsync</c>.
    /// </summary>
    /// <remarks>
    /// Three, because two is a duplicate and three is a pattern: one game answering at two addresses
    /// is the ordinary case this signal exists to catch, and a screen a third unrelated listing also
    /// sends is the codebase's rather than anybody's. Measured from the catalogue, so a codebase that
    /// ships a distinctive default needs no list entry and a game that hand-edits one keeps its
    /// signal.
    /// </remarks>
    public int SharedBannerListings { get; init; } = 3;

    /// <summary>
    /// Throws when a setting could only have arrived from a typo or a hand-edited file, rather than
    /// starting a run that would be wrong in a way nobody notices until it is on the network.
    /// </summary>
    public void Validate()
    {
        if (MaxConcurrency < 1)
        {
            throw new ArgumentException("MaxConcurrency must be at least 1.");
        }

        if (BatchSize < 1)
        {
            throw new ArgumentException("BatchSize must be at least 1.");
        }

        if (MaxDepth < 0)
        {
            throw new ArgumentException("MaxDepth cannot be negative.");
        }

        if (MaxFanOutPerSource < 0)
        {
            throw new ArgumentException("MaxFanOutPerSource cannot be negative.");
        }

        if (GlobalInterval < TimeSpan.Zero || PerHostInterval < TimeSpan.Zero)
        {
            throw new ArgumentException("Rate-limit intervals cannot be negative.");
        }

        if (ConfirmationAttempts < 0)
        {
            throw new ArgumentException("ConfirmationAttempts cannot be negative.");
        }

        if (ConfirmationDelay < TimeSpan.Zero)
        {
            throw new ArgumentException("ConfirmationDelay cannot be negative.");
        }

        if (ProbeTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("ProbeTimeout must be positive: an unbounded probe can starve the web tier.");
        }

        if (PollInterval <= TimeSpan.Zero || LeaseRetryInterval <= TimeSpan.Zero)
        {
            throw new ArgumentException("PollInterval and LeaseRetryInterval must be positive.");
        }

        if (ReviewThreshold > AutoMergeThreshold)
        {
            throw new ArgumentException("ReviewThreshold cannot exceed AutoMergeThreshold: nothing would ever be reviewed.");
        }

        if (SharedBannerListings < 2)
        {
            throw new ArgumentException(
                "SharedBannerListings must be at least 2: below that the banner signal never fires, "
                + "since the game being scored publishes the screen itself.");
        }
    }
}
