using MUI.Catalog;
using MUI.Crawl;

namespace MUI.Crawler;

/// <summary>
/// Which of a probe's possible player counts becomes the one presence row (spec §5.2).
/// </summary>
/// <remarks>
/// The choice has to be made here and cannot be deferred: <c>presence_sample</c> is keyed
/// <c>(game_id, at)</c>, so one probe writes at most one row. §5.2 states the ladder — <c>who</c>
/// outranks <c>mssp</c> outranks <c>info</c> outranks <c>mssp_roster</c> outranks <c>banner</c> — and
/// requires it be applied before the writer is called; this is that. The banner rung is last because it's pattern-matching a
/// stranger's ASCII art (a number there could be a high score or a stale file), kept only because it's
/// the sole rung that reaches Aardwolf-like games with no MSSP and no pre-login WHO. <c>info</c> ranks
/// below <c>mssp</c> not on accuracy (PennMUSH's <c>Connected:</c> line and MSSP's <c>PLAYERS</c> come
/// from the same call) but so a new rung can't silently relabel counts already published under
/// <c>mssp</c> — its effect is only on rows that would otherwise be NULL. <c>mssp_roster</c> is held
/// to the same rule and sits below <c>info</c> for a reason of its own: it is a count of a list the
/// game published rather than a number it stated, and a roster leaves out whoever the game does not
/// show, so it is a floor (see <see cref="FieldSource.MsspRoster"/>). This ladder (which count to
/// keep) is a different ranking from <c>FieldSources</c> (§5.1: whether a value was read by us or
/// reported to us) — a banner count is the least trusted choice but is still text we parsed ourselves,
/// so it's labelled an observation, while <c>mssp</c> and <c>info</c> are the game's own report and
/// labelled declared. Nothing here ever returns zero for a source that failed: every exit that
/// couldn't obtain a number returns an unmeasurable reading with a reason (§5.4's hatched cell); a
/// measured zero reaches this code only because a parser genuinely read one.
/// </remarks>
public static class PresenceChoice
{
    /// <summary>The MSSP variable a game states its own player count in.</summary>
    public const string PlayersVariable = MsspPresence.PlayersVariable;

    /// <summary>
    /// The one reading this probe produces. Only ever called for a probe that <b>answered</b>: a
    /// failed probe writes no presence row at all, which is the third of §5.4's three states and is
    /// the absence of a row rather than a value.
    /// </summary>
    public static PresenceReading From(ProbeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Outcome is not ProbeOutcome.Answered)
        {
            throw new ArgumentException(
                "A failed probe has no presence reading: §5.4's third state is the absence of a row, "
                + "and the availability series carries it instead.",
                nameof(result));
        }

        // 1. WHO, which outranks MSSP because it's live rather than whatever the codebase last
        //    cached. HasCount is false for an unreadable answer, so an unparseable DOING header can
        //    never arrive here as a zero.
        if (result.Who is { HasCount: true, Count: { } counted })
        {
            return PresenceReading.Counted(counted, FieldSource.Who);
        }

        // 2. MSSP PLAYERS, labelled as the declaration it is. Read through MsspPresence rather than
        //    parsed here, because the probe consults the same reader to decide whether WHO still
        //    needs asking — a value accepted there and refused here would mean a probe that stayed
        //    quiet and then had nothing to show for it.
        if (MsspPresence.Stated(result.Mssp) is { Found: true } stated)
        {
            return PresenceReading.Counted(stated.Count, FieldSource.Mssp);
        }

        // 3. The INFO block's own Connected: line, which is the same kind of statement as MSSP
        //    PLAYERS and is read only where MSSP offered none. Delimited-block only: see
        //    LoginCommandReading.ConnectedPlayers for why the markers are the whole defence.
        if (LoginCommandReading.ConnectedPlayers(result.Info) is { } fromInfo)
        {
            return PresenceReading.Counted(fromInfo, FieldSource.Info);
        }

        // 4. A roster the report published and we counted, under its own source because it is a
        //    floor rather than a total (see FieldSource.MsspRoster). Below `info` on 0019's rule —
        //    a new rung fills NULL rows and never relabels a published one — which in today's
        //    catalogue means it fills none at all: every game publishing a roster also states
        //    PLAYERS or is counted over I3. It is here so that a game whose stated count goes
        //    missing still has somewhere honest for its answer to land, which is what lets the probe
        //    stop typing WHO at it.
        if (MsspPresence.Roster(result.Mssp) is { Found: true } roster)
        {
            return PresenceReading.Counted(roster.Count, FieldSource.MsspRoster);
        }

        // 5. The connect screen, if it stated a count about itself.
        if (result.BannerPlayerCount is { } fromBanner)
        {
            return PresenceReading.Counted(fromBanner, FieldSource.Banner);
        }

        return PresenceReading.Unmeasurable(ReasonFor(result));
    }

    /// <summary>
    /// Why no count was obtainable. Four reasons, and they name four different problems: a game that
    /// answers no <c>WHO</c> at all, a game whose login prompt ate the word, a <c>WHO</c> our parser
    /// could not read, and an MSSP <c>PLAYERS</c> that was not a number.
    /// </summary>
    /// <remarks>
    /// Picked from the strongest evidence rather than defaulted: a non-numeric <c>PLAYERS</c> outranks
    /// the WHO reasons because it's a fact about the game's own report. <c>who_login_prompt</c> is the
    /// one of the four that isn't ours — see <see cref="UnmeasurableReason.WhoLoginPrompt"/>. The
    /// <c>info</c> rung deliberately adds no fourth reason: most games here simply printed no
    /// <c>INFO</c> block at all, which is the ordinary state of the hobby rather than a problem the way
    /// an unreadable <c>WHO</c> is, and inventing a vocabulary entry for an unobserved case would be
    /// guessing at what a codebase does.
    /// </remarks>
    private static UnmeasurableReason ReasonFor(ProbeResult result) =>
        result.MsspField(PlayersVariable) is not null ? UnmeasurableReason.PlayersNotNumeric
        : result.Who.Confidence is WhoConfidence.LoginPrompt ? UnmeasurableReason.WhoLoginPrompt
        : result.Who.Attempted ? UnmeasurableReason.WhoUnparseable
        : UnmeasurableReason.WhoNotOffered;
}
