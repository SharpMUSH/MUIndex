using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawl;
using MUI.Discovery;

using Microsoft.Extensions.Logging;

namespace MUI.Crawler;

/// <summary>
/// Whose game is this probe? Spec §7.3's judgement, carried out.
/// </summary>
/// <remarks>
/// <para>
/// A probe has to be attributed to a game before anything it measured can be stored, and the wrong
/// answer is the failure that clutters every incumbent catalogue: duplicate listings on one side,
/// silently fused unrelated games on the other. <see cref="IdentityMatcher"/> scores; this acts.
/// </para>
/// <para>
/// <b>Nothing here invents a game for a probe that failed.</b> A dial that never got in carries no
/// MSSP, no banner and no handshake, and minting a listing from an address alone is how a directory
/// fills up with hosts nobody ever reached. A target that has answered before already has its game
/// id on it, so a later failure still records downtime against the right game.
/// </para>
/// </remarks>
public sealed class CatalogueBinder(
    IGameStore games,
    IEndpointStore endpoints,
    IGameFieldStore fields,
    IdentityMatcher matcher,
    IDuplicateReviewRepository reviews,
    TimeProvider time,
    ILogger<CatalogueBinder>? logger = null)
{
    /// <summary>
    /// The game this probe belongs to, minting one if §7.2 and §7.3 say it should be listed, or null
    /// when there is nothing to attribute it to.
    /// </summary>
    public async Task<Binding?> BindAsync(
        CrawlTarget target,
        ProbeResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(result);

        if (target.GameId is { } known && await games.ByIdAsync(known, cancellationToken) is not null)
        {
            if (result.Outcome is ProbeOutcome.Answered)
            {
                await AttachAsync(known, result, cancellationToken);
            }

            return new Binding(known, Created: false, ReviewedAgainst: null);
        }

        if (result.Outcome is not ProbeOutcome.Answered)
        {
            // Never reached, so nothing to list and nothing to record against. The target keeps its
            // own schedule and is probed for ever regardless (§7.1, §7.4).
            return null;
        }

        if (!MayBeListed(target, result))
        {
            logger?.LogDebug(
                "{Host}:{Port} answered but was referred rather than seeded and published no MSSP "
                + "name or hostname of its own, so it is not listed yet (§7.2)",
                result.Host, result.Port);

            return null;
        }

        var verdict = await matcher.ResolveAsync(result, cancellationToken);

        switch (verdict)
        {
            case IdentityVerdict.Merge merge:
                // This probe *is* that game — a game that moved house, or a second port of one we
                // already know. The endpoint change reaches the change feed through AttachAsync.
                await AttachAsync(merge.GameId, result, cancellationToken);
                return new Binding(merge.GameId, Created: false, ReviewedAgainst: null);

            case IdentityVerdict.Review review:
            {
                // Middling. Both games get a live page and a reciprocal link, because a wrongly
                // hidden game is worse than a visible duplicate — so the new one is created exactly
                // as a Fresh verdict would create it, and the pair is opened beside it.
                var created = await CreateAsync(result, cancellationToken);
                await AttachAsync(created, result, cancellationToken);

                await reviews.OpenAsync(
                    created, review.GameId, review.Score, time.GetUtcNow(), cancellationToken);

                logger?.LogInformation(
                    "{Host}:{Port} scored {Score:F2} against an existing game; both are listed and a "
                    + "duplicate review is open",
                    result.Host, result.Port, review.Score.Score);

                return new Binding(created, Created: true, ReviewedAgainst: review.GameId);
            }

            default:
            {
                var created = await CreateAsync(result, cancellationToken);
                await AttachAsync(created, result, cancellationToken);

                logger?.LogInformation("{Host}:{Port} is a new listing", result.Host, result.Port);

                return new Binding(created, Created: true, ReviewedAgainst: null);
            }
        }
    }

    /// <summary>
    /// §7.2's listing gate: <b>"a referred host must independently answer MSSP with its own
    /// <c>NAME</c>/<c>HOSTNAME</c> before it is listed"</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It applies to referrals and to nothing else. A target a human configured, or one an import
    /// seeded, was not proposed by a stranger, so answering at all is enough for it — which is what
    /// keeps a game like Aardwolf, with no MSSP whatsoever, listable.
    /// </para>
    /// <para>
    /// <c>MeaningfulName</c> rather than any <c>NAME</c>: an unedited codebase publishing its own name
    /// has not identified itself, and admitting one would let a referral list mint a listing per
    /// unedited PennMUSH it can point at.
    /// </para>
    /// </remarks>
    private static bool MayBeListed(CrawlTarget target, ProbeResult result) =>
        target.DiscoveredFromGameId is null
        || (result.MsspOutcome is MsspOutcome.Received
            && (MsspReading.MeaningfulName(result.Mssp) is not null
                || MsspReading.Meaningful(result.Mssp, "HOSTNAME") is not null));

    private async Task<Guid> CreateAsync(ProbeResult result, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var name = NameOf(result);
        var slug = await GameSlug.UniqueAsync(
            name,
            async (candidate, ct) => await games.BySlugAsync(candidate, ct) is not null,
            cancellationToken);

        var game = new GameRecord(
            Guid.CreateVersion7(),
            slug,
            name,
            Tagline: null,
            LifecycleState.Active,
            IsClaimed: false,
            FirstSeenAt: now);

        await games.InsertAsync(game, cancellationToken);

        return game.Id;
    }

    /// <summary>
    /// What to call a game nobody has claimed. §4.4's auto-listing marks it <em>discovered,
    /// unclaimed</em>, and this is the name on it until an owner says otherwise.
    /// </summary>
    /// <remarks>
    /// The address is the last resort and is honest: a game that told us nothing about itself is
    /// listed under the only thing we know about it, rather than under its codebase's name — which is
    /// what a naive read of <c>NAME</c> produces, and which would put dozens of unrelated games in the
    /// listing all called "PennMUSH".
    /// </remarks>
    private static string NameOf(ProbeResult result) =>
        MsspReading.MeaningfulName(result.Mssp)
        ?? MsspReading.Meaningful(result.Mssp, "HOSTNAME")
        ?? $"{result.Host}:{result.Port}";

    /// <summary>
    /// Records that this game answers at this address, and fingerprints the connect screen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The banner hash is written here and nowhere else, and without it §7.3's banner signal could
    /// never fire: a game would have to move house before we had ever fingerprinted it. A probe that
    /// saw no banner writes nothing — silence is not a redesign, and the hash of an empty screen is a
    /// value every silent server would share, at half the merge threshold.
    /// </para>
    /// <para>
    /// <b>The identity signals the matcher reads are the MSSP rows the reconciler already wrote.</b>
    /// <c>IdentityFields</c> spells them lower-case (<c>name</c>, <c>created</c>, <c>website</c>,
    /// <c>contact</c>, <c>codebase</c>) and MSSP spells them upper-case, and the two meet because
    /// both <c>IdentityMatcher.StoredAsync</c> and <c>IGameFieldIndex</c> compare field names
    /// case-insensitively. That is load-bearing and not obvious: a lookup that folded only the value
    /// would find nothing, and the matcher would score every probe as fresh while passing its own
    /// tests against a fixture that used its own spelling.
    /// </para>
    /// </remarks>
    private async Task AttachAsync(Guid gameId, ProbeResult result, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var existing = await endpoints.ByAddressAsync(result.Host, result.Port, cancellationToken);

        await endpoints.UpsertAsync(
            new GameEndpoint(
                gameId,
                result.Host,
                result.Port,
                EndpointKind.Telnet,
                existing?.FirstSeenAt ?? now,
                now,
                EndpointState.Active),
            cancellationToken);

        if (existing is null || existing.GameId != gameId)
        {
            // A change feed is a table of events that actually happened (§5.1). A second sighting of
            // an address we already attribute to this game is not one, and writing a row per probe
            // would bury every real event under the crawler's own footsteps.
            await fields.RecordChangeAsync(
                new FieldChange(
                    gameId,
                    IdentityFields.Endpoint,
                    FieldSource.Handshake,
                    existing is null ? null : $"{existing.Host} {existing.Port}",
                    $"{result.Host} {result.Port}",
                    now),
                cancellationToken);
        }

        if (result.Banner is not { Length: > 0 } banner || BannerFingerprint.Flatten(banner).Length == 0)
        {
            return;
        }

        await fields.UpsertAsync(
            new GameField(gameId, IdentityFields.BannerHash, FieldSource.Banner, BannerFingerprint.Of(banner), now, now),
            cancellationToken);

        // Displayed on the grounds that the server sends it unauthenticated to every anonymous
        // connection (§11), ANSI intact, and suppressible on owner request. Upserted rather than
        // reconciled, for the reason FieldObservations.From records: a banner that states its own live
        // player count would otherwise write a change-feed row on every probe, for ever.
        await fields.UpsertAsync(
            new GameField(gameId, ConnectScreenField, FieldSource.Banner, banner, now, now),
            cancellationToken);
    }

    /// <summary>The field the ANSI-rendered connect screen is stored under (spec §4.8, §6.2).</summary>
    public const string ConnectScreenField = "connect_screen";
}

/// <summary>Which game a probe was attributed to, and what that attribution cost.</summary>
public sealed record Binding(Guid GameId, bool Created, Guid? ReviewedAgainst);
