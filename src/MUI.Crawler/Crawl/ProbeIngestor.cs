using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawl;

using Microsoft.Extensions.Logging;

// MUI.Catalog.ActivityBand is the listing facet a reader filters on; MUI.Discovery.ActivityBand is
// how busy one probe looked, as the scheduler reads it. The names collide, hence the alias.
using SchedulerBand = MUI.Discovery.ActivityBand;

namespace MUI.Crawler;

/// <summary>
/// What one <see cref="ProbeResult"/> does to storage — spec §6.5's seam, in one place.
/// </summary>
/// <remarks>
/// <para>
/// <code>
/// ProbeSession ──▶ ProbeResult ──┬──▶ FieldReconciler    (§5.1)
///                                ├──▶ PresenceWriter     (§5.2)
///                                └──▶ AvailabilityWriter  (§5.3)
/// </code>
/// </para>
/// <para>
/// <b>The four rules this type exists to hold, and each is a bug this codebase has already reasoned
/// about:</b>
/// </para>
/// <list type="number">
/// <item>
/// A probe that <b>answered with a count</b> writes one presence row with that count — including a
/// measured zero, which is a filled cell and a real fact about a game. <c>who</c> outranks
/// <c>mssp</c>, and the choice is made in <see cref="PresenceChoice"/> before the writer is called,
/// because <c>presence_sample</c> is keyed <c>(game_id, at)</c> and there is nowhere to keep both.
/// </item>
/// <item>
/// A probe that <b>answered and could not be counted</b> writes a presence row with
/// <c>count = NULL</c> and a reason. <b>Never a zero</b>, and never nothing: writing nothing would be
/// indistinguishable from not having probed, which renders identically to downtime, and a game whose
/// <c>DOING</c> header is customised past our parser would show as permanently dark while running
/// perfectly well.
/// </item>
/// <item>
/// A probe that <b>failed</b> writes an availability transition and <b>no presence row at all</b>.
/// It writes no fields either: a dial that never got in confirmed nothing, and bumping
/// <c>last_confirmed_at</c> on rows nobody re-observed would record our crawler having run as the
/// game having answered.
/// </item>
/// <item>
/// A <b>scope refusal never reaches this type</b>. <see cref="ProbeOutcome"/> has two members and
/// neither is <c>Refused</c>; a refusal happens before a <see cref="ProbeResult"/> exists and is
/// counted on the cycle instead. Do not add the member, and do not manufacture a result for one — the
/// guard's own remarks explain why <c>FailureCause.Refused</c> (the far end sent an RST, a real
/// measurement of a real host) can never be allowed to mean the same thing.
/// </item>
/// </list>
/// <para>
/// Three writes sit alongside the three writers because they are the same event seen from the game
/// row rather than from the series: <c>last_reachable_at</c>, which is what §7.5's grace and §5.2's
/// <c>quiet</c>-versus-<c>dark</c> band are both measured from; §7.5's automatic un-archiving —
/// "one successful probe restores the game to the default listing", with no human on either side of
/// the transition; and §5.7's re-mint, because a game renaming itself is a measured change like any
/// other and the name it is listed under has just been reconciled. The re-mint runs <em>after</em>
/// the reconciler and never before it: it reads the winning <c>NAME</c> this probe just confirmed.
/// </para>
/// </remarks>
public sealed class ProbeIngestor(
    IPresenceWriter presence,
    IAvailabilityWriter availability,
    IFieldReconciler fields,
    IGameStore games,
    ArchiveSweeper archive,
    SlugMinter slugs,
    ILogger<ProbeIngestor>? logger = null)
{
    /// <summary>Applies everything one probe of one game means.</summary>
    /// <param name="gameId">
    /// The game this probe is attributed to. Resolving that is identity's job (§7.3) and deliberately
    /// not this type's: it happens before ingestion, because a probe that cannot be attributed to a
    /// game has nowhere to write and must not invent one.
    /// </param>
    /// <param name="result">One telnet session's whole answer.</param>
    /// <param name="cancellationToken">The cycle's budget.</param>
    public async Task<Ingestion> IngestAsync(
        Guid gameId,
        ProbeResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        var at = result.ObservedAt;

        return result.Outcome is ProbeOutcome.Answered
            ? await AnsweredAsync(gameId, result, at, cancellationToken)
            : await FailedAsync(gameId, result, at, cancellationToken);
    }

    private async Task<Ingestion> AnsweredAsync(
        Guid gameId,
        ProbeResult result,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        var reachability = await availability.ObserveAsync(
            gameId, AvailabilityState.Reachable, FailureCause.None, at, cancellationToken: cancellationToken);

        var reading = PresenceChoice.From(result);
        var written = await presence.WriteAsync(gameId, reading, at, cancellationToken);

        var reconciliation = await fields.ApplyAsync(
            gameId, FieldObservations.From(result), at, cancellationToken);

        // The two game-row facts. MarkReachableAsync only ever moves forward, so a slow probe landing
        // out of order cannot walk it backwards.
        await games.MarkReachableAsync(gameId, at, cancellationToken);

        // §7.5, and it is immediate rather than swept: a game that comes back is re-listed by the
        // probe that found it. Archiving is never permanent and never needs a human.
        var restored = await archive.RestoreAsync(gameId, at, cancellationToken);

        if (restored)
        {
            logger?.LogInformation(
                "{Host}:{Port} answered again and is out of the archive", result.Host, result.Port);
        }

        // §5.7, and last of everything this probe does. After the reconciliation because it reads the
        // winning NAME that reconciliation just confirmed; after the two writes above because it is
        // the only step here that can fail on a precondition rather than on the database being gone —
        // two games settling on one name race at the unique index — and a URL that could not be
        // re-minted this cycle must never cost a measurement that was already made. It re-mints
        // nothing until a name has held for a grace period, so the common case is a read and no write.
        var renamed = await slugs.ConsiderAsync(gameId, at, cancellationToken);

        return new Ingestion(
            reachability, written, reading.Source, reading.Count, reconciliation, restored, renamed);
    }

    private async Task<Ingestion> FailedAsync(
        Guid gameId,
        ProbeResult result,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        var (state, cause) = FailureReading.Of(result);

        // The message travels with the cause. FailureReading collapses a dozen socket errors into
        // six words on purpose — the site reasons about those six — and this is what was underneath.
        var reachability = await availability.ObserveAsync(
            gameId, state, cause, at, result.Failure?.Detail, cancellationToken);

        // No presence row, no field reconciliation, no last_reachable_at, no un-archiving. The
        // availability series carries this hour on its own, and the heatmap's empty cell IS that
        // absence — a row here would fill or hatch a cell nothing measured.
        return new Ingestion(reachability, Presence: null, Source: null, Count: null,
            Fields: FieldReconciliation.Nothing, Restored: false);
    }
}

