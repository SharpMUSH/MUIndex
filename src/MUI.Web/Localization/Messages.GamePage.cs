namespace MUI.Web.Localization;

public static partial class Messages
{
    /// <summary>
    /// THE TREND CHART, THE REACHABILITY STRIP AND THE REST OF THE GAME PAGE — the trend chart's
    /// per-day titles and chrome, the reachability strip and its causes, the ANSI capture's frame,
    /// the capability matrix's disagreements said in prose, the rest of the game page (links, plain
    /// surface headings, listing row headers), the crawler strip and status page, the document
    /// preview/meta tags, and the plain mirror's feed headings and home counts.
    /// </summary>
    /// <remarks>
    /// A day has the same three states an hour has (rule 2): counted (incl. measured zero),
    /// probed-no-count, and not measured. <c>trend.day.notMeasured</c>/<c>notCounted</c> are
    /// deliberately far apart in wording so no locale can render both as "unavailable"; a test
    /// enforces it. Dates are <c>{d, date, …}</c> arguments, never a month name written here, so a
    /// translator can pick their own date style.
    /// </remarks>
    private static Dictionary<string, string> GamePage() => new(StringComparer.Ordinal)
    {
        // THE TREND CHART, THE REACHABILITY STRIP AND THE REST OF THE GAME PAGE
        // ═════════════════════════════════════════════════════════════════════════════════════
        // A day has the same three states an hour has (rule 2): counted (incl. measured zero),
        // probed-no-count, and not measured. `trend.day.notMeasured`/`notCounted` are deliberately
        // far apart in wording so no locale can render both as "unavailable"; a test enforces it.
        //
        // Dates are `{d, date, …}` arguments, never a month name written here, so a translator can
        // pick their own date style.

        // ── the trend chart's per-day title, one per column ───────────────────────────────────
        // A flat day (every probe the same number) is its own id, not a "24–24" range.
        ["trend.day.counted"] = "{d, date, medium} — {typical} on average, {low}–{high} across {probes, plural, one {# probe} other {# probes}}",
        ["trend.day.flat"] = "{d, date, medium} — {count, plural, =0 {0 players} one {# player} other {# players}}, every one of {probes, plural, one {# probe} other {# probes}}",
        ["trend.day.notCounted"] = "{d, date, medium} — probed, no count could be read",
        ["trend.day.notMeasured"] = "{d, date, medium} — no measurement",

        // ── the sentence above the chart ──────────────────────────────────────────────────────
        ["trend.summary"] = "Typically {typical} on, peaking at {peak}, over {counted} of {days, plural, one {# day} other {# days}}.",
        ["trend.direction.steady"] = "Steady across the range.",
        ["trend.direction.up"] = "Up about {change, number, percent} from the start of the range to the end.",
        ["trend.direction.down"] = "Down about {change, number, percent} from the start of the range to the end.",

        ["trend.none.probed"] = "Probed in this range, and no player count could be read from any of it.",
        ["trend.none.notMeasured"] = "No measurement in this range.",
        ["trend.empty"] = "Nothing counted in this range.",

        // ── the week lines, which are the text alternative and the plain surface both ─────────
        ["trend.week.span"] = "{from, date, d MMM}–{to, date, d MMM}",
        ["trend.week.oneDay"] = "{d, date, d MMM}",
        ["trend.week.counted"] = "{span}: typically {typical}, peak {peak}, {days, plural, one {# day} other {# days}} counted",
        ["trend.week.notCounted"] = "{span}: probed, no count could be read",
        ["trend.week.notMeasured"] = "{span}: not measured",
        ["trend.week.uncounted"] = "{count, plural, one {# day probed without a count} other {# days probed without a count}}",
        ["trend.week.unmeasured"] = "{count, plural, one {# day not measured} other {# days not measured}}",
        ["trend.week.and"] = "{line}, {clause}",

        // ── the chart's own chrome ────────────────────────────────────────────────────────────
        ["trend.range"] = "days in UTC · {from, date, medium} – {to, date, medium}",
        ["trend.ceiling"] = "{value} at the top",
        ["trend.counted"] = "{counted} of {days, plural, one {# day} other {# days}} counted",
        ["trend.axis.month"] = "{d, date, MMM}",
        ["trend.axis.monthYear"] = "{d, date, MMM yyyy}",

        ["trend.legend.mean"] = "mean of the counts we read that day",
        ["trend.legend.peak"] = "up to the busiest count that day",
        ["trend.legend.band"] = "lowest to highest count that day",
        ["trend.legend.notCounted"] = "probed, no count could be read",
        ["trend.legend.notMeasured.bar"] = "not measured — no bar at all",
        ["trend.legend.notMeasured.line"] = "not measured — a break in the line",

        // ── seeking and switching, which are links because the range is in the address ────────
        ["trend.spans.label"] = "Trend range",
        ["trend.shapes.label"] = "Trend shape",
        ["trend.preset"] = "{days, plural, one {# day} other {# days}}",
        ["trend.earlier"] = "← earlier",
        ["trend.later"] = "later →",
        ["trend.shape.line"] = "line",
        ["trend.shape.bar"] = "bars",
        ["trend.plain.heading"] = "How many, over time",
        ["trend.plain.range"] = "{from, date, medium} – {to, date, medium}, UTC",
        ["trend.plain.earlier"] = "earlier",
        ["trend.plain.note"] = "a week is summarised over the days in it we counted; a week with none says so",

        // ── the reachability strip ────────────────────────────────────────────────────────────
        // Reachable, never up (spec §5.8) — a locked id in four registers since they decline
        // differently in most languages.
        ["reach.kicker"] = "reachable · last {days, plural, one {# day} other {# days}}",
        ["reach.stat.reachable"] = "reachable",
        ["reach.stat.longestOutage"] = "longest outage",
        ["reach.stat.lastCause"] = "last cause",
        ["reach.noOutage"] = "none in the window",
        ["reach.noCause"] = "nothing recorded",
        ["reach.scale.ago"] = "{days, plural, one {# day ago} other {# days ago}}",
        ["reach.scale.today"] = "today",
        ["reach.legend.reachable"] = "reachable",
        ["reach.legend.degraded"] = "degraded — answered, could not finish",
        ["reach.legend.unreachable"] = "unreachable",
        ["reach.legend.notMeasured"] = "not measured",

        // The state as a noun, for the middle of a spell — "3 days unreachable".
        ["reach.word.reachable"] = "reachable",
        ["reach.word.degraded"] = "degraded",
        ["reach.word.unreachable"] = "unreachable",
        ["reach.word.notMeasured"] = "not measured",

        // Ninety of these are drawn per game. The fourth is the one that matters: a day before we
        // knew the game existed is not a day it was down, and it says so about us rather than about
        // them.
        ["reach.day.reachable"] = "{d, date, d MMM} — reachable all day",
        ["reach.day.degraded"] = "{d, date, d MMM} — degraded ({cause}): answered, could not finish",
        ["reach.day.unreachable"] = "{d, date, d MMM} — unreachable ({cause})",
        ["reach.day.notMeasured"] = "{d, date, d MMM} — not measured; we were not watching this game yet",

        // Denominator is observed time, not the window — a game found an hour ago must not read
        // "Reachable 100.0% of the last 90 days".
        ["reach.none"] = "{days, plural, one {Not yet measured over the last # day.} other {Not yet measured over the last # days.}}",
        ["reach.fraction.window"] = "Reachable {percent} of the last {days, plural, one {# day} other {# days}}.",
        ["reach.fraction.measured"] = "Reachable {percent} of the {days, plural, one {# day} other {# days}} we have measured.",
        ["reach.fraction.unknown"] = "{days, plural, one {Reachability over the last # day is not yet measured.} other {Reachability over the last # days is not yet measured.}}",
        ["reach.unreachable.noneInWindow"] = "No day in the window was unreachable.",
        ["reach.unreachable.noneMeasured"] = "No day we measured was unreachable.",
        ["reach.unreachable.days"] = "{count, plural, one {# day unreachable.} other {# days unreachable.}}",
        ["reach.degraded.days"] = "{count, plural, one {# day degraded — we got in and could not finish.} other {# days degraded — we got in and could not finish.}}",
        ["reach.longestOutage"] = "Longest outage {duration}.",
        ["reach.longestOutage.cause"] = "Longest outage {duration} ({cause}).",
        // "gap" (we stopped watching) vs "predate" (before we started) — an unwatched day is ours,
        // never theirs, and no cause is given for either.
        ["reach.gap"] = "{count, plural, one {# day went unmeasured — we were not watching.} other {# days went unmeasured — we were not watching.}}",
        ["reach.predate"] = "{count, plural, one {# day predates anything we measured.} other {# days predate anything we measured.}}",
        ["reach.spell.range"] = "{from, date, d MMM} – {to, date, d MMM}",
        ["reach.spell.oneDay"] = "{d, date, d MMM}",
        ["reach.spell"] = "{range}: {count, plural, one {# day} other {# days}} {word}",
        ["reach.spell.cause"] = "{range}: {count, plural, one {# day} other {# days}} {word} ({cause})",
        ["reach.plain.heading"] = "Reachable",
        ["reach.plain.window"] = "{days, plural, one {last # day} other {last # days}}",
        ["reach.plain.fraction"] = "Reachable: {percent} of the {days, plural, one {last # day} other {last # days}}",
        ["reach.plain.longestOutage"] = "Longest outage: {duration}",

        // ── why a dial did not complete, in a person's words ──────────────────────────────────
        // A cause names the event our socket saw, never a judgement of the game.
        ["cause.dns"] = "dns did not resolve",
        ["cause.refused"] = "connection refused",
        ["cause.tls"] = "tls failed",
        ["cause.timeout"] = "timed out",
        ["cause.handshakeStalled"] = "handshake stalled",

        // "from here" names our vantage point — reachable is measured from one host, never uptime.
        ["cause.noRoute"] = "no route from here",
        ["cause.none"] = "no cause recorded",

        // ── the ANSI capture's frame ──────────────────────────────────────────────────────────
        // Chrome around the screen, never the screen — the <pre> content is the server's bytes, not
        // a bundle string. "read as" not "encoded in": a game can declare one charset and send another.
        ["ansi.suppressed"] = "The owner asked us not to republish this game's connect screen.",
        ["ansi.absent"] = "No connect screen has been captured from this game.",
        ["ansi.tooSmall"] = "{count, plural, one {Only # row came back — too little to show.} other {Only # rows came back — too little to show.}}",
        ["ansi.asSent"] = "as sent by the server",
        ["ansi.size"] = "{columns}×{rows}",
        ["ansi.size.doubleWidth"] = "{columns}×{rows}, double-width",
        ["ansi.depth.colour"] = "16-colour SGR",
        ["ansi.depth.plain"] = "no colour",
        ["ansi.readAs"] = "read as {charset}",
        ["ansi.captured"] = "captured",
        ["ansi.frozen"] = "frozen — the last screen we saw",
        ["ansi.alt.named"] = "ASCII art: the connect screen of {game}. Its text is under \"read as text\", below.",
        ["ansi.alt"] = "ASCII art: this game's connect screen. Its text is under \"read as text\", below.",
        ["ansi.plain.rows"] = "{count, plural, one {connect screen: # line, text only} other {connect screen: # lines, text only}}",
        ["ansi.plain.rows.readAs"] = "{count, plural, one {connect screen: # line, text only, read as {charset}} other {connect screen: # lines, text only, read as {charset}}}",

        // ── the capability matrix's disagreements, said in prose ──────────────────────────────
        ["capability.disagree.declaredNotOffered"] = "the game declares it, and the server has never offered it in a handshake.",
        ["capability.disagree.offeredNotDeclared"] = "the server offers it, and the game's own record says it does not.",
        ["capability.disagree.note"] = "Usually a stale hand-typed field, not a lie. Shown because a client should not rely on what the two disagree about.",

        // ── the rest of the game page ─────────────────────────────────────────────────────────
        ["game.notFound"] = "Not found",
        ["game.notFound.hint"] = "No game at this address. Check the spelling.",
        ["game.endpoint.lastAnswered"] = "last answered",
        ["game.stale"] = "{count, plural, one {# is past its refresh window. Old, not wrong.} other {# are past their refresh window. Old, not wrong.}}",
        ["game.referrals.lists"] = "This game's own referral list names:",
        ["game.referrals.listedBy"] = "Named by the referral list of:",
        ["game.referral.since"] = "since {date}",
        ["game.referral.dropped"] = "no longer listed, last seen",

        // "Still probed" is the §7.5 promise, said explicitly rather than implied.
        ["game.archived.lastAnswered"] = "Last answered {date}, {ago} ago.",
        ["game.archived.stillProbed"] = "Still probed weekly; this page updates the day it answers.",
        ["game.archived.knownLive"] = "{span} known live",

        // Exclusion is OURS and prints the argument; unlisting is THEIRS and prints no reason.
        ["game.excluded"] = "Not in the listing, the rankings or the daily figure: we do not think this address is a game somebody can play. Everything below is what it told us, unchanged.",
        ["game.excluded.reason"] = "Our reason: {why}",
        ["game.unlisted"] = "Not in the listing, the rankings or the daily figure, at the request of the people who run it. Everything below is preserved as it was, this page and every address it has ever had go on answering, and we are not dialling it.",

        // ── the plain surface's own headings for this page ────────────────────────────────────
        ["game.plain.playersNow"] = "Players now: {count}",
        // The graphical caption pluralises this and the plain one did not, so one surface said "1 of
        // 6 disagrees" and its own mirror said "1 of 6 disagree" about the same six rows.
        ["game.plain.capabilities"] = "Capabilities ({disagreeing, plural, one {# of {total} disagrees} other {# of {total} disagree}})",
        ["game.plain.declared"] = "Declared by the game",
        ["game.plain.connectScreen"] = "Connect screen",
        ["game.plain.whatChanged"] = "What changed",
        ["game.plain.measured"] = "measured",
        ["game.plain.declared.column"] = "declared",
        ["game.plain.disagree"] = "** disagree",

        // ── the reach row, and the same facts on the plain surface ────────────────────────────
        // The platform's own name is an ARGUMENT, never part of the string — a brand should not be
        // translatable.
        ["links.heading"] = "Where to find this game",
        ["links.to"] = "This game on {platform}",
        ["links.website"] = "Website",
        ["links.wiki"] = "Wiki",
        ["links.forum"] = "Forum",
        ["links.email"] = "Email",
        ["links.plain.heading"] = "Where to find this game",

        // Column heads repeated per row for assistive tech — the visible head stays `aria-hidden`
        // and a screen-reader-only row needs its own "12"/"2h ago" labelled.
        ["listing.row.connected"] = "connected",
        ["listing.row.reached"] = "reached",
        ["listing.row.discovered"] = "discovered",

        ["facet.group.search"] = "search",
        ["facet.group.archived"] = "archived",
        ["facet.group.adult"] = "adult",
        ["facet.value.included"] = "included",

        // Rule 2's third state on the game page's live figure: `PlayersNow` is null for three
        // different situations the page cannot tell apart, so this names the absence, no cause.
        ["game.count.none"] = "no count",
        ["game.count.none.why"] = "no current count, and nothing here says why",
        ["game.plain.playersNoCount"] = "Players now: no count (nothing here says why)",

        // ── the crawler strip ─────────────────────────────────────────────────────────────────
        ["crawler.live"] = "crawler live · last probe {age}",
        ["crawler.quiet"] = "crawler quiet · last probe {age}",
        ["crawler.noProbe"] = "no probe has finished here yet",

        ["crawler.cycle.nothingDue"] = "nothing due this cycle",
        ["crawler.cycle"] = "{considered, plural, one {# due} other {# due}}"
            + " · {answered, plural, one {# answered} other {# answered}}"
            + " · {failed, plural, one {# failed} other {# failed}}",
        ["crawler.registry"] = "{targets, plural, one {# address in the registry} other {# addresses in the registry}}"
            + ", {due, plural, one {# due now} other {# due now}}",

        // ── the crawler status page ───────────────────────────────────────────────────────────
        ["crawler.page.title"] = "The crawler",
        ["crawler.page.lede"] = "MUINDEX-CRAWLER is the automated probe behind every measurement on"
            + " this site — what it has been doing, and how often.",
        ["crawler.page.aboutLink"] = "who is this, and how to opt out",
        ["crawler.page.empty"] = "No crawl cycle has completed here yet.",
        ["crawler.page.statusLabel"] = "status",
        ["crawler.page.registryLabel"] = "registry",
        ["crawler.page.cycleLabel"] = "last cycle",
        ["crawler.cycle.full"] = "{considered, plural, one {# due} other {# due}}"
            + " · {probed, plural, one {# probed} other {# probed}}"
            + " · {answered, plural, one {# answered} other {# answered}}"
            + " · {failed, plural, one {# failed} other {# failed}}"
            + " · {errored, plural, one {# errored} other {# errored}}"
            + " · {optedOut, plural, one {# opted out} other {# opted out}}",
        ["crawler.cycle.finishedAt"] = "last cycle finished {when} · took {took}",
        ["crawler.history.title"] = "recent cycles",
        ["crawler.history.empty"] = "No completed cycles are recorded yet.",
        ["crawler.liveness.title"] = "went dark, came back",
        ["crawler.liveness.lede"] = "The other two liveness feeds — newly discovered stays on the front page.",
        ["crawler.recent.title"] = "recently updated",
        ["crawler.recent.lede"] = "The newest field changes the crawler has written in the last 30 days, across every listed game.",
        ["crawler.recent.empty"] = "No field has changed recently.",
        ["crawler.due.title"] = "next up",
        ["crawler.due.lede"] = "The soonest-due addresses in the registry, whether or not they have resolved to a named game yet.",
        ["crawler.due.empty"] = "Nothing is due.",

        // ── what this site says about itself where it is not this site ────────────────────────
        // <title>, meta description and Open Graph tags. "mu*index" arrives as {site}, machine
        // voice like a hostname, rather than editable text.
        ["preview.documentTitle"] = "{page} — {site}",
        ["preview.site"] = "A directory of the MU* hobby — MUSHes, MUDs, MUCKs, MOOs — where every "
            + "fact carries how it was obtained and how old it is.",
        ["preview.demo"] = "Demo data — nothing here was measured. {description}",
        ["preview.cardAlt"] = "{site} — measured, not declared",
        ["preview.cardAlt.named"] = "{title} on {site}",

        // One id per page, title and description apart: a title is a noun phrase and a description
        // is a sentence, and a language that declines the first differently from the second has
        // nowhere to stand if they share an id.
        ["preview.title.games"] = "Games",
        ["preview.title.archive"] = "The archive",
        ["preview.title.rankings"] = "Rankings",
        ["preview.title.ecosystem"] = "The ecosystem",
        ["preview.title.reference"] = "Reference",
        ["preview.title.about"] = "About",
        ["preview.title.notFound"] = "Not found",
        ["preview.title.random"] = "Random game",
        ["preview.title.account"] = "Your games",
        ["preview.title.claim"] = "Claim {game}",

        ["preview.desc.games"] = "Every MU* we have reached, faceted on what we measured: codebase, "
            + "the protocols a server offered in the handshake, TLS, charset, language, and when we "
            + "last got in.",
        ["preview.desc.archive"] = "The games that went dark, kept. Each keeps its page, history and "
            + "URL, is still probed weekly, and returns to the listing on one successful connection.",
        ["preview.desc.rankings"] = "Busiest, most reachable, longest running — computed from "
            + "measurements only. No votes, stars or ratings anywhere on this site.",
        ["preview.desc.ecosystem"] = "Codebase share and protocol adoption across the games we "
            + "measure, with what servers offer set beside what they declare. Shares, never totals.",
        ["preview.desc.reference"] = "Hand-written pages on the codebases, clients and protocols of "
            + "the MU* hobby, cross-linked to counts taken from the crawl.",
        ["preview.desc.about"] = "How this catalogue is built: what the crawler does, what it refuses "
            + "to do, and how to make it stop.",
        ["preview.desc.notFound"] = "No game at this address. Nothing here is ever deleted, so a game "
            + "that once lived at this URL still does — check the spelling.",
        ["preview.desc.random"] = "One game from the catalogue, chosen at random and never the same "
            + "one twice.",
        ["preview.desc.account"] = "The listings you have claimed, and what a claim lets you change.",
        ["preview.desc.claim"] = "Prove you run this game by publishing a token where only its "
            + "operator could put it.",

        // Unknown count is a sentence, never a zero (rule 4).
        ["preview.game.archived"] = "Archived — last reachable {age}, and still probed",
        ["preview.game.archived.undated"] = "Archived, and still probed",
        ["preview.game.countUnknown"] = "Player count unknown — the game answers, and publishes no "
            + "number we can read",
        ["preview.game.count"] = "{count, plural, one {# player} other {# players}}, {how} {age}",

        // ── the plain mirror's feed headings and home counts ──────────────────────────────────
        // Uppercased at the call site, so the id carries words, not casing.
        ["feed.plain.newlyDiscovered"] = "Newly discovered",
        ["feed.plain.wentDark"] = "Went dark",
        ["feed.plain.cameBack"] = "Came back",

        ["home.plain.known"] = "{count, plural, one {# game known} other {# games known}}",
        ["home.plain.connectedNow"] = "{count, plural, one {# populated (measured)} other {# populated (measured)}}",
        ["home.plain.uncounted"] = "{count, plural, one {# unknown population} other {# unknown population}}",
        ["home.plain.archived"] = "{count, plural, one {# archived, still probed} other {# archived, still probed}}",
        ["home.plain.trending"] = "Trending",
    };
}
