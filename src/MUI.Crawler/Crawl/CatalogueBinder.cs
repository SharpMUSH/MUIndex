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
/// <see cref="IdentityMatcher"/> scores; this acts. A probe that never got in mints nothing — an
/// address alone is how a directory fills up with hosts nobody ever reached — but a target that
/// already has a game keeps it, so a later failure still records downtime against the right game.
/// Attribution is decided once; duplication is checked on every probe, since two listings can only be
/// found to be one game from evidence that arrives later.
/// </remarks>
public sealed class CatalogueBinder(
    IGameStore games,
    IEndpointStore endpoints,
    IGameFieldStore fields,
    ISlugHistoryStore slugs,
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
            if (result.Outcome is not ProbeOutcome.Answered)
            {
                return new Binding(known, Created: false, ReviewedAgainst: null);
            }

            await AttachAsync(known, result, cancellationToken);

            // Attribution is settled, but whether the catalogue is listing one game twice is
            // re-checked on every probe, not just the first: two ports of one game can look different
            // the one time they're compared and never be re-scored otherwise.
            if (await matcher.RivalAsync(result, known, cancellationToken) is not { CandidateGameId: { } rival } score)
            {
                return new Binding(known, Created: false, ReviewedAgainst: null);
            }

            // Idempotent by contract: an open pair is returned rather than re-opened, so a twin that
            // matches on every probe for a year is still one row awaiting one judgement.
            await reviews.OpenAsync(known, rival, score, time.GetUtcNow(), cancellationToken);

            logger?.LogInformation(
                "{Host}:{Port} is listed already and scored {Score:F2} against another listing; a "
                + "duplicate review is open and neither page has moved",
                result.Host, result.Port, score.Score);

            return new Binding(known, Created: false, ReviewedAgainst: rival);
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
                var created = await CreateAsync(target, result, cancellationToken);
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
                var created = await CreateAsync(target, result, cancellationToken);
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
    /// "A stranger proposed this" covers both a referral and the public submission form; an operator
    /// seed or a backfill target was not proposed by a stranger, so answering at all is enough for it.
    /// The gate runs before <see cref="IdentityMatcher.ResolveAsync"/> — an ungated target would
    /// otherwise be scored against the whole catalogue on whatever it published, and could mint a
    /// listing and take a rival's slug before the real game ever shows up. The cost, accepted by
    /// §7.2 itself: a real game whose operator never edited MSSP stays unlisted until it does.
    /// </remarks>
    private static bool MayBeListed(CrawlTarget target, ProbeResult result) =>
        !ProposedByAStranger(target) || IdentifiedItself(result);

    /// <summary>
    /// Whether the <em>server</em> told us what it is, by any means it chose.
    /// </summary>
    /// <remarks>
    /// The gate is on evidence a MU\* protocol was spoken, not on MSSP alone — a game like
    /// <c>game.convergencemush.org</c> (RhostMUSH, no MSSP, but an <c>INFO</c> reply naming itself)
    /// used to be refused a listing purely for lacking telnet option 70, a fact about its codebase
    /// rather than whether it exists. A banner is deliberately not among the signals: every TCP
    /// service can print text at a stranger, and admitting one would make the gate a formality.
    /// <c>MeaningfulName</c> rather than any name keeps an unedited install — which has identified its
    /// codebase, not itself — from listing or taking a slug.
    /// </remarks>
    private static bool IdentifiedItself(ProbeResult result) =>
        // It named itself, over MSSP or over INFO. Either is the game answering for the game.
        (result.MsspOutcome is MsspOutcome.Received
            && (MsspReading.MeaningfulName(result.Mssp) is not null
                || MsspReading.Meaningful(result.Mssp, "HOSTNAME") is not null))
        || LoginCommandReading.MeaningfulName(result.Info, result.Version) is not null

        // It named its engine. Weaker than a name and still unforgeable by whoever typed the address
        // into the form: an HTTP server, an SSH daemon and a wrong port do not answer INFO with a
        // codebase. The listing it earns is under its own address (NameOf), never under the engine.
        || LoginCommandReading.MeaningfulCodebase(result.Info, result.Version) is not null

        // We got in and read a player list. The strongest signal here and the one hardest to produce
        // by accident — WhoConfidence.Count means a parser walked a real WHO header, and rule 4 says
        // an unreadable one yields unknown rather than a number.
        || result.Who.Confidence is WhoConfidence.Count;

    /// <summary>
    /// Whether this address reached us from somebody with no standing to vouch for it.
    /// </summary>
    /// <remarks>
    /// The two ways that happens, named in one place so a third one cannot be added without meeting
    /// this. Operator seeds and the backfill are the complement, and both are somebody at our end
    /// choosing an address on purpose.
    /// </remarks>
    private static bool ProposedByAStranger(CrawlTarget target) =>
        target.DiscoveredFromGameId is not null || target.SubmittedAt is not null;

    /// <summary>
    /// Mints the game, carrying <see cref="CrawlTarget.SubmittedAt"/> across.
    /// </summary>
    /// <remarks>
    /// A submitted address answering is still a game and still gets a row, a slug and a permanent
    /// place in the registry — what the marker changes is that <c>NpgsqlGameQueries</c> keeps it off
    /// every public surface until somebody claims it (§8, migration 0010). A submitted address that
    /// turns out to be a second port of a game we already list attaches to that game via the merge
    /// arm instead, and stays exactly as public as it was — never a way to hide a listed game.
    /// </remarks>
    private async Task<Guid> CreateAsync(
        CrawlTarget target,
        ProbeResult result,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var name = NameOf(result);
        // Taken means taken by anybody, ever (§5.7). A slug a game gave up in a rename still redirects
        // and is still in somebody's bookmarks, so handing it to a new listing would silently point an
        // old URL at a game that never wore it — which is worse than the 404 the table exists to
        // prevent.
        var slug = await GameSlug.UniqueAsync(
            name,
            async (candidate, ct) =>
                await games.BySlugAsync(candidate, ct) is not null
                || await slugs.RetiredByAsync(candidate, ct) is not null,
            // What NameOf falls back to, for the same reason and one step later: a game that named
            // itself in a script the fold does not keep is listed at its address rather than at the
            // word "game", which the next such game would then have to share as game-2.
            $"{result.Host}:{result.Port}",
            cancellationToken);

        var game = new GameRecord(
            Guid.CreateVersion7(),
            slug,
            name,
            Tagline: null,
            LifecycleState.Active,
            IsClaimed: false,
            FirstSeenAt: now,
            SubmittedAt: target.SubmittedAt,
            // How this site first heard of the address this game was promoted from, carried
            // across the way the submission date is. A dated fact about our crawl, never a
            // claim about where the game came from.
            DiscoveredVia: target.DiscoveredVia);

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
        // A name the game gave over INFO, which for an MSSP-less codebase is the only one there is.
        // Below MSSP because a server that publishes both has said the MSSP one on purpose, and above
        // the address because "Convergence MUSH" is what its players call it.
        ?? LoginCommandReading.MeaningfulName(result.Info, result.Version)
        // A target admitted by codebase or WHO signals alone lands here by design — it earned a
        // listing by existing, not a name it never gave.
        ?? $"{result.Host}:{result.Port}";

    /// <summary>
    /// Publishes a submitted game the moment a probe shows it to be a game (spec §7.8).
    /// </summary>
    /// <remarks>
    /// Checked on every <see cref="AttachAsync"/> call, not only at creation, since a submission that
    /// shows nothing today may show something next year — the address is kept and re-probed for ever
    /// (§7.4), so an operator who switches MSSP on publishes their own game without us involved. Games
    /// nobody submitted are left alone: they were never hidden, so corroborating them would misrecord
    /// a decision that was never taken. The merge arm reaches this too: a hidden submission that turns
    /// out to be a second port of a listed game is absorbed rather than published separately.
    /// </remarks>
    private async Task CorroborateAsync(
        Guid gameId,
        ProbeResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (await games.ByIdAsync(gameId, cancellationToken)
            is not { SubmittedAt: not null, CorroboratedAt: null })
        {
            return;
        }

        var signals = MuLikeness.Signals(result);

        if (signals.Count == 0)
        {
            return;
        }

        await games.CorroborateAsync(gameId, now, signals, cancellationToken);

        logger?.LogInformation(
            "{Host}:{Port} was submitted rather than found, and answered as a game ({Signals}), so it "
            + "is listed without waiting for a claim (§7.8)",
            result.Host, result.Port, string.Join(", ", signals));
    }

    /// <summary>
    /// Records that this game answers at this address, and fingerprints the connect screen.
    /// </summary>
    /// <remarks>
    /// The banner hash is written here and nowhere else — without it §7.3's banner signal could never
    /// fire. A probe that saw no banner writes nothing, since the hash of an empty screen is a value
    /// every silent server would share. The identity signals the matcher reads are the MSSP rows the
    /// reconciler already wrote; <c>IdentityFields</c> spells them lower-case and MSSP upper-case, and
    /// the two meet only because field-name lookups are case-insensitive throughout.
    /// </remarks>
    private async Task AttachAsync(Guid gameId, ProbeResult result, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();

        await CorroborateAsync(gameId, result, now, cancellationToken);

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
            // A change feed is a table of events that actually happened (§5.1); a re-sighting of an
            // address already attributed to this game isn't one.
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

        // Displayed on the grounds the server sends it unauthenticated to every connection (§11).
        // Upserted rather than reconciled: a banner stating its own live player count would otherwise
        // write a change-feed row on every probe.
        await fields.UpsertAsync(
            new GameField(gameId, ConnectScreenField, FieldSource.Banner, banner, now, now),
            cancellationToken);
    }

    /// <summary>The field the ANSI-rendered connect screen is stored under (spec §4.8, §6.2).</summary>
    public const string ConnectScreenField = "connect_screen";
}

/// <summary>Which game a probe was attributed to, and what that attribution cost.</summary>
public sealed record Binding(Guid GameId, bool Created, Guid? ReviewedAgainst);
