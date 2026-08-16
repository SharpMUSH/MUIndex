using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using MUI.Catalog;
using MUI.Discovery;
using MUI.I3;

namespace MUI.Crawler;

/// <summary>
/// One pass over Intermud-3: take the mudlist, seed what is new, bind what has been promoted, and
/// count the games that consent to being counted.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> 240 of 428 sampled games have no player count, and roughly 210 of those
/// are one problem: outside the TinyMUD lineage, <c>WHO</c> at a login prompt is read as a character
/// name, so there is nothing on the connect screen to parse. The LP family, which is most of what is
/// dark, predates MSSP and never adopted it. I3 answers the same question a different way —
/// <c>who-req</c> returns an array of users, and the count is the length of the array.
/// </para>
/// <para>
/// <b>Three steps and they are deliberately in this order.</b> Seeding comes first because it is
/// worth the most: of 179 muds on the network, 134 are addresses we do not have and 85 of those are
/// up, against 8 dark games that binding rescues. Binding comes second because it is a *lookup*
/// rather than a match — the address we just seeded gets probed by the ordinary crawl, promoted by
/// the ordinary identity path, and this asks which game that turned out to be. Counting comes last
/// because it is the only step that sends anything to a stranger.
/// </para>
/// <para>
/// <b>Nothing here matches addresses, resolves a name or compares an IP</b>, and that is the point.
/// Deciding whether an address is a game we already have is <c>CatalogueBinder</c>'s and
/// <c>IdentityMatcher</c>'s job; it is tested, it knows how to tell one game on two ports from two
/// games on one host, and duplicating any of it here would be a second opinion nobody asked for.
/// </para>
/// </remarks>
public sealed class I3Cycle(
    II3Gateway gateway,
    ICrawlTargetRepository targets,
    II3BindingRepository bindings,
    IPresenceStore presence,
    I3Options options,
    TimeProvider time,
    ILogger<I3Cycle>? log = null)
{
    private readonly ILogger<I3Cycle> _log = log ?? NullLogger<I3Cycle>.Instance;

    public async Task<I3CycleResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();

        var muds = await gateway.MudlistAsync(cancellationToken);
        if (muds.Count == 0)
        {
            // The router pushes the mudlist unasked and the gateway caches it, so an empty answer
            // means the gateway is not connected rather than that the network is empty. Doing
            // nothing is right; seeding nothing and counting nothing are both already the case.
            _log.LogInformation("The I3 gateway returned no muds; skipping this cycle.");
            return new I3CycleResult();
        }

        var result = new I3CycleResult { Listed = muds.Count };

        foreach (var mud in muds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A mud that publishes no player port has said there is nothing to dial — the spec
            // permits 0 for a mud that is private or closed, and MUIndex publishes it about itself.
            // Recording it is right; seeding an address at port 0 is not.
            if (mud.PlayerPortNumber <= 0 || string.IsNullOrWhiteSpace(mud.HostAddress))
            {
                await bindings.UpsertAsync(
                    mud.Name, mud.HostAddress, 0, mud.Answers("who"), now, cancellationToken);
                result.Unlistable++;
                continue;
            }

            await bindings.UpsertAsync(
                mud.Name, mud.HostAddress, mud.PlayerPortNumber, mud.Answers("who"), now,
                cancellationToken);

            var target = await targets.ByAddressAsync(
                mud.HostAddress, mud.PlayerPortNumber, cancellationToken);

            if (target is null)
            {
                // §7.6's rule, applied to a new source: host and port and nothing else. The name, the
                // driver, the mudlib and the contact address the router also handed us are the mud's
                // claims relayed by a third party; every fact this site shows about a game is
                // measured by our own crawler, so the address is all we take.
                //
                // IsOperatorSeed stays false, which is the security-relevant half: these are
                // stranger-supplied addresses and HostScopeGuard must rule on every one of them, the
                // same way it rules on a REFERRAL.
                await targets.AddAsync(
                    new CrawlTarget
                    {
                        Id = Guid.CreateVersion7(),
                        Host = mud.HostAddress,
                        Port = mud.PlayerPortNumber,
                        NextProbeAt = now,
                        FirstSeenAt = now,
                    },
                    cancellationToken);

                result.Seeded++;
                continue;
            }

            // Promoted by the ordinary crawl since we last looked, so we now know which game it is.
            if (target.GameId is { } game)
            {
                await bindings.BindAsync(mud.Name, game, cancellationToken);
                result.Bound++;
            }
        }

        result.Counted = await AskAsync(now, cancellationToken);
        return result;
    }

    private async Task<int> AskAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var askable = await bindings.AskableAsync(
            now - options.AskEvery, options.MaxAsksPerCycle, cancellationToken);

        var asked = 0;

        foreach (var binding in askable)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Marked before the answer, not after. The floor exists to bound what we send, and a mud
            // that stays silent must not therefore be asked again on the next cycle — that would
            // turn the quietest participants into the ones we bother most.
            await bindings.MarkAskedAsync(binding.MudName, now, cancellationToken);

            var reply = await gateway.WhoAsync(binding.MudName, cancellationToken);
            var reading = I3PresenceChoice.From(reply);

            await presence.AppendAsync(
                reading.Count is { } count
                    ? PresenceSample.Counted(binding.GameId!.Value, now, count, reading.Source)
                    : PresenceSample.Unmeasurable(binding.GameId!.Value, now, reading.Reason!.Value,
                        reading.Source),
                cancellationToken);

            asked++;

            // A pause between muds rather than a burst. I3 has no CRAWL DELAY to honour, so the
            // restraint is ours to choose and is chosen here, where it is visible.
            if (options.BetweenAsks > TimeSpan.Zero)
            {
                await Task.Delay(options.BetweenAsks, time, cancellationToken);
            }
        }

        return asked;
    }
}

/// <summary>What one pass did, for the log and for the operator.</summary>
public sealed record I3CycleResult
{
    /// <summary>Muds the router listed.</summary>
    public int Listed { get; set; }

    /// <summary>Addresses we did not have, now in the registry awaiting an ordinary probe.</summary>
    public int Seeded { get; set; }

    /// <summary>Muds newly attached to a game.</summary>
    public int Bound { get; set; }

    /// <summary>Muds asked for a count this pass.</summary>
    public int Counted { get; set; }

    /// <summary>
    /// Muds publishing no dialable address — private, closed, or a gateway like ourselves.
    /// </summary>
    public int Unlistable { get; set; }
}

/// <summary>How hard to lean on the network.</summary>
public sealed record I3Options
{
    /// <summary>
    /// The floor between two <c>who-req</c>s to one mud.
    /// </summary>
    /// <remarks>
    /// <b>Chosen rather than honoured.</b> MSSP has a <c>CRAWL DELAY</c> a server can state and the
    /// telnet crawler obeys it; I3 has no equivalent, so there is nobody to defer to and the restraint
    /// has to be ours. Thirty minutes over ~177 muds is a packet every ten seconds — nothing to a
    /// router, and invisible to any single game.
    /// </remarks>
    public TimeSpan AskEvery { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>A ceiling per pass, so a long outage cannot become a burst when it ends.</summary>
    public int MaxAsksPerCycle { get; init; } = 60;

    /// <summary>Spacing between consecutive asks, so a pass is a trickle rather than a flood.</summary>
    public TimeSpan BetweenAsks { get; init; } = TimeSpan.FromSeconds(2);
}
