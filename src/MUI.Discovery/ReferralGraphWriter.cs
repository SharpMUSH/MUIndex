using MUI.Crawl;

namespace MUI.Discovery;

/// <summary>What happened to one entry of one <c>REFERRAL</c> list.</summary>
public enum ReferralVerdict
{
    /// <summary>A new crawl target, due immediately.</summary>
    Added,

    /// <summary>Already in the registry, crawled on its own account. Its schedule is not touched.</summary>
    AlreadyKnown,

    /// <summary>The server named itself. Counted rather than dropped, so a run can explain itself.</summary>
    SelfReferral,

    /// <summary>Past <see cref="DiscoveryOptions.MaxDepth"/> hops from a seed.</summary>
    TooDeep,

    /// <summary>A literal address in loopback, private, CGNAT, link-local, unique-local or multicast space.</summary>
    NotRoutable,

    /// <summary>This deployment does not follow referrals at all.</summary>
    ReferralsDisabled,

    /// <summary>
    /// Past <see cref="DiscoveryOptions.MaxFanOutPerSource"/> for this source. A verdict rather than a
    /// silent drop, because a run that cannot say it stopped early cannot be diagnosed.
    /// </summary>
    FanOutExceeded,
}

/// <summary>What one <c>REFERRAL</c> list contributed, and what it did not.</summary>
public sealed record ReferralIntake(int Added, IReadOnlyDictionary<ReferralVerdict, int> Verdicts)
{
    /// <summary>
    /// The intake for a probe that told us nothing about referrals. Distinct from an empty list: a
    /// server whose MSSP was unavailable this hour has not withdrawn its referrals.
    /// </summary>
    public static readonly ReferralIntake Nothing = new(0, new Dictionary<ReferralVerdict, int>());

    /// <summary>How many entries carried <paramref name="verdict"/>, zero when none did.</summary>
    public int Count(ReferralVerdict verdict) => Verdicts.GetValueOrDefault(verdict);
}

/// <summary>
/// The referral graph. Provenance only (spec §7.1) — nothing here schedules anything.
/// </summary>
public interface IReferralRepository
{
    Task<IReadOnlyList<ReferralEdge>> FromGameAsync(Guid gameId, CancellationToken ct);

    /// <summary>
    /// Records the edge, keeping the earliest <c>FirstSeenAt</c> it has ever had. An edge is never
    /// deleted.
    /// </summary>
    Task UpsertAsync(ReferralEdge edge, CancellationToken ct);

    /// <summary>
    /// Marks every edge from this game that is not in <paramref name="stillPresent"/> absent, leaving
    /// the dates alone: <c>LastSeenAt</c> means "last seen", not "last looked" (spec §7.1).
    /// </summary>
    Task MarkAbsentAsync(
        Guid fromGameId,
        IReadOnlyCollection<(string Host, int Port)> stillPresent,
        DateTimeOffset at,
        CancellationToken ct);

    /// <summary>
    /// Everything reachable from this source through the graph, so a poisoned list can be traced and
    /// its whole subtree pruned (spec §7.2). Must terminate on a cycle.
    /// </summary>
    Task<IReadOnlyList<ReferralEdge>> SubtreeOfAsync(Guid rootGameId, CancellationToken ct);
}

/// <summary>
/// Turns one game's MSSP <c>REFERRAL</c> list into provenance edges and candidate crawl targets.
/// </summary>
/// <remarks>
/// <para>
/// <b>Verify, don't trust</b> (spec §7.2). A referral produces a <see cref="CrawlTarget"/> with a
/// null <see cref="CrawlTarget.GameId"/> — a hostname somebody claimed is a game. It becomes a game
/// only when it answers MSSP with its own <c>NAME</c>, in the crawl loop, not here.
/// </para>
/// <para>
/// Depth and fan-out are capped per source, and the referrer is recorded on the target — a referral
/// graph is unbounded by construction, so without the caps one hostile list walks as far as MSSP
/// reaches. <see cref="CrawlTarget.DiscoveredFromGameId"/> plus the edges let a poisoned source's
/// whole subtree be traced and pruned afterwards.
/// </para>
/// <para>
/// <b>A refused referral gets no edge either</b> — an edge is a claim the site is willing to render,
/// and rendering "this game refers to 169.254.169.254" is publishing an attacker's payload. The
/// literal-address check here is the weak half of §7.2, not the gate; see
/// <see cref="ReferralCandidate.IsCrawlable"/> and <see cref="HostScopeGuard"/>.
/// </para>
/// </remarks>
public sealed class ReferralGraphWriter(
    IReferralRepository edges,
    ICrawlTargetRepository targets,
    DiscoveryOptions options,
    TimeProvider time)
{
    public async Task<ReferralIntake> ApplyAsync(
        Guid fromGameId,
        int fromDepth,
        ProbeResult result,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Outcome is not ProbeOutcome.Answered || result.MsspOutcome is not MsspOutcome.Received)
        {
            // We learned nothing about this game's referrals. That is not the same fact as "it lists
            // none", and treating it as such would erase the graph on any hour MSSP was unavailable —
            // or, for RejectedTooLarge, on the strength of our own size limit.
            return ReferralIntake.Nothing;
        }

        var referrals = MsspReferrals.From(result.Mssp);
        var verdicts = new Dictionary<ReferralVerdict, int>();

        if (!options.FollowReferrals)
        {
            foreach (var _ in referrals)
            {
                Count(verdicts, ReferralVerdict.ReferralsDisabled);
            }

            return new ReferralIntake(0, verdicts);
        }

        var now = time.GetUtcNow();
        var present = new List<(string Host, int Port)>();
        var added = 0;
        var accepted = 0;

        foreach (var referral in referrals)
        {
            if (referral.Port == result.Port && CanonicalHost.Same(referral.Host, result.Host))
            {
                Count(verdicts, ReferralVerdict.SelfReferral);
                continue;
            }

            if (!referral.IsCrawlable)
            {
                Count(verdicts, ReferralVerdict.NotRoutable);
                continue;
            }

            if (accepted >= options.MaxFanOutPerSource)
            {
                Count(verdicts, ReferralVerdict.FanOutExceeded);
                continue;
            }

            if (fromDepth + 1 > options.MaxDepth)
            {
                Count(verdicts, ReferralVerdict.TooDeep);
                continue;
            }

            accepted++;
            present.Add((referral.Host, referral.Port));

            await edges.UpsertAsync(
                new ReferralEdge(fromGameId, referral.Host, referral.Port, now, now, Present: true), ct);

            if (await targets.ByAddressAsync(referral.Host, referral.Port, ct) is not null)
            {
                // Already crawled on its own account. Nothing to schedule: the target's NextProbeAt is
                // its own business and a second referral must not drag it forward.
                Count(verdicts, ReferralVerdict.AlreadyKnown);
                continue;
            }

            await targets.AddAsync(new CrawlTarget
            {
                Id = Guid.CreateVersion7(),
                GameId = null,
                Host = referral.Host,
                Port = referral.Port,
                NextProbeAt = now,
                FirstSeenAt = now,
                DiscoveredFromGameId = fromGameId,
                Depth = fromDepth + 1,
            }, ct);

            added++;
            Count(verdicts, ReferralVerdict.Added);
        }

        await edges.MarkAbsentAsync(fromGameId, present, now, ct);
        return new ReferralIntake(added, verdicts);
    }

    private static void Count(Dictionary<ReferralVerdict, int> verdicts, ReferralVerdict verdict) =>
        verdicts[verdict] = verdicts.GetValueOrDefault(verdict) + 1;
}
