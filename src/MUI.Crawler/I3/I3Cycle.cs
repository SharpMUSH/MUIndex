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
/// Most player-count-less games are one problem: outside the TinyMUD lineage, <c>WHO</c> at a login
/// prompt reads as a character name, so there's nothing on the connect screen to parse, and the
/// LP family predates MSSP entirely. I3's <c>who-req</c> answers the same question a different way —
/// an array of users, counted by length. The three steps run in this order deliberately: seeding
/// first since new addresses are worth the most; binding second because it's a lookup, not a match —
/// the address gets probed and identified by the ordinary crawl path, and this only asks which game
/// that turned out to be; counting last because it's the only step that sends anything to a stranger.
/// Nothing here matches addresses, resolves a name or compares an IP — that's <c>CatalogueBinder</c>'s
/// and <c>IdentityMatcher</c>'s job, and duplicating it here would be a second opinion nobody asked for.
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
            // means the gateway isn't connected, not that the network is empty.
            _log.LogInformation("The I3 gateway returned no muds; skipping this cycle.");
            return new I3CycleResult();
        }

        var result = new I3CycleResult { Listed = muds.Count };

        foreach (var mud in muds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A mud that publishes no player port has said there's nothing to dial — the spec permits
            // 0 for a private or closed mud. Recording it is right; seeding port 0 is not.
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
                // §7.6's rule applied to a new source: host and port and nothing else — the name,
                // driver, mudlib and contact the router also handed us are a third party's claims, not
                // measurements. IsOperatorSeed stays false: these are stranger-supplied addresses and
                // HostScopeGuard must rule on every one, same as a REFERRAL.
                await targets.AddAsync(
                    new CrawlTarget
                    {
                        Id = Guid.CreateVersion7(),
                        Host = mud.HostAddress,
                        Port = mud.PlayerPortNumber,
                        NextProbeAt = now,
                        FirstSeenAt = now,
                        DiscoveredVia = DiscoverySource.I3Mudlist,
                    },
                    cancellationToken);

                result.Seeded++;
                continue;
            }

            // The mud's own name, recorded under a source that says how weak it is. This is a
            // deliberate exception to §7.6's "address only" rule: some LP-family games offer no MSSP
            // and print a login prompt with no title, so there's no measurement of their name to be
            // had through telnet at all, and an IP is not a more honest listing than a name, just an
            // unusable one. It can't displace a name a human typed — `staff` outranks `i3_mudlist` —
            // which matters since nothing polices what a mud calls itself on the network.
            if (target.GameId is { } named && !string.IsNullOrWhiteSpace(mud.Name))
            {
                await fields.UpsertAsync(
                    new GameField(named, "NAME", FieldSource.I3Mudlist, mud.Name, now, now),
                    cancellationToken);
            }

            // What it says it runs: three more fields of the same kind, arriving in the same packet.
            // See I3Description for which field is which and why.
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

            // Promoted by the ordinary crawl since we last looked, so we now know which game it is. A
            // refusal here is ordinary, not exceptional: one game can hold several I3 names (one mud
            // registered once per router), and whichever binds first is the one that counts.
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

            // Marked before the answer, not after: a mud that stays silent must not be asked again
            // next cycle just because it never answered — that would make the quietest participants
            // the ones we bother most.
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
    /// Chosen rather than honoured: I3 has no <c>CRAWL DELAY</c> equivalent for a server to state, so
    /// the restraint has to be ours.
    /// </remarks>
    public TimeSpan AskEvery { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>A ceiling per pass, so a long outage cannot become a burst when it ends.</summary>
    public int MaxAsksPerCycle { get; init; } = 60;

    /// <summary>Spacing between consecutive asks, so a pass is a trickle rather than a flood.</summary>
    /// <remarks>
    /// The gateway's own limit, not just etiquette: <c>deploy/i3/config.yaml</c> caps <c>who</c> at 10
    /// requests per minute on the sidecar's API. 8 seconds is 7.5/minute, under the cap with margin,
    /// and a full 60-ask cycle still finishes inside the 30-minute floor.
    /// </remarks>
    public TimeSpan BetweenAsks { get; init; } = TimeSpan.FromSeconds(8);
}
