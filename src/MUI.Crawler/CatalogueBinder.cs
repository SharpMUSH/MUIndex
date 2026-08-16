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
/// <para>
/// <b>Attribution is decided once; duplication is asked on every probe.</b> An address that already
/// has a game keeps it — re-pointing one behind an operator's back is a merge, and merges are §7.3's
/// business rather than a crawl cycle's side effect. But the catalogue can only find out that two of
/// its listings are one game from evidence that arrives *later*, so a bound target is still scored
/// against the rest of the catalogue and can still open a suspected-duplicate pair. It could not,
/// once, and aardmud.org:23 and :4000 sat in the catalogue with identical connect screens and
/// nothing between them.
/// </para>
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

            // Attribution is settled — this address is that game and stays that game. Whether the
            // catalogue is listing one game twice is a different question, and it used to be asked
            // only once, here, on the first probe that ever reached this address. Two ports of one
            // game that looked different the single time they were compared stayed two listings for
            // ever, because nothing looked again: aardmud.org:23 and :4000 have byte-identical
            // connect screens and no open pair between them.
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
    /// <para>
    /// <b>The gate is on "a stranger proposed this", and a referral is one of two ways that
    /// happens.</b> The other is the public submission form. A target a human operator configured,
    /// or one the backfill seeded, was not proposed by a stranger, so answering at all is enough for
    /// it — which is what keeps a game like Aardwolf, with no MSSP whatsoever, listable by an
    /// operator. Reading the gate as "referrals only" left the form outside it, and a form is a
    /// stranger's proposal with a lower barrier than editing your own MSSP.
    /// </para>
    /// <para>
    /// <b>What that let through was not a hidden listing, it was the identity matcher.</b> This runs
    /// <em>before</em> <c>IdentityMatcher.ResolveAsync</c>, so an ungated target is scored against
    /// the whole catalogue on whatever it cared to publish — a VPS answering <c>NAME "Aardwolf"</c>
    /// and a plausible <c>CREATED</c> is a merge candidate for the real one, and short of that it
    /// mints a game and takes the <c>aardwolf</c> slug, because <c>GameSlug.UniqueAsync</c> asks the
    /// <em>store</em> whether a slug is free and the store does not know that a submitted game is
    /// hidden. The real Aardwolf then arrives and is listed at <c>aardwolf-2</c>, for ever, by
    /// somebody who filled in a form.
    /// </para>
    /// <para>
    /// <b>The cost is §7.2's own, stated there and accepted:</b> a real, reachable game whose
    /// operator never edited one line of MSSP stays unlisted, and submitting it does not change
    /// that. The address is kept and re-probed for ever, so it lists itself the moment a name is
    /// published, with nobody involved.
    /// </para>
    /// <para>
    /// <c>MeaningfulName</c> rather than any <c>NAME</c>: an unedited codebase publishing its own name
    /// has not identified itself, and admitting one would let a referral list — or a submitter with a
    /// default install — mint a listing per unedited PennMUSH it can point at.
    /// </para>
    /// </remarks>
    private static bool MayBeListed(CrawlTarget target, ProbeResult result) =>
        !ProposedByAStranger(target) || IdentifiedItself(result);

    /// <summary>
    /// Whether the <em>server</em> told us what it is, by any means it chose.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The gate is on evidence, not on one protocol.</b> It read MSSP alone for a fortnight, which
    /// quietly meant "a game may be listed if its codebase implements telnet option 70" — a fact
    /// about somebody's build, dressed as a judgement about whether they exist.
    /// <c>game.convergencemush.org:10000</c> is the case that made it plain: RhostMUSH, no MSSP, and
    /// an <c>INFO</c> reply naming the game, its engine and its sixty-four connected players. It was
    /// probed fourteen seconds after it was submitted, answered every six hours for a fortnight, and
    /// was refused a listing every time.
    /// </para>
    /// <para>
    /// <b>What the gate is actually for survives intact.</b> §7.2 exists so a stranger cannot mint a
    /// listing by pointing us at an address, and — the sharper half — so the identity matcher never
    /// scores an unvouched target against the whole catalogue on nothing. Each signal below is the
    /// far end speaking a MU\* protocol about itself, which is exactly the bar MSSP was standing in
    /// for. A banner is deliberately <em>not</em> among them: every TCP service on earth can print
    /// text at a stranger, and admitting one would make the gate a formality.
    /// </para>
    /// <para>
    /// <c>MeaningfulName</c> rather than any name, in both readers, and that is what keeps the
    /// Aardwolf case closed: an unedited install answering with its engine's name has identified its
    /// codebase and not itself, so it still does not list and still cannot take the slug.
    /// </para>
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
    /// <para>
    /// A submitted address answering is still a game and still gets a row, a slug and a permanent
    /// place in the registry — what the marker changes is that <c>NpgsqlGameQueries</c> keeps it off
    /// every public surface until somebody claims it (§8, migration 0010). It is copied at creation
    /// rather than joined on read because the listing asks the question once per row.
    /// </para>
    /// <para>
    /// <b>The merge arm above does not come through here, and that is the interesting case.</b> A
    /// submitted address that turns out to be a second port of a game we already list attaches to
    /// that game and leaves it exactly as public as it was. Anything else would make the form a way
    /// to hide a listed game by naming one of its addresses.
    /// </para>
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
            cancellationToken);

        var game = new GameRecord(
            Guid.CreateVersion7(),
            slug,
            name,
            Tagline: null,
            LifecycleState.Active,
            IsClaimed: false,
            FirstSeenAt: now,
            SubmittedAt: target.SubmittedAt);

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
        // The last resort, and honest: a game that told us nothing about itself is listed under the
        // only thing we know about it, never under its codebase's name. A target admitted by the
        // codebase or WHO signals alone lands here, which is the intended outcome — it earned a
        // listing by existing, not a name it never gave.
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
    /// <summary>
    /// Publishes a submitted game the moment a probe shows it to be a game (spec §7.8).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Here rather than at creation, because a submission that shows nothing today may show
    /// something next year.</b> Every arm of <see cref="BindAsync"/> reaches
    /// <see cref="AttachAsync"/> — the new listing, the game we already knew, and the merge — so a
    /// rule written once here cannot be the rule one path forgot. The address is kept and re-probed
    /// for ever (§7.4), so an operator who switches MSSP on has published their own game by
    /// teatime, with nobody at this end involved. That is the same self-healing property §7.2 claims
    /// for the name gate, and it is the reason this is not a decision taken once at creation.
    /// </para>
    /// <para>
    /// <b>Games nobody submitted are left alone.</b> They were never hidden, so corroborating them
    /// would write a record of a decision that was never taken, and <c>corroborated_at</c> would
    /// stop meaning what the queue reads it as.
    /// </para>
    /// <para>
    /// <b>The merge arm reaches this too, and that is correct.</b> A hidden submission that turns out
    /// to be a second port of a game we already list is absorbed and no longer offered separately —
    /// but if it is instead the survivor, the probe that corroborates it publishes it like any
    /// other. Nothing here can hide a game that was already visible: the write is one-way.
    /// </para>
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
