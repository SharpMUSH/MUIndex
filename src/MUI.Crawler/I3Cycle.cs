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
    IGameFieldStore fields,
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
                    mud.Name, mud.HostAddress, 0, mud.Answers("who"), mud.IsUp, now, cancellationToken);
                result.Unlistable++;
                continue;
            }

            await bindings.UpsertAsync(
                mud.Name, mud.HostAddress, mud.PlayerPortNumber, mud.Answers("who"), mud.IsUp, now,
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

            // The mud's own name for itself, recorded under a source that says how weak it is.
            //
            // §7.6 says the seed contributes an address and nothing else, and this is the deliberate
            // exception rather than an erosion of it: 31 games are listed as an IP because they are
            // LP-family, offer no MSSP, and print a login prompt with no title in it. There is no
            // measurement of their names to be had through the telnet probe — not "not yet", but not
            // at all — and `82-153-225-173-4242` is not a more honest listing than `Nightfall`, only
            // an unusable one. The provenance system exists to carry a weak value labelled weak.
            //
            // It cannot displace a name a human typed: `staff` outranks `i3_mudlist`, which matters
            // because the live network carries `Your MUD Name`, `test` and `DeadSouls-FluffOS2019`
            // beside the real ones. Nothing polices what a mud calls itself.
            if (target.GameId is { } named && !string.IsNullOrWhiteSpace(mud.Name))
            {
                await fields.UpsertAsync(
                    new GameField(named, "NAME", FieldSource.I3Mudlist, mud.Name, now, now),
                    cancellationToken);
            }

            // And what it says it runs, which is three more fields of the same kind arriving in the
            // same packet. They were modelled when the gateway was written and never read; 179 of
            // 179 live entries fill in all three, and 47 of the games bound to an I3 name carry no
            // codebase at all. See I3Description for which field is which and why this is declared.
            if (target.GameId is { } described)
            {
                foreach (var observation in I3Description.From(mud))
                {
                    await fields.UpsertAsync(
                        new GameField(
                            described, observation.Field, observation.Source, observation.Value, now, now),
                        cancellationToken);
                }
            }

            // Promoted by the ordinary crawl since we last looked, so we now know which game it is.
            //
            // A refusal here is ordinary rather than exceptional: one game routinely holds several I3
            // names — The Zone is also The Zone-dalet, The Zone-i4 and The Zone-wpr, one mud
            // registered once per router — and whichever binds first is the one that counts it. The
            // rest are duplicates of a fact we already have, and were once enough to tear down every
            // pass for ever.
            if (target.GameId is { } game
                && await bindings.BindAsync(mud.Name, game, cancellationToken))
            {
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
    /// <remarks>
    /// <b>The gateway's own limit, not just etiquette.</b> <c>deploy/i3/config.yaml</c> caps
    /// <c>who</c> at 10 requests per minute on the sidecar's API. A 2-second spacing sends up to 30,
    /// three times that — measured on 2026-08-18: 60-ask cycles lost 13-34% of asks to
    /// <c>i3_no_reply</c>, 25-ask cycles lost 4-8%, 11-ask cycles lost none, tracking a token bucket
    /// exhausting faster in bigger batches. 8 seconds is 7.5/minute, under the cap with margin, and a
    /// full 60-ask cycle still finishes in 8 minutes — inside the 30-minute floor with room to grow.
    /// </remarks>
    public TimeSpan BetweenAsks { get; init; } = TimeSpan.FromSeconds(8);
}
