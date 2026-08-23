using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using MUI.Ares;
using MUI.Catalog;
using MUI.Discovery;

namespace MUI.Crawler;

/// <summary>
/// One pass over AresCentral: take the games list, seed what is new, record what the hub says about
/// the ones already promoted, and date the ones that have stopped appearing.
/// </summary>
/// <remarks>
/// <para>
/// The first source this site reads by invitation. §7.6's etiquette clause names asking for a
/// documented API as the thing to do in preference to scraping, and this is that having worked —
/// which is why the pass takes more than addresses. The values are the game's own self-description,
/// held by the AresMUSH community's own hub, reached with credentials its maintainer issued. They are
/// still declared and are stored as such: <see cref="FieldSource.AresCentral"/>, below MSSP, never
/// above a human.
/// </para>
/// <para>
/// Nothing here matches addresses, resolves a name, or decides that two listings are one game. That
/// is <c>CatalogueBinder</c>'s and <c>IdentityMatcher</c>'s work, reached the ordinary way, through a
/// probe. This pass seeds and annotates; <b>it never mints a game</b>.
/// </para>
/// </remarks>
public sealed class AresCycle(
    IAresGames hub,
    ICrawlTargetRepository targets,
    IAresListingRepository listings,
    IGameFieldStore fields,
    TimeProvider time,
    ILogger<AresCycle>? log = null)
{
    private readonly ILogger<AresCycle> _log = log ?? NullLogger<AresCycle>.Instance;

    public async Task<AresCycleResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();

        // Deliberately not caught. A refusal has to reach the caller as a failure: swallowing it and
        // carrying on would run the sweep at the end against a list we never received, and date every
        // game we hold as having left.
        var games = await hub.ListAsync(cancellationToken);

        var result = new AresCycleResult { Listed = games.Count };

        foreach (var game in games)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Normalised, not merely trimmed, and for the same reason crawl_target normalises: the
            // hub sends at least one mixed-case hostname, and keying a listing on the raw spelling
            // would turn a change of case into a new row plus a delisting of the old one — a game
            // reported as having left on a pass where nothing happened.
            var host = string.IsNullOrWhiteSpace(game.Hostname)
                ? null
                : CanonicalHost.Normalize(game.Hostname);

            if (string.IsNullOrEmpty(host) || game.Port <= 0)
            {
                // Listed but not dialable — in development, or a game that gave the hub no address.
                // Worth recording where there is an address to key it under; planting a target that
                // can never answer is not.
                if (!string.IsNullOrEmpty(host))
                {
                    await listings.UpsertAsync(Row(game, host, now), cancellationToken);
                }

                result.Unlistable++;
                continue;
            }

            await listings.UpsertAsync(Row(game, host, now), cancellationToken);

            var target = await targets.ByAddressAsync(host, game.Port, cancellationToken);

            if (target is null)
            {
                await targets.AddAsync(
                    new CrawlTarget
                    {
                        Id = Guid.CreateVersion7(),
                        Host = host,
                        Port = game.Port,
                        NextProbeAt = now,
                        FirstSeenAt = now,

                        // Stranger-supplied, exactly like a REFERRAL: HostScopeGuard rules on every
                        // one of these at dial time, and an operator seed it is not (§7.2).
                        IsOperatorSeed = false,
                        DiscoveredVia = DiscoverySource.AresCentral,
                    },
                    cancellationToken);

                result.Seeded++;
                continue;
            }

            if (target.GameId is not { } gameId)
            {
                // A known address the ordinary crawl has not had answer for itself yet. There is
                // nothing to hang a field on, and §7.1 says so.
                continue;
            }

            await listings.BindAsync(host, game.Port, gameId, cancellationToken);
            result.Bound++;

            var wrote = false;

            foreach (var (field, value) in Declared(game))
            {
                await fields.UpsertAsync(
                    new GameField(gameId, field, FieldSource.AresCentral, value, now, now),
                    cancellationToken);
                wrote = true;
            }

            if (wrote)
            {
                result.Described++;
            }
        }

        // Only after a fetch that wholly succeeded — see the first statement of this method. A
        // refused or truncated answer never reaches here, so it can never read as everyone having
        // left at once.
        result.Delisted = await listings.DelistMissingAsync(now, cancellationToken);

        _log.LogDebug("AresCentral pass: {Result}", result);

        return result;
    }

    /// <summary>
    /// The fields the hub holds, skipping every one it left blank.
    /// </summary>
    /// <remarks>
    /// A blank is not a value. Writing one would park an empty string on a rung that outranks the
    /// banner, so a game whose hub entry has an empty website would lose the one we parsed.
    /// </remarks>
    private static IEnumerable<(string Field, string Value)> Declared(AresListedGame game)
    {
        if (Meaningful(game.Name) is { } name)
        {
            yield return ("NAME", name);
        }

        if (Meaningful(game.Description) is { } description)
        {
            yield return ("DESCRIPTION", description);
        }

        if (Meaningful(game.Genre) is { } genre)
        {
            yield return ("GENRE", genre);
        }

        if (Meaningful(game.Website) is { } website)
        {
            yield return ("WEBSITE", website);
        }

        if (Meaningful(game.Status) is { } status)
        {
            yield return ("STATUS", status);
        }

        // Not a field the hub sends — a fact about what the list is. AresCentral lists AresMUSH
        // games, so appearing on it is the statement. Recorded at the same weak rung as the rest,
        // because it is still somebody else's say-so about somebody else's server, and our own
        // handshake must be able to disagree with it.
        yield return (FieldObservations.CodebaseField, "AresMUSH");
    }

    private static string? Meaningful(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AresListing Row(AresListedGame game, string host, DateTimeOffset now) => new()
    {
        Hostname = host,
        Port = game.Port,
        Name = Meaningful(game.Name),
        Description = Meaningful(game.Description),
        Genre = Meaningful(game.Genre),
        Website = Meaningful(game.Website),
        Status = Meaningful(game.Status),
        LastPing = Meaningful(game.LastPing),
        FirstSeenAt = now,
        LastListedAt = now,
    };
}

/// <summary>What one pass did, for the log and for the operator.</summary>
public sealed record AresCycleResult
{
    /// <summary>Games the hub listed.</summary>
    public int Listed { get; set; }

    /// <summary>Addresses we did not have, now in the registry awaiting an ordinary probe.</summary>
    public int Seeded { get; set; }

    /// <summary>Listings attached to a game the crawl had already promoted.</summary>
    public int Bound { get; set; }

    /// <summary>Games the hub's values were written to this pass.</summary>
    public int Described { get; set; }

    /// <summary>Listings publishing no dialable address.</summary>
    public int Unlistable { get; set; }

    /// <summary>Listings the hub stopped mentioning, now dated. Never removed.</summary>
    public int Delisted { get; set; }
}