/// <summary>What one probe wrote (spec §5.2, §5.3, §5.4), so a caller can assert on it.</summary>
/// <remarks>
/// <see cref="Presence"/> is null exactly when the probe failed, which is the third of §5.4's three
/// states — the one expressed as the absence of a row rather than as a value in one.
/// </remarks>
public sealed record Ingestion(
    AvailabilityOutcome Availability,
    PresenceOutcome? Presence,
    FieldSource? Source,
    int? Count,
    FieldReconciliation Fields,
    bool Restored,
    Rename? Renamed = null)
{
    /// <summary>Whether a count was stored. False for a hatched cell and for a failed probe alike.</summary>
    public bool Counted => Presence is PresenceOutcome.Counted;

    /// <summary>
    /// How busy the game looked, as the scheduler reads it (spec §7.7).
    /// </summary>
    /// <remarks>
    /// <c>Unknown</c> for an unmeasurable count, never <c>Quiet</c>: parsers never fabricate and
    /// neither does the schedule, so a game we could not count is not a game with nobody on it.
    /// </remarks>
    public SchedulerBand Activity => (Presence, Count) switch
    {
        (PresenceOutcome.Counted, > 0) => SchedulerBand.Busy,
        (PresenceOutcome.Counted, 0) => SchedulerBand.Quiet,
        _ => SchedulerBand.Unknown,
    };
}

