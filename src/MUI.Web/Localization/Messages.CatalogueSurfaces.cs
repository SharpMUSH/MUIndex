namespace MUI.Web.Localization;

public static partial class Messages
{
    /// <summary>
    /// THE CATALOGUE SURFACES — /ecosystem, /rankings and /archive. Each id is a whole claim with
    /// its numbers as arguments (not assembled from fragments), count and set before percentage, so
    /// a translator never has to reorder them.
    /// </summary>
    private static Dictionary<string, string> CatalogueSurfaces() => new(StringComparer.Ordinal)
    {
        // THE CATALOGUE SURFACES — /ecosystem, /rankings, /archive and /find.
        // Each id is a whole claim with its numbers as arguments (not assembled from fragments),
        // count and set before percentage, so a translator never has to reorder them.
        // ═════════════════════════════════════════════════════════════════════════════════════

        // ── the ecosystem dashboard: shares, never totals, and never a share without its set ──
        // §15.7 withholds an absolute population figure; every id here carries its denominator in
        // the same string as the percentage.
        ["ecosystem.title"] = "The ecosystem",
        ["ecosystem.noTotals"] = "We publish the share, a ratio over the games we measured survives the ones we cannot reach.",

        // {value} is the drawn numeral, passed as an argument rather than embedded in markup.
        ["ecosystem.listed"] = "{count, plural, one {{value} game listed} other {{value} games listed}}",
        ["ecosystem.handshakes"] = "{count, plural, one {{value} game whose handshake we completed} other {{value} games whose handshake we completed}}",
        ["ecosystem.msspReports"] = "{count, plural, one {{value} game whose MSSP report we hold} other {{value} games whose MSSP report we hold}}",
        ["ecosystem.oldestHandshake"] = "Oldest handshake here: confirmed {age} ago.",

        // Count, set, then percentage — an empty denominator is "nothing measured", not 0%.
        ["ecosystem.share"] = "{count, number} of {total, number} ({fraction, number, ::percent .0})",
        ["ecosystem.share.nothing"] = "{count, number} of {total, number} — nothing measured yet",
        // The percentage alone, for the one cell whose denominator is its column head.
        ["ecosystem.share.percent"] = "{fraction, number, ::percent .0}",

        ["ecosystem.codebases.title"] = "Codebases",
        // Both numbers: identified count must not be mistaken for total listed games.
        ["ecosystem.codebases.basis"] = "Of the {listed, plural, one {# game} other {# games}} listed, {identified, number} told us what they run, and every share below is over those {identified, number}. " +
                                        "A codebase we could not read is left out of the denominator",
        ["ecosystem.codebases.none"] = "No listed game has told us its codebase yet.",
        ["ecosystem.soleUse"] = "{share} run a codebase no other listed game runs — one game each, which is assumed to be just that game. They are inside the denominator above:",

        ["ecosystem.lineages.title"] = "Lineages",
        ["ecosystem.lineages.basis"] = "The same games, grouped by the tradition their server descends from — our reading of the codebase, not anything a game published. " +
                                       "No game reports \"MUSH\": MSSP has no such value, and most of the MUSH world publishes no MSSP at all, so this is the only way the question can be asked.",
        ["ecosystem.lineages.none"] = "No listed game runs a codebase we place in a lineage yet.",
        ["ecosystem.lineages.notClassified"] = "{count, plural, one {# of those games runs a codebase} other {# of those games run codebases}} we do not place in any lineage — several say as much themselves, publishing {family}. " +
                                               "They are inside the denominator above and in nobody's share.",

        ["ecosystem.protocols.title"] = "Protocols",
        ["ecosystem.protocols.floor"] = "Read every measured figure below as a floor. " +
                                        "Nothing else here is requested, and a server may support a protocol without ever offering it.",
        ["ecosystem.mssp.instrument"] = "{instrument} is the one row below that is not a floor: we ask every server " +
                                        "for it by name, so the games that did not offer it were asked and declined.",
        ["ecosystem.mssp.gap"] = "We hold {reports, number} reports and {offered, plural, one {# game} other {# games}} offer MSSP today: " +
                                 "the other {gap, number} stopped publishing one after we read it, and a report is not thrown away " +
                                 "because it stopped being reissued.",
        ["ecosystem.protocols.caption"] = "Protocol adoption. 'Measured' is what a server offered in a completed handshake; " +
                                          "'declared' is what its MSSP claims.",
        ["ecosystem.column.protocol"] = "protocol",
        ["ecosystem.column.measured"] = "measured — of {basis}",
        ["ecosystem.column.declared"] = "declared — of {basis}",

        // Four whole sentences, not a share with clauses bolted on — a concatenated fragment can't
        // be reordered by a translator.
        ["ecosystem.measured.never"] = "not measured — never observed",
        ["ecosystem.measured.declined"] = "{share} · {declined, plural, one {# game} other {# games}} declined when asked",
        ["ecosystem.measured.unasked"] = "{share} · {unobserved, plural, one {# game} other {# games}} neither offered nor asked",
        ["ecosystem.measured.declinedAndUnasked"] = "{share} · {declined, plural, one {# game} other {# games}} declined when asked · {unobserved, plural, one {# game} other {# games}} neither offered nor asked",
        ["ecosystem.declared.none"] = "not asked — every report here is the answer",

        ["ecosystem.curve.title"] = "Adoption over time",
        ["ecosystem.curve.caveat"] = "Each point is a share over the games we had measured that day, so this line moves for two reasons: a game changing what it offers, and the set of games we can measure changing around it. Only the first is adoption. The transition count below is the part that is purely games changing their minds.",
        ["ecosystem.curve.caption"] = "Measured share of each protocol, oldest reading first",
        ["ecosystem.curve.then"] = "then",
        ["ecosystem.curve.now"] = "now",
        ["ecosystem.curve.notMeasured"] = "not measured",
        ["ecosystem.snapshot.title"] = "A snapshot, not a curve",
        ["ecosystem.snapshot"] = "A snapshot of what we can measure now. An adoption curve plots games changing their minds, and we record a change when it happens, so the curve becomes drawable once enough have been recorded. Plotting when we first reached each game would measure the crawl, not the hobby.",
        ["ecosystem.transitions"] = "{count, plural, one {# capability change} other {# capability changes}} recorded so far — the material a curve is drawn from.",
        ["ecosystem.transitions.none"] = "No measured capability has changed yet, so there is nothing to plot.",

        ["ecosystem.plain.denominators"] = "Measured is of {measured}; declared is of {declared}. Two sets of games, so two denominators.",
        ["ecosystem.plain.counts"] = "{listed} · {handshakes} · {mssp}.",
        ["ecosystem.plain.oldestHandshake"] = "The oldest handshake in this picture was last confirmed {age} ago.",
        ["ecosystem.plain.lineages"] = "The same games, grouped by the tradition their server descends from. This is {evidence} — {meaning} — and not anything a game published: no game reports \"MUSH\", " +
                                       "because MSSP has no such value and most of the MUSH world publishes no MSSP at all.",
        ["ecosystem.plain.measured"] = "measured: {value}",
        ["ecosystem.plain.declared"] = "declared: {value}",

        // ── the rankings: computed from measured data only, and it says so first ──────────────
        // §2 rules out votes/stars/ratings permanently; this claim must survive translation exactly.
        ["rankings.title"] = "Rankings",
        ["rankings.noVote"] = "Computed from measured data only. No votes, stars or ratings, ever. Nothing here ranks quality. We have not measured it.",
        ["rankings.busiest.title"] = "Busiest, by measured concurrent players",
        ["rankings.window.label"] = "Ranking window",
        // A ranking window and a sort window are different jobs — not window.7 under another name.
        ["rankings.span"] = "{days, plural, one {# day} other {# days}}",

        ["rankings.basis.median"] = "Median of the player counts we measured over the last {days, plural, one {# day} other {# days}}.",
        ["rankings.basis.none"] = "No game yet has the {samples, number} samples across {days, plural, one {# day} other {# days}} that a median needs.",
        ["rankings.basis.eligible"] = "{eligible, plural, one {# game} other {# games}} of {listed, number} have the {samples, number} samples across {days, plural, one {# day} other {# days}} it needs.",
        // Rule 4, on the surface where a zero is most likely to be read as an absence.
        ["rankings.basis.zero"] = "A measured zero counts; an unreadable count does not.",
        ["rankings.spanChoice"] = "A week says who is busy now; a quarter says who has been busy. They are different questions and a game can lead one and not the other. Days are whole days, UTC.",
        ["rankings.busiest.empty"] = "No listed game has enough counted samples to rank yet — a statement about how long we have been measuring, not about how busy anybody is.",
        ["rankings.busiest.caption"] = "Games ranked by the median of the player counts we measured over the last {days, plural, one {# day} other {# days}}. Games on the same median share a place; nothing here breaks the tie.",
        ["rankings.column.place"] = "#",
        ["rankings.column.game"] = "game",
        ["rankings.column.median"] = "median",
        ["rankings.column.peak"] = "peak",
        ["rankings.column.samples"] = "counted samples",
        ["rankings.column.days"] = "days measured",

        ["rankings.trending.title"] = "Trending",
        ["rankings.trending.basis"] = "A line fitted through each game's own daily measured median over its last 14 days, among games with at least 3 days that had enough samples for a median — always this fitted trend, whichever ranking window is shown above.",
        ["rankings.trending.empty"] = "No listed game is trending up — a statement about how many games clear the sample floor on enough days, not about the hobby.",
        ["rankings.trending.caption"] = "Games whose fitted trend over their last 14 measured days rises enough to call it a rise rather than noise, each with at least 3 days that had enough samples for a median.",
        ["rankings.column.latest"] = "latest",
        ["rankings.column.earliest"] = "earliest",
        ["rankings.column.change"] = "change",

        ["rankings.spells.title"] = "Longest unbroken reachable spell",
        // Reachable, never uptime.
        ["rankings.spells.basis"] = "Every probe since the date given found the game reachable — a socket answered, which is not a claim the game was up throughout. A spell cannot be longer than we have been watching, so the date is the fact and the duration follows.",
        ["rankings.spells.empty"] = "No listed game is in an unbroken reachable spell right now.",
        ["rankings.spells.caption"] = "Games whose every probe since the date given found them reachable. Games reachable since the same date share a place; nothing here breaks the tie.",
        ["rankings.column.since"] = "reachable since",
        ["rankings.column.duration"] = "that is",
        // §7.5 in one sentence: out of these two tables and out of nothing else.
        ["rankings.archivedNote"] = "Archived games are out of both tables and nothing else; one successful probe puts them back.",

        ["rankings.plain.busiest"] = "Busiest — median measured players, last {days, plural, one {# day} other {# days}}",
        ["rankings.plain.windows"] = "windows:",
        ["rankings.plain.thisOne"] = "this one",
        ["rankings.plain.row"] = "median {median, number} · peak {peak, number} · {samples, plural, one {# counted sample} other {# counted samples}} over {days, number} of {window, number} days",
        ["rankings.plain.spellRow"] = "reachable on every probe since {date} · {duration}",
        ["rankings.plain.trending"] = "Trending",
        ["rankings.plain.trendRow"] = "{median, number} latest, {prior, number} earliest (+{players, number})",

        // ── the archive: removed from the default listing, and from nothing else ──────────────
        // §7.5: nothing here is deleted, closed, dead or defunct.
        ["archive.title"] = "The archive",
        ["archive.lede"] = "Games that have stopped answering. Nothing was deleted. Still probed weekly, and one successful probe puts a game back in the listing the same day.",
        ["archive.search.legend"] = "search the archive",
        ["archive.search.label"] = "Search archived games",
        ["archive.search.placeholder"] = "name, codebase or description",
        ["archive.search.submit"] = "show",
        ["archive.count"] = "{count, plural, =0 {No archived games} one {# archived game} other {# archived games}}",
        ["archive.badge"] = "archived",
        ["archive.lastReachable"] = "last reachable",
        ["archive.knownLive"] = "known live",
        ["archive.darkFor"] = "({age} ago)",
        ["archive.empty"] = "Nothing matched.",

        // Never reached is not reached long ago, and no reachable time measured is not a measured
        // zero. Both are gaps in our record and neither may name a cause.
        ["archive.neverReachable"] = "never, in anything we measured",
        ["archive.noReachableTime"] = "no reachable time measured",
        ["archive.darkFor.unknown"] = "unknown",
        ["archive.knownLive.years"] = "{years, number, ::.#} years",
        ["archive.knownLive.days"] = "{days, plural, one {# day} other {# days}}",
        ["archive.run"] = "{from} – {to} · {span}",
        ["archive.run.months"] = "{count, plural, one {# month} other {# months}}",
        ["archive.run.years"] = "{count, plural, one {# year} other {# years}}",

        ["archive.plain.matching"] = "{count, plural, one {# game} other {# games}} matching \"{query}\"",
        ["archive.plain.count"] = "{count, plural, one {# game} other {# games}}",
        ["archive.plain.lastReachable"] = "Last reachable:",
        ["archive.plain.knownLive"] = "Known live:",
        ["archive.plain.knownLiveValue"] = "{value} of measured reachable time",
        ["archive.plain.run"] = "Run:",
        ["archive.plain.codebase"] = "Codebase:",
    };
}
