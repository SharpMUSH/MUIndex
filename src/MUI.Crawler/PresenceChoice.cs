using MUI.Catalog;
using MUI.Crawl;

namespace MUI.Crawler;

/// <summary>
/// Which of a probe's three possible player counts becomes the one presence row (spec §5.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>The choice has to be made here and cannot be deferred.</b> <c>presence_sample</c> is keyed
/// <c>(game_id, at)</c>, so one probe writes at most one row and there is nowhere to keep a WHO count
/// and an MSSP count and decide between them later. §5.2 states the ladder — <c>who</c> outranks
/// <c>mssp</c> — and says in as many words that it "must be applied <em>before</em> the writer is
/// called, by whatever assembles the probe's reading". This is that.
/// </para>
/// <para>
/// The ladder is <c>who</c> → <c>mssp</c> → <c>info</c> → <c>banner</c>, and the last rung is last
/// for a reason the crawler measured: the banner count is pattern-matching a stranger's ASCII art,
/// where a number may be a high score, an uptime or last week's figure left in a static file. It is
/// also the only rung that reaches Aardwolf, which supports no MSSP and answers no pre-login
/// <c>WHO</c> — so it is worth having, and worth having last.
/// </para>
/// <para>
/// <b><c>info</c> is third, and it is third on purpose rather than on merit.</b> In PennMUSH the
/// <c>Connected:</c> line of the <c>INFO</c> block and MSSP's <c>PLAYERS</c> come out of the same
/// <c>count_players()</c> call, so the two are equal-trust and nothing about accuracy separates them.
/// What separates them is that <c>mssp</c> is already recorded against live games: placing the new
/// rung second would silently relabel the source of counts this site has been publishing, and a rung
/// added below cannot change a single value that measures today. Its whole effect is on rows that
/// would otherwise have been NULL — the MUSH-family games with no MSSP and a <c>DOING</c> header past
/// our WHO parser, which were being recorded unmeasurable with their exact count sitting in the
/// probe's own payload.
/// </para>
/// <para>
/// <b>Last on this ladder and still measured</b> (§5.1, <c>FieldSources</c>). The two rankings are
/// different questions: which count to keep when several are available, and whether the one we kept
/// was read by us or reported to us. A banner count is the least trustworthy <em>choice</em> and is
/// nonetheless text we parsed off the socket this probe, so it is labelled as the observation it is
/// — while <c>mssp</c>, which outranks it here, is the game's own report and is labelled declared.
/// <c>info</c> outranks <c>banner</c> and is labelled declared beside <c>mssp</c>, which is the same
/// disagreement in the same direction: what arrives on a generated <c>Label: value</c> line is the
/// game reporting itself, whoever opened the socket. Nothing about the ordering is an argument for
/// relabelling any of them.
/// </para>
/// <para>
/// <b>Nothing here ever returns zero for a source that failed.</b> Every exit that could not obtain a
/// number returns an unmeasurable reading with a reason, which is §5.4's hatched cell. A measured zero
/// — we got in and nobody was there — is a filled cell and reaches this code only because a parser
/// genuinely read a zero.
/// </para>
/// </remarks>
public static class PresenceChoice
{
    /// <summary>The MSSP variable a game states its own player count in.</summary>
    public const string PlayersVariable = "PLAYERS";

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

        // 1. WHO, which outranks MSSP because it is live rather than whatever the codebase last
        //    cached. HasCount is false for an unreadable answer, so an unparseable DOING header can
        //    never arrive here as a zero.
        if (result.Who is { HasCount: true, Count: { } counted })
        {
            return PresenceReading.Counted(counted, FieldSource.Who);
        }

        // 2. MSSP PLAYERS, labelled as the declaration it is.
        var declared = result.MsspField(PlayersVariable);
        if (declared is not null && int.TryParse(declared.Trim(), out var stated) && stated >= 0)
        {
            return PresenceReading.Counted(stated, FieldSource.Mssp);
        }

        // 3. The INFO block's own Connected: line, which is the same kind of statement as MSSP
        //    PLAYERS and is read only where MSSP offered none. Delimited-block only: see
        //    LoginCommandReading.ConnectedPlayers for why the markers are the whole defence.
        if (LoginCommandReading.ConnectedPlayers(result.Info) is { } fromInfo)
        {
            return PresenceReading.Counted(fromInfo, FieldSource.Info);
        }

        // 4. The connect screen, if it stated a count about itself.
        if (result.BannerPlayerCount is { } fromBanner)
        {
            return PresenceReading.Counted(fromBanner, FieldSource.Banner);
        }

        return PresenceReading.Unmeasurable(ReasonFor(result, declared));
    }

    /// <summary>
    /// Why no count was obtainable. Four reasons, and they name four different problems: a game that
    /// answers no <c>WHO</c> at all, a game whose login prompt ate the word, a <c>WHO</c> our parser
    /// could not read, and an MSSP <c>PLAYERS</c> that was not a number.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reason is what tells a maintainer which of our two problems a hatched cell is, so it is
    /// picked from the strongest evidence rather than defaulted. A non-numeric <c>PLAYERS</c> outranks
    /// the WHO reasons because it is a fact about the game's own report — the one place a server said
    /// something and we could not use it.
    /// </para>
    /// <para>
    /// <b><c>who_login_prompt</c> is the one of the four that is not ours</b>, and it was the largest
    /// population inside <c>who_unparseable</c> until it was given a word of its own: 43 of the 107
    /// payloads stored on 2026-08-17. See <see cref="UnmeasurableReason.WhoLoginPrompt"/> for what the
    /// two were being read as, and migration 0026 for why no existing row is rewritten.
    /// </para>
    /// <para>
    /// <b>The <c>info</c> rung adds no fourth reason, and that is a finding rather than an omission.</b>
    /// The overwhelming case for a game that reached here is that it printed no <c>INFO</c> block at
    /// all — every DIKU, LP and MOO on the crawl, which is most of it — and "this game published no
    /// machine-readable INFO" is not a problem the way an unreadable <c>WHO</c> is. It is the ordinary
    /// state of the hobby, and naming it would put a fourth word on the heatmap that separates no two
    /// games a maintainer could act on. The three reasons still name the three things that went wrong:
    /// we never asked, we asked and could not read the answer, and the game's own figure was not a
    /// number. A block whose <c>Connected:</c> line is unreadable is rare enough that it has never been
    /// observed, and inventing a schema vocabulary entry for it now would be guessing at what a
    /// codebase does — which is what <c>docs/codebase-survey-2026-07-30.md</c> exists to stop.
    /// </para>
    /// </remarks>
    private static UnmeasurableReason ReasonFor(ProbeResult result, string? declaredPlayers) =>
        declaredPlayers is not null ? UnmeasurableReason.PlayersNotNumeric
        : result.Who.Confidence is WhoConfidence.LoginPrompt ? UnmeasurableReason.WhoLoginPrompt
        : result.Who.Attempted ? UnmeasurableReason.WhoUnparseable
        : UnmeasurableReason.WhoNotOffered;
}