/// <summary>
/// The availability state and cause a failed probe means (spec §5.3).
/// </summary>
/// <remarks>
/// <para>
/// <b><c>degraded</c> is "we got in and could not finish"</b> — the socket answered and the session
/// did not complete within the probe budget. It matters because it is not darkness: §7.5's grace
/// clock does not run against a server that picks up the phone and then falls silent, and
/// <see cref="ArchiveSweeper"/> checks for exactly that before archiving anything.
/// </para>
/// <para>
/// <b>The evidence for it is that a telnet option was negotiated before the budget ran out.</b> An
/// option only fires when the far end spoke telnet back, so a timeout with options observed is a
/// session that started and did not end; a timeout with none is a host that never got that far.
/// <b>The known limitation, and what is left of it:</b> <c>TelnetProbe</c> carries no banner on its
/// failure path, so a server that sends forty lines of connect screen and never negotiates anything
/// reads as unreachable rather than degraded. The half of that which was doing real damage is closed
/// — a server that <em>hangs up</em> mid-session now finishes as <see cref="ProbeOutcome.Answered"/>
/// carrying what it said, which is every DIKU descendant meeting the probe's own flush line. What
/// remains is the budget-expiry case, and closing it means the probe surfacing what it had when the
/// budget expired; it is still not something to paper over here by guessing.
/// </para>
/// </remarks>
public static class FailureReading
{
    public static (AvailabilityState State, FailureCause Cause) Of(ProbeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var cause = CauseOf(result.Failure?.Cause);

        return cause is FailureCause.Timeout && result.OfferedOptions.Count > 0
            ? (AvailabilityState.Degraded, FailureCause.HandshakeStalled)
            : (AvailabilityState.Unreachable, cause);
    }

    /// <summary>
    /// <c>DialFailure.Classify</c>'s vocabulary, read back into the schema's.
    /// </summary>
    /// <remarks>
    /// <see cref="DialFailureCause.Error"/> — <c>DialFailure.Classify</c>'s own catch-all, for a real
    /// dial failure with no more specific name in its vocabulary — becomes
    /// <see cref="FailureCause.Timeout"/> rather than a made-up specific cause: the closest honest
    /// reading of "the dial did not complete and we do not know why", and the raw detail is logged at
    /// the call site. It is emphatically not <see cref="FailureCause.Refused"/>, which means the far
    /// end sent an RST and is a measurement in its own right.
    /// <para>
    /// Unlike the string this replaced, a value that is none of <see cref="DialFailureCause"/>'s named
    /// members cannot arrive here silently — the switch names every member explicitly, and the
    /// discard arm throws rather than defaulting to <see cref="FailureCause.Timeout"/>, so a cause
    /// added to <c>DialFailure.Classify</c> without a matching arm here fails loudly instead of being
    /// misfiled as a measured game timeout.
    /// </para>
    /// </remarks>
    private static FailureCause CauseOf(DialFailureCause? cause) => cause switch
    {
        null => FailureCause.Timeout,
        DialFailureCause.Dns => FailureCause.Dns,
        DialFailureCause.Refused => FailureCause.Refused,
        DialFailureCause.Timeout => FailureCause.Timeout,
        DialFailureCause.NoRoute => FailureCause.NoRoute,
        DialFailureCause.Error => FailureCause.Timeout,
        _ => throw new ArgumentOutOfRangeException(
            nameof(cause), cause, "an unrecognised DialFailureCause reached ProbeIngestor.CauseOf"),
    };
}
