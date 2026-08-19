using System.Globalization;

using System.Resources;

namespace MUI.Web.Localization;

/// <summary>
/// Every string the chrome says, keyed by context, in ICU MessageFormat.
/// </summary>
/// <remarks>
/// Values are ICU patterns, not resx's plain <c>{0}</c> substitutions, because a plural or gendered
/// agreement needs a real branch. Ids are granular past what English needs (e.g.
/// <c>provenance.count.measured</c> vs <c>provenance.game.measured</c>) since other languages
/// inflect the same English word differently by context. Game names, hostnames, codebase strings,
/// version numbers and protocol acronyms never enter this file — they're machine voice, marked
/// <c>translate="no"</c> in markup.
/// </remarks>
public static class Messages
{
    /// <summary>
    /// The source bundle, compiled in. Mirrors <c>Resources/Messages.resx</c> (a test keeps them in
    /// sync) so English remains the fallback even without a satellite assembly loaded.
    /// </summary>
    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        // ── counts ─────────────────────────────────────────────────────────────────────────
        ["facet.count"] = "{count, plural, one {# game} other {# games}}",
        ["facet.value.include"] = "{value}, {count, plural, one {# game} other {# games}}, only",
        ["facet.value.exclude"] = "{value}, {count, plural, one {# game} other {# games}}, excluded",
        ["facet.value.choose"] = "{value}, {count, plural, one {# game} other {# games}}",
        ["facet.any"] = "any {facet}, {count, plural, one {# game} other {# games}}",
        // Not "every fact measured": declared/derived facts are shown too, labelled as such (rule 1).
        ["listing.total"] = "{count, plural, =0 {No games listed here.}"
            + " one {# game, each fact carrying how it was obtained.}"
            + " other {# games, each fact carrying how it was obtained.}}",
        ["chart.basis"] = "{days, plural, one {# day} other {# days}} measured · {probes, plural, one {# probe} other {# probes}}",
        // Day count is a real plural branch, not "{days}d" — German inflects the unit (1 Tag, 2 Tage).
        ["window.samples"] = "{days, plural, one {#d} other {#d}} · {count, plural, one {# count} other {# counts}}",
        ["capabilities.agree"] = "{disagreeing, plural, =0 {None of the {total} disagree.} one {# of {total} disagrees with what the game declares.} other {# of {total} disagree with what the game declares.}}",

        // ── find a game ────────────────────────────────────────────────────────────────────
        // A sentence, not a bare number — a screen reader needs to say what the number counts.
        ["find.matching"] = "{count, plural, =0 {No games match every answer.} one {# game matches every answer.} other {# games match every answer.}}",
        ["find.basis"] = "of {listed, plural, one {# listed game} other {# listed games}} · {answers, plural, =0 {no answers given} one {# answer given} other {# answers given}}",
        ["find.show"] = "{count, plural, one {Show the one game} other {Show these # games}}",
        ["find.drop"] = "drop \"{answer}\" — {count, plural, one {# game} other {# games}}",
        ["find.clear"] = "clear answer to: {question}",

        ["find.title"] = "Find a game",
        ["find.kicker"] = "matching all answers",
        ["find.noun"] = "{count, plural, one {game} other {games}}",
        ["find.clearAll"] = "clear all answers",
        ["find.startAgain"] = "start again",
        ["find.more"] = "{count, plural, one {# more} other {# more}}",
        ["find.answersGiven"] = "answers given",
        ["find.wholeListing"] = "the whole listing",
        ["find.refused"] = "that query was refused",
        ["find.name.label"] = "a name, if you have one",
        ["find.name.placeholder"] = "name, or part of one",
        ["find.name.submit"] = "Search by name",

        // The six questions, and the answer that un-asks each one.
        ["find.q.band"] = "Is anyone playing right now?",
        ["find.q.genre"] = "What do you want to play?",
        ["find.q.lineage"] = "What kind of game?",
        ["find.q.language"] = "In which language?",
        ["find.q.client"] = "Anything your client needs?",
        ["find.q.dark"] = "Include games that have gone dark?",
        ["find.any.band"] = "doesn't matter",
        ["find.any.genre"] = "any genre",
        ["find.any.lineage"] = "any kind",
        ["find.any.language"] = "any language",
        ["find.any.client"] = "doesn't matter",
        ["find.dark.no"] = "no, only live games",
        ["find.dark.yes"] = "yes, show me those too",
        ["find.dark.chip"] = "games that have gone dark",

        // ── the client question's options ─────────────────────────────────────────────────────
        // One id per capability with its whole label, not a gloss glued to an acronym.
        ["find.protocol.tls"] = "TLS — encrypted, handshake completed by us",
        ["find.protocol.mssp"] = "MSSP — server self-description",
        ["find.protocol.mccp"] = "MCCP — compressed output",
        ["find.protocol.mxp"] = "MXP — clickable links",
        ["find.protocol.gmcp"] = "GMCP — structured client data",
        ["find.protocol.msdp"] = "MSDP — structured client data",
        ["find.protocol.charset"] = "CHARSET — encoding negotiation",
        ["find.protocol.utf8"] = "UTF-8 — non-Latin text renders",
        ["find.protocol.ttype"] = "TTYPE — client tells its type",
        ["find.protocol.atcp"] = "ATCP — structured client data",
        ["find.protocol.msp"] = "MSP — sound triggers",
        ["find.protocol.eor"] = "EOR — prompt marking",
        ["find.protocol.other"] = "{token} — measured in the handshake",

        // ── the locked provenance words, one id per context ───────────────────────────────────
        ["provenance.count.measured"] = "measured",
        ["provenance.game.measured"] = "measured",
        ["provenance.capability.measured"] = "measured",
        ["provenance.screen.measured"] = "measured",
        ["kicker.measured"] = "measured",
        ["provenance.count.declared"] = "declared",
        ["provenance.game.declared"] = "declared",
        ["provenance.capability.declared"] = "declared",
        ["kicker.declared"] = "declared",
        ["provenance.derived"] = "derived",
        ["kicker.derived"] = "derived",

        // ── the four kinds of absence ─────────────────────────────────────────────────────────
        ["state.notMeasured"] = "not measured",
        ["state.uncounted"] = "uncounted",
        ["state.unreachable"] = "unreachable",
        ["state.notCounted"] = "not counted",

        // ── the listing's own absences ────────────────────────────────────────────────────────
        ["listing.count.none"] = "no count",
        ["listing.plain.fromHere"] = "from here",
        ["listing.plain.archived"] = "archived",
        ["listing.plain.claimed"] = "claimed",
        ["random.empty.title"] = "Nothing to pick from",
        ["random.empty.body"] = "No game matches that filter. Try {listing}, or {archive}.",
        ["random.empty.listing"] = "the whole listing",
        ["random.empty.archive"] = "include the archive",

        // ── the words the product rests on ────────────────────────────────────────────────────
        ["term.connected"] = "connected",
        ["term.unclaimed"] = "unclaimed",
        ["term.claimedByOwner"] = "claimed by its owner",
        ["term.stillProbed"] = "still probed",
        ["term.typical"] = "typical",
        ["term.peak"] = "peak",

        // ── the accessibility promises ────────────────────────────────────────────────────────
        ["a11y.readAsText"] = "read as text",
        ["a11y.plainText"] = "plain text",
        ["a11y.skipToContent"] = "skip to content",
        ["a11y.asciiBanner"] = "ASCII banner: the connect screen of {game}.",


        // ── site chrome ───────────────────────────────────────────────────────────────────────
        ["nav.catalogues"] = "Catalogues",
        ["nav.account"] = "This site and your account",
        ["nav.browse"] = "browse",
        ["nav.learn"] = "learn",
        ["nav.thisSite"] = "this site",
        ["nav.menu"] = "menu",
        ["nav.reading"] = "reading",
        ["nav.games"] = "games",
        ["nav.find"] = "find",
        ["nav.random"] = "random",
        ["nav.archive"] = "archive",
        ["nav.reference"] = "reference",
        ["nav.ecosystem"] = "ecosystem",
        ["nav.rankings"] = "rankings",
        ["nav.about"] = "about",
        ["nav.discord"] = "Discord",
        ["nav.submit"] = "submit",
        ["nav.submitGame"] = "submit a game",
        ["nav.signIn"] = "sign in",
        ["nav.yourGames"] = "your games",
        ["theme.label"] = "theme",
        ["theme.auto"] = "auto",
        ["theme.light"] = "light",
        ["theme.dark"] = "dark",
        ["banner.demo.lead"] = "Demo data.",
        ["banner.demo"] = "No database is configured, so this is a fixture. Nothing here was measured.",
        ["footer.archive"] = "archive",

        // ── home ──────────────────────────────────────────────────────────────────────────────
        ["home.title"] = "A directory of the MU* hobby",
        // Same correction as listing.total: not every fact is measured, just labelled as which kind.
        ["home.lede"] = "Every fact carries how it was obtained and how old it is: measured by our "
            + "crawler, or declared by the game and marked as such.",
        ["home.search.label"] = "Search games by name, theme, codebase or host",
        ["home.search.placeholder"] = "search by name, theme, codebase or host",
        ["home.search.submit"] = "search",
        ["tile.gamesKnown"] = "games known",
        ["tile.connectedNow"] = "populated",
        ["tile.answeringUncounted"] = "unknown population",
        ["tile.archived"] = "archived",
        ["feed.newlyDiscovered"] = "newly discovered",
        ["feed.wentDark"] = "went dark — still probed",
        ["feed.cameBack"] = "came back",
        ["feed.nothingNew"] = "Nothing new.",
        ["feed.nothingDark"] = "Nothing went dark.",
        ["feed.nothingBack"] = "Nothing came back. We keep knocking.",
        ["feed.live"] = "live",

        // ── the listing ───────────────────────────────────────────────────────────────────────
        ["games.title"] = "Games",
        ["listing.sortedBy"] = "sorted by {order}",
        ["listing.random"] = "random",
        ["listing.columns"] = "connected · reached",
        ["listing.fromHere"] = "from here",
        ["listing.empty.head"] = "Nothing matched.",
        ["listing.empty.hint"] = "Try fewer words, or drop a filter.",
        ["listing.clearFilters"] = "clear filters",
        ["listing.aboutCodebase"] = "about {codebase}",
        ["listing.never"] = "never",
        ["listing.claimed"] = "claimed by its owner",
        ["listing.unknownCodebase"] = "Unknown Codebase",
        ["listing.unknownCodebase.title"] = "we could not identify the codebase this game runs",
        ["listing.moreProtocols"] = "and {count, plural, one {# more} other {# more}}: {names}",

        // ── the order switch ──────────────────────────────────────────────────────────────────
        ["switch.order"] = "Order",
        ["switch.window"] = "Window",
        ["switch.now"] = "now",
        ["switch.typical"] = "typical",
        ["switch.peak"] = "peak",
        ["switch.name"] = "name",
        ["switch.reached"] = "reached",
        ["window.7"] = "7 days",
        ["window.30"] = "30 days",
        ["window.90"] = "90 days",

        // ── the filter panel ──────────────────────────────────────────────────────────────────
        ["filters.search.label"] = "Search games",
        ["filters.search.placeholder"] = "search games",
        ["filters.summary"] = "filters",
        ["filters.showing"] = "showing",
        ["filters.clearAll"] = "clear all",
        ["filters.stopFiltering"] = "— stop filtering by this",
        ["facet.anyValue"] = "any",
        ["facet.more"] = "more filters ({count})",
        ["facet.moreValues"] = "{count, plural, one {# more} other {# more}}",
        ["facet.alsoShow"] = "also show",
        ["facet.alsoShow.note"] = "Off by default. Neither is a judgement about the game.",
        ["facet.archived"] = "archived",
        ["facet.adult"] = "adult",
        ["facet.archived.state"] = "archived games, {shown, select, true {shown} other {hidden}}",
        ["facet.adult.state"] = "games declaring adult content, {shown, select, true {shown} other {hidden}}",
        ["facet.countsNote"] = "Every count here comes from a game we measured — not an estimate.",
        ["facet.key.summary"] = "what the badges and the blanks mean",
        ["facet.key.blank"] = "A blank is a gap in our measurement, not a no. Each facet spells its own: not identified, not declared, nothing negotiated.",
        ["facet.key.zero"] = "A measured zero is a count. An unknown count is not a zero and never sorts as one.",
        ["facet.key.openEnded"] = "Open-ended facets list their {count} commonest values. The rest are reachable by search and by URL.",
        ["facet.presence.note"] = "Unticked means not measured — not that the game lacks it.",

        // ── facet groups ──────────────────────────────────────────────────────────────────────
        ["facet.group.band"] = "activity",
        ["facet.group.seen"] = "last seen",
        ["facet.group.protocol"] = "protocols offered",
        ["facet.group.tls"] = "encrypted",
        ["facet.group.charset"] = "encoding",
        ["facet.group.codebase"] = "codebase",
        ["facet.group.version"] = "version",
        ["facet.group.lineage"] = "lineage",
        ["facet.group.family"] = "family",
        ["facet.group.trending"] = "trending",
        ["facet.group.genre"] = "genre",
        ["facet.group.language"] = "language",

        // ── facet values ──────────────────────────────────────────────────────────────────────
        ["facet.band.playersNow"] = "connected now",
        ["facet.band.activeThisWeek"] = "active this week",
        // Not "uncounted" — this band mixes a measured-zero week with an unreadable-count week
        // (rules 2/4), so it names the threshold rather than a cause.
        ["facet.band.quiet"] = "quiet — no count above 0",
        ["facet.band.dark"] = "dark — not reached in a month",
        ["facet.band.archived"] = "archived",
        ["facet.seen.day"] = "in the last 24 hours",
        ["facet.seen.week"] = "in the last 7 days",
        ["facet.seen.month"] = "in the last 30 days",
        ["facet.seen.older"] = "longer ago",
        ["facet.seen.never"] = "never reached",
        ["facet.unknown.charset"] = "nothing negotiated",
        ["facet.unknown.codebase"] = "not identified",
        ["facet.unknown.trending"] = "not enough measurement yet",
        ["facet.unknown.other"] = "not declared",
        ["facet.tls.yes"] = "connected over TLS",
        ["facet.excluded"] = "not {value}",
        ["facet.known.charset"] = "something negotiated",
        ["facet.known.codebase"] = "identified at all",
        ["facet.known.trending"] = "measured both weeks",
        ["facet.known.other"] = "declared at all",

        // ── trending, this week's median against last week's ─────────────────────────────────────
        ["facet.trending.up"] = "trending up",
        ["facet.trending.steady"] = "steady",
        ["facet.trending.down"] = "trending down",

        // ── evidence, and what each word means ────────────────────────────────────────────────
        ["evidence.measured.meaning"] = "we watched this happen",
        ["evidence.declared.meaning"] = "the game says so, and we did not check",
        ["evidence.derived.meaning"] = "we grouped what the game told us",

        // ── sort orders ───────────────────────────────────────────────────────────────────────
        // {days} selects a plural form here too — see window.samples above.
        ["sort.name"] = "name",
        ["sort.players"] = "connected now",
        ["sort.reached"] = "last reached",
        ["sort.medianWeek"] = "typically on · 7 days",
        ["sort.medianMonth"] = "typically on · 30 days",
        ["sort.medianQuarter"] = "typically on · 90 days",
        ["sort.peakWeek"] = "most on at once · 7 days",
        ["sort.peakMonth"] = "most on at once · 30 days",
        ["sort.peakQuarter"] = "most on at once · 90 days",
        ["sort.group.row"] = "on the row now",
        ["sort.group.typical"] = "typical",
        ["sort.group.peak"] = "peak",
        ["sort.unranked.players"] = "Unknown count",
        ["sort.unranked.reached"] = "never once reached — not reached long ago",
        ["sort.unranked.median"] = "fewer than {minimum} counts in the window, or none at all — not a typical count of zero",
        ["sort.unranked.window"] = "nothing we could count in the window — not a game nobody was on",
        ["sort.window.median"] = "median {value} · {days, plural, one {#d} other {#d}}"
            + " · {count, plural, one {# count} other {# counts}}",
        ["sort.window.peak"] = "most {value} at once · {days, plural, one {#d} other {#d}}"
            + " · {count, plural, one {# count} other {# counts}}",

        // ── the game page's own headings ──────────────────────────────────────────────────────
        ["game.connectScreen"] = "Connect screen",
        ["game.connectionsByHour"] = "Connections by hour",
        ["game.howMany"] = "How many, over time",
        ["game.reachable"] = "Reachable",
        ["game.whatChanged"] = "What changed",
        ["game.capabilities"] = "Capabilities",
        ["game.declaredByGame"] = "Declared by the game",
        ["game.referrals"] = "Referrals",
        ["game.unclaimed"] = "Unclaimed — everything here was measured.",
        ["game.claimed"] = "Claimed by its owner — measured facts below are still ours.",
        ["game.claim"] = "Claim this game",
        ["game.answeringSince"] = "answering since {date}",
        ["game.readAsTextRows"] = "read as text — {count, plural, one {# row} other {# rows}}",
        ["capability.column"] = "capability",
        ["capability.age"] = "age",
        ["capability.offered"] = "offered",
        ["capability.silent"] = "silent",
        ["capability.absent"] = "absent",
        ["capability.denied"] = "denied",
        ["capability.claimed"] = "claimed",
        ["capability.disagrees"] = "disagrees",
        ["capability.whereTheyDisagree"] = "where they disagree ({count})",

        // ── the week of hours, said in words ──────────────────────────────────────────────────
        // Three states per rule 2: counted (incl. measured zero), probed-no-count, and not
        // measured — the third names no cause, since a failed probe and an undialled hour look alike.
        ["activity.cell.counted"] = "{day} {time} — {count, plural, =0 {0 players, measured} one {# player on average} other {# players on average}}",
        ["activity.cell.notCounted"] = "{day} {time} — probed, no count could be read",
        ["activity.cell.notMeasured"] = "{day} {time} — no measurement in this hour",

        ["activity.none"] = "We have not measured this game's activity yet.",
        ["activity.noCount"] = "No hour of the week has produced a player count.",

        // A measured zero is a measurement and must not read as an absence of data.
        ["activity.allZero"] = "Measured every hour and nobody has been on in any of them.",

        ["activity.gap.day"] = "{count, plural, one {# hour on {day} has no measurement yet.} other {# hours on {day} have no measurement yet.}}",
        ["activity.gap.week"] = "{count, plural, one {# hour across the week has no measurement yet.} other {# hours across the week have no measurement yet.}}",
        ["activity.uncounted.day"] = "{count, plural, one {# hour on {day} answered but produced no count.} other {# hours on {day} answered but produced no count.}}",
        ["activity.uncounted.week"] = "{count, plural, one {# hour across the week answered but produced no count.} other {# hours across the week answered but produced no count.}}",

        ["activity.busiest.everyDay"] = "Busiest every day, {window}.",
        ["activity.busiest.everyDay.part"] = "Busiest every day, {part}, {window}.",
        ["activity.busiest.days"] = "Busiest {days} {window}.",
        ["activity.busiest.days.part"] = "Busiest {days} {part}, {window}.",

        ["activity.quiet"] = "Reliably quiet {who}, {window}.",
        ["activity.quiet.part"] = "Reliably quiet {who} in the {part}, {window}.",
        ["activity.quiet.everyDay"] = "every day",
        ["activity.quiet.everyMeasuredDay"] = "every day we could measure",
        ["activity.quiet.weekdays"] = "on weekdays",
        ["activity.quiet.onDays"] = "on {days}",

        // Same four names in two registers (plural for the busy band, singular for the quiet one) —
        // English builds one from the other, but that's not true of every language.
        ["activity.part.morning"] = "morning",
        ["activity.part.afternoon"] = "afternoon",
        ["activity.part.evening"] = "evening",
        ["activity.part.smallHours"] = "small hours",
        ["activity.parts.morning"] = "mornings",
        ["activity.parts.afternoon"] = "afternoons",
        ["activity.parts.evening"] = "evenings",
        ["activity.parts.smallHours"] = "small hours",

        ["activity.days.list"] = "{list}, {next}",
        ["activity.days.pair"] = "{first} and {second}",

        ["activity.sparse.kicker"] = "not enough measurements yet",
        ["activity.sparse.none"] = "No hour of the week has a measurement yet.",
        ["activity.sparse.uncounted"] = "{count, plural, one {# hour answered and produced no count.} other {# hours answered and produced no count.}}",
        ["activity.sparse.wait"] = "The grid appears once every day of the week has one.",
        ["activity.sparse.days"] = "{days, plural, one {Measured on # of the seven days so far; the grid appears once every day has an hour in it.} other {Measured on # of the seven days so far; the grid appears once every day has an hour in it.}}",
        ["activity.sample.zero"] = "{count, plural, one {# hour measured, all of it at nobody on.} other {# hours measured, all of them at nobody on.}}",
        ["activity.sample.peak"] = "{count, plural, one {# hour measured, the busiest {peak} on {day} at {time} UTC.} other {# hours measured, the busiest {peak} on {day} at {time} UTC.}}",
        ["activity.sample.more"] = "{count, plural, one {# more hour answered and produced no count.} other {# more hours answered and produced no count.}}",

        ["activity.day.line"] = "{day} — {facts}",
        ["activity.day.facts"] = "{first}, {second}",
        ["activity.day.allZero"] = "measured at zero all day",
        ["activity.day.peak"] = "peak {count} at {time}",
        ["activity.day.nobodyOn"] = "nobody on {window}",
        ["activity.day.noCount"] = "no count in any hour",
        ["activity.day.notMeasured"] = "{count, plural, one {# hour not measured} other {# hours not measured}}",
        ["activity.day.notCounted"] = "{count, plural, one {# hour probed but uncountable} other {# hours probed but uncountable}}",

        ["activity.column.day"] = "day",
        ["activity.column.quietest"] = "quietest",
        ["activity.column.busiest"] = "busiest",
        ["activity.column.at"] = "at",
        ["activity.column.noCount"] = "no count",
        ["activity.caption"] = "Players on by day, in UTC. {window}.",
        ["activity.times"] = "times in UTC · {window}",
        ["activity.rollingAverage"] = "{weeks, plural, one {#-week rolling average} other {#-week rolling average}}",
        ["activity.legend.counted"] = "counted, including a measured zero",
        ["activity.legend.notCounted"] = "probed, no count could be read",
        ["activity.legend.notMeasured"] = "no measurement in that hour",
        ["activity.plain.heading"] = "When people are on (UTC)",
        ["activity.key.counted"] = "counted",
        ["activity.key.counted.meaning"] = "we got in and read a number, including a measured zero",
        ["activity.key.uncounted.meaning"] = "we got in and no number could be read",
        ["activity.key.notMeasured.meaning"] = "we have no measurement for that hour",

        // ── the switcher's own chrome ─────────────────────────────────────────────────────────
        ["locale.label"] = "language",
        ["locale.submit"] = "change language",

        // ═════════════════════════════════════════════════════════════════════════════════════
        // STATIC PAGE COPY — /about, /submit, /account/sign-in
        // Every lead/body pair is two ids because the graphical page bolds the lead and the plain
        // page cannot — presentation stays out of the string.
        // ═════════════════════════════════════════════════════════════════════════════════════

        // ── about: the page, and what a fact here is ──────────────────────────────────────────
        ["about.title"] = "About mu*index",
        ["about.lede"] = "Every game here was measured by the crawler, and every value says where it "
            + "came from and when. This page covers what we gather, its limits, what we won't do, "
            + "and how to make the crawler stop.",

        ["about.measures.heading"] = "What a fact here is",
        ["about.measures.declared.lead"] = "Measured beats declared, and both are shown.",
        ["about.measures.declared.body"] = "A game's MSSP report is the game describing itself. The "
            + "telnet handshake is what we watched it do. Both appear on its page, labelled with how "
            + "and when. Where they disagree, we show the disagreement.",
        ["about.measures.count.lead"] = "A player count says where it came from.",
        ["about.measures.count.body"] = "Either a WHO or DOING read at the connect screen, which we "
            + "counted, or the game's own MSSP PLAYERS field, which it published. Never merged.",
        ["about.measures.unknown.lead"] = "Only a completed read can be a zero. Anything else is unknown.",
        ["about.measures.unknown.body"] = "Servers customise their WHO headers freely, and past a "
            + "point our parser cannot read one. That is uncountable, its own state. A measured zero "
            + "— we got in, nobody was there — is a count, and prints as one.",
        ["about.measures.reachable.lead"] = "We call this reachable. We never call it uptime.",
        ["about.measures.reachable.body"] = "We open a socket from one host at intervals. A game we "
            + "cannot route to is unreachable and perfectly alive. Nothing here claims a game's "
            + "uptime, because nothing here measured it.",
        ["about.measures.hour.lead"] = "An hour is counted, uncountable, or not measured.",
        ["about.measures.hour.body"] = "The activity grid has three states. The third is empty and "
            + "names no cause: an hour we could not reach and an hour we never probed are the same "
            + "absence, and neither is that server's downtime.",

        // ── about: How we gather and adjust data ──────────────────────────────────────────────────
        ["about.limits.heading"] = "How we gather and adjust data",
        ["about.limits.grace.lead"] = "Archive grace is measured from the day we found you.",
        ["about.limits.grace.body"] = "A game that stops answering leaves the default listing after "
            + "its grace period: a quarter of the reachable time we probed, floored at 60 days and "
            + "capped at 365. A game running since 1995 starts at the floor on the day we discover "
            + "it. We import nothing to fill in the years before we arrived.",
        ["about.limits.created.lead"] = "We do not credit MSSP CREATED toward that grace.",
        ["about.limits.created.body"] = "It is one hand-typed line in a config file, so crediting it "
            + "would make the archive threshold gameable. We also can't guess at its format.",
        ["about.limits.claim.lead"] = "Claiming a game earns the ceiling.",
        ["about.limits.claim.body"] = "Proving server access is worth the full year of archival grace.",
        ["about.limits.deletion.lead"] = "Nothing is ever deleted.",
        ["about.limits.deletion.body"] = "Archiving takes a game out of the default listing, the "
            + "rankings and the active-today figure, and nothing else. Its page, URL, history and "
            + "address survive, it keeps being probed, and one successful probe puts it back.",

        // ── about: what this site will not do ─────────────────────────────────────────────────
        ["about.never.heading"] = "What this site will not do",
        ["about.never.votes.lead"] = "No votes, stars, ratings or recommendations.",
        ["about.never.votes.body"] = "Rankings are computed from measured data only.",
        ["about.never.forums.lead"] = "No forums, reviews, wikis, comments or player profiles.",
        ["about.never.forums.body"] = "We aim to only provide data and information.",
        ["about.never.names.lead"] = "Player names are never persisted.",
        ["about.never.names.body"] = "A WHO reply is parsed in memory for a count or other sources, and counted. " +
                                     "Self-published MSSP values are the exception to this rule.",

        // ── about: the crawler, and how to make it stop ───────────────────────────────────────
        // The command list, MSSP variable, and DNS label/value are read off the objects that
        // consume them, so this page cannot advertise a switch wired to nothing.
        ["about.crawler.heading"] = "The crawler, and how to make it stop",
        ["about.crawler.probe.lead"] = "A probe is one connection that never logs in.",
        ["about.crawler.probe.body"] = "It opens a socket, negotiates telnet options, reads the "
            + "connect screen, asks for MSSP by negotiating option 70, sends {commands}, and "
            + "disconnects. No character, no login, nothing changed on the far side. A timeout "
            + "bounds the session so a wedged probe cannot sit on a connection slot.",
        ["about.crawler.delay.lead"] = "CRAWL DELAY wins.",
        ["about.crawler.delay.body"] = "A game that states a preferred minimum gap in its MSSP "
            + "report is heeded. A dark game is still tried for ever at the longer interval, which is how "
            + "it re-lists itself when it comes back.",
        ["about.crawler.referral.lead"] = "We check where REFERRAL points.",
        ["about.crawler.referral.body"] = "MSSP lets a game name other games. We use the endpoint as the source of truth however.",
        ["about.crawler.screens.lead"] = "Connect screens are shown because they are sent to "
            + "everybody.",
        ["about.crawler.screens.body"] = "Claiming a game gives its owner the option to stop it being shown.",
        ["about.crawler.stop.lead"] = "Say stop, and we stop — three ways.",
        ["about.crawler.stop.body"] = "Publish {variable} 1 in your MSSP report, and the probe that "
            + "reads it is the last one. Or publish a TXT record at {label}.your.host reading "
            + "\"{value}\", which needs no MSSP support and no account here. Or write to a person. "
            + "All three are honoured within one crawl cycle, recorded with the date and what we "
            + "read, and enforced on the submission form too.",
        ["about.crawler.scope.lead"] = "The MSSP field stops that listener; the record stops the "
            + "host.",
        ["about.crawler.scope.body"] = "MSSP is published by the port that answered, so it speaks "
            + "for that port — MU* hosting routinely runs unrelated games on one domain, and one "
            + "must not silence its neighbour. A TXT record covers every port unless it names one, "
            + "as \"{value}=4201\". Anything there we cannot read as a port list means the whole "
            + "host, so \"{value}=all\" works.",
        ["about.crawler.dns.lead"] = "The DNS route is the one you can undo without asking us.",
        ["about.crawler.dns.body"] = "A TXT record is readable without connecting to a server that "
            + "told us not to, so we re-read it before every dial. Delete it and we dial again "
            + "within a week. An MSSP field cannot be re-read without doing the thing you asked us "
            + "to stop, so MSSP opt-outs and written requests stand until you say otherwise. That "
            + "TXT lookup is all an opted-out address gets: it touches your nameserver, never your "
            + "game.",
        ["about.crawler.stopping.lead"] = "Stopping is not deleting, and it is not downtime.",
        ["about.crawler.stopping.body"] = "A game that opts out keeps its page, its address and "
            + "everything we measured before it asked. Only new data stops: the activity grid stops "
            + "gaining hours and names no cause, because our decision to stop knocking is a fact "
            + "about us. It is recorded on the crawl that did not happen, and in the register of "
            + "who asked.",
        ["about.crawler.unlist.lead"] = "If stopping is not enough, the listing can go too.",
        ["about.crawler.unlist.body"] = "Once we have stopped on every address your game answers "
            + "on, your dashboard offers one more thing: take it out of the listing, the rankings "
            + "and the daily figure. The page and every address it has ever had still answer, and "
            + "nothing is deleted — it stops being somewhere a reader arrives by browsing. It needs "
            + "a verified claim, because it is a decision about your game and we record who made "
            + "it. And a probe undoes it: take your opt-out back, and the next dial that gets an "
            + "answer puts you back in the listing without asking us twice.",

        // ── about: who is knocking ────────────────────────────────────────────────────────────
        // {name} is read off ProbeOptions, so a deployment with its own crawler name gets a page
        // that names it.
        ["about.identity.announced"] = "The crawler names itself {name} when a server asks what it "
            + "is.",
        ["about.identity.crawler"] = "crawler",
        ["about.identity.contact"] = "contact",
        ["about.identity.crawler.line"] = "Crawler: {name}",
        ["about.identity.contact.line"] = "Contact: {url}",
        ["about.identity.placeholder"] = "— placeholder; this deployment set no contact address",
        ["about.identity.placeholder.plain"] = "No contact address is configured, so the one above "
            + "is a placeholder and answers nobody.",

        // ── submit a game ─────────────────────────────────────────────────────────────────────
        // A host and a port, nothing else — the form has no name/description box.
        ["submit.title"] = "Submit a game",
        ["submit.lede"] = "Tell us where a game is. A host and a port is the whole form; everything "
            + "else on this site is measured by our own crawler.",
        ["submit.host.label"] = "Host",
        ["submit.port.label"] = "Port",
        ["submit.host.hint"] = "mud.example.org, or paste mud.example.org:4201 and leave the port "
            + "empty",
        ["submit.button"] = "Submit",
        ["submit.noCatalogue"] = "Submitting needs a database, and this site is running on the demo "
            + "fixture. There is no crawl registry to write into, so the form is absent rather than "
            + "quietly doing nothing.",
        ["submit.notHere"] = "Not here",
        ["submit.what.heading"] = "What happens to an address",
        ["submit.what.resolve"] = "We resolve the address before dialling it, and refuse anything "
            + "that resolves off the public internet. That is a decision about our own socket, "
            + "never a fact about a game.",
        ["submit.what.optOut"] = "If whoever runs that host has asked us not to crawl it, we will "
            + "not take the address, whoever submits it. A stranger cannot put your game back on "
            + "this site.",
        ["submit.what.schedule"] = "If it answers, we read what the server says for itself and keep "
            + "reading it on its own schedule, for ever. An address only has to be given once.",
        ["submit.what.claim"] = "Nothing appears on the site until somebody proves they run it. "
            + "Claiming takes a passkey and one line published on the game itself.",
        ["submit.what.duplicate"] = "An address we already have collapses onto the existing entry. "
            + "Sending it twice makes no second listing and brings no probe forward.",

        ["submit.answer.thatAddress"] = "that address",
        ["submit.accepted.heading"] = "In the registry.",
        ["submit.accepted.sentence"] = "{address} will be dialled on the next crawl cycle, then on "
            + "its own schedule for ever. It appears here once somebody proves they run it — come "
            + "back to this form with the same address and it will hand you the link.",
        ["submit.unclaimed.heading"] = "We have it, unclaimed.",
        ["submit.unclaimed.sentence"] = "{address} is one we already measure. It stays off the site "
            + "until somebody proves they run it. If that is you, this is the way in.",
        ["submit.known.heading"] = "We already have that one.",
        ["submit.known.sentence"] = "{address} is a game we already measure. Nothing was created and "
            + "nothing was changed.",
        ["submit.knownAddress.heading"] = "We already have that address.",
        ["submit.knownAddress.sentence"] = "{address} is already known to us. Nothing was created "
            + "and nothing was changed.",
        ["submit.queued.heading"] = "Already waiting.",
        ["submit.queued.sentence"] = "{address} is in the crawl registry and has not answered yet. "
            + "Sending it again does not bring it forward: a target keeps its own schedule, so "
            + "nobody can hurry us at somebody else's server.",
        ["submit.malformed.heading"] = "Not an address we can dial.",
        ["submit.malformed.sentence"] = "A host needs a dot or a colon in it, and a port is a number "
            + "between 1 and 65535. Fill in both boxes, or paste mud.example.org:4201 into the "
            + "first.",
        ["submit.undialable.heading"] = "We cannot dial that.",
        ["submit.undialable.sentence"] = "Three things produce this answer for {address}: the name "
            + "may not resolve, it may resolve off the public internet, or whoever runs that host "
            + "may have asked us to stay away. We deliberately do not say which, because answering "
            + "that for a stranger maps a network from outside it. Nothing was recorded about the "
            + "address; the decision was ours and it is filed as ours.",
        ["submit.tooMany.heading"] = "Enough for now.",
        ["submit.tooMany.sentence"] = "This form is rate-limited by sender, and you have hit the "
            + "bound. Come back in an hour. Nothing was lost — anything we took is already in the "
            + "registry.",
        ["submit.link.claim"] = "claim this game",

        // ── signing in, which is a passkey and nothing else ───────────────────────────────────
        ["account.signIn.title"] = "Sign in",
        ["account.signIn.preview"] = "Sign in with a passkey to claim a game you run. There is no "
            + "password to lose and none to steal.",
        ["account.signIn.noDatabase"] = "Claiming needs a database, and this site is running on the "
            + "demo fixture. There is nothing to sign in to.",
        ["account.signIn.passkey.lead"] = "Sign-in is a passkey.",
        ["account.signIn.passkey.body"] = "Your device or password manager holds the private key; we "
            + "hold only the public half. No password, no email.",
        ["account.signIn.button"] = "Sign in with a passkey",
        ["account.signIn.script"] = "The one page here that needs JavaScript. Passkeys cannot work "
            + "without it.",
        ["account.register.heading"] = "No account yet?",
        ["account.register.lede"] = "You need one only to claim a game you run. Pick a name to be "
            + "known by — a label beside your claim, not a real name.",
        ["account.register.name.label"] = "Name",
        ["account.register.name.placeholder"] = "e.g. corvid-admin",
        ["account.register.button"] = "Create an account with a passkey",
        ["account.store.heading"] = "What we store",
        ["account.store.name"] = "The name you chose.",
        ["account.store.keys"] = "The public key of each passkey you register, and what your device "
            + "called it.",
        ["account.store.claims"] = "Which games you have claimed, and when.",
        ["account.store.note"] = "No email address, no password, no IP log tied to your account. "
            + "Lose every passkey and you can publish a fresh claim token on your game and start "
            + "again: the game is the proof, not the account.",

        // ── dates, ages and provenance ────────────────────────────────────────────────────────
        // Month names come from CultureInfo (Locales.CultureOf), but day/month/year arrangement is
        // a per-language pattern here: Japanese writes 2026年7月30日, unreachable via ToString.
        ["date.absolute"] = "{day} {month} {year}",

        // UTC is named, not implied — every time here is UTC because that's the crawler's only clock.
        ["date.stamp"] = "{date} {time} UTC",

        // ── the age ladder, in three registers ────────────────────────────────────────────────
        // Three families over the same seven rungs: a bare duration, "confirmed X ago", and "unreached
        // for X" are different questions even where the English is identical. Every rung is a real
        // ICU plural even where English has one form, since other languages may need the branch.
        ["age.short.now"] = "now",
        ["age.short.minutes"] = "{count, plural, one {#m} other {#m}}",
        ["age.short.hours"] = "{count, plural, one {#h} other {#h}}",
        ["age.short.days"] = "{count, plural, one {#d} other {#d}}",
        ["age.short.weeks"] = "{count, plural, one {#w} other {#w}}",
        ["age.short.months"] = "{count, plural, one {#mo} other {#mo}}",
        ["age.short.years"] = "{count, plural, one {#y} other {#y}}",

        // How long ago we last confirmed a value. The freshest rung is its own id ("just now") so
        // "now ago" can't be reintroduced.
        ["age.ago.now"] = "just now",
        ["age.ago.minutes"] = "{count, plural, one {#m ago} other {#m ago}}",
        ["age.ago.hours"] = "{count, plural, one {#h ago} other {#h ago}}",
        ["age.ago.days"] = "{count, plural, one {#d ago} other {#d ago}}",
        ["age.ago.weeks"] = "{count, plural, one {#w ago} other {#w ago}}",
        ["age.ago.months"] = "{count, plural, one {#mo ago} other {#mo ago}}",
        ["age.ago.years"] = "{count, plural, one {#y ago} other {#y ago}}",

        // How long since the game was last reached. Never "offline"/"down" — reachable, not up.
        ["age.dark.now"] = "just now",
        ["age.dark.minutes"] = "{count, plural, one {#m ago} other {#m ago}}",
        ["age.dark.hours"] = "{count, plural, one {#h ago} other {#h ago}}",
        ["age.dark.days"] = "{count, plural, one {#d ago} other {#d ago}}",
        ["age.dark.weeks"] = "{count, plural, one {#w ago} other {#w ago}}",
        ["age.dark.months"] = "{count, plural, one {#mo ago} other {#mo ago}}",
        ["age.dark.years"] = "{count, plural, one {#y ago} other {#y ago}}",

        // The <time> element's hover title and the absolute a screen reader hears after the age.
        ["time.title"] = "{age}, {stamp}",
        ["time.spoken"] = ", {stamp}",

        // ── the provenance chip's tooltip ─────────────────────────────────────────────────────
        // "last confirmed" is the date we last saw the value, not a date the game did anything.
        ["chip.title"] = "{value} — {how} via {source}, last confirmed {date}",
        ["chip.title.stale"] = "{value} — {how} via {source}, last confirmed {date} (past its expected refresh)",

        // The same chip in plain text, where there is no hover to put it in.
        ["chip.plain"] = "({how}, {age})",
        ["chip.plain.stale"] = "({how}, {age}, stale)",

        ["provenance.game.ownerDeclared"] = "owner-declared",

        // ── how a value reached us, one id per source ─────────────────────────────────────────
        // Display names, not FieldSource's ToString ("via Mssp") — protocol names stay as-is in
        // every locale; the rest is ours to translate.
        ["source.staff"] = "staff",
        ["source.handshake"] = "the telnet handshake",
        ["source.owner"] = "the owner",
        ["source.who"] = "WHO",
        ["source.i3"] = "I3",
        ["source.mssp"] = "MSSP",
        ["source.info"] = "INFO",
        ["source.i3Mudlist"] = "the I3 mudlist",
        ["source.banner"] = "the connect screen",

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

        ["rankings.trending.title"] = "Trending this week",
        ["rankings.trending.basis"] = "This week's median measured players against the week before it, among games with enough samples for a median in both weeks — always the current week, whichever ranking window is shown above.",
        ["rankings.trending.empty"] = "No listed game is trending up this week — a statement about how many games clear the sample floor in both weeks, not about the hobby.",
        ["rankings.trending.caption"] = "Games whose measured median this week is enough higher than the week before to call it a rise rather than noise, both weeks with enough samples for a median.",
        ["rankings.column.thisWeek"] = "this week",
        ["rankings.column.lastWeek"] = "last week",
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
        ["rankings.plain.trending"] = "Trending this week",
        ["rankings.plain.trendRow"] = "{median, number} this week, {prior, number} last week ({percent, number}% up)",

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
        ["crawler.recent.title"] = "recently updated",
        ["crawler.recent.lede"] = "The newest field changes the crawler has written, across every listed game.",
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

        // ── THE REFERENCE SECTION'S CHROME ────────────────────────────────────────────────────
        // ═════════════════════════════════════════════════════════════════════════════════════
        // The Markdown articles are translated as documents by their own route; this bundle holds
        // only the furniture around them. MSSP, GMCP, TTYPE etc. stay out of the bundle — a
        // translated protocol acronym is destroyed evidence, not a localized string.
        ["reference.title"] = "Reference",
        ["reference.lede"] = "What the codebases are, what the clients do, and what the protocols "
            + "mean. Written by hand and kept in the repository beside the crawler — not a wiki, and "
            + "there is nothing on this page to edit. Every {number} here is a different thing: it "
            + "comes from the catalogue and is recomputed each time you load the page.",

        // The emphasised word is placed by the message, not markup — see Sentences.Place.
        ["reference.lede.number"] = "number",
        ["reference.plain.lede"] = "Hand-written, single-author, and versioned in git. The prose here "
            + "is ours; every number beside it was measured by the crawler and is recomputed on each "
            + "request. This is not a wiki, and there is no way to edit it from this page.",

        // Heading and kind-label are separate ids since inflected languages need different forms.
        ["reference.section.orientation"] = "Start here",
        ["reference.section.codebase"] = "Codebases",
        ["reference.section.client"] = "Clients",
        ["reference.section.protocol"] = "Protocols",
        ["reference.kind.orientation"] = "orientation",
        ["reference.kind.codebase"] = "codebase",
        ["reference.kind.client"] = "client",
        ["reference.kind.protocol"] = "protocol",

        // A hand-written gap is work not done, not something removed — §7.5 applies to a 404 too.
        ["reference.notFound.title"] = "Not found",
        ["reference.notFound.body"] = "No reference page here. This section is hand-written, so a gap "
            + "is work nobody has done rather than something that was removed — {index}.",
        ["reference.notFound.index"] = "see what there is",

        // ── a codebase page's measured half ───────────────────────────────────────────────────
        // The zero is a sentence and never a bare 0 (rule 4): none identified is a statement about
        // this crawler's reach, and reads as a statement about the codebase unless it is spelled out.
        ["reference.codebase.heading"] = "Games running it",
        ["reference.codebase.none"] = "We have not identified any yet. That is a fact about what this "
            + "crawler has measured, not about what exists — a game we have not reached, or whose "
            + "codebase we could not read, is not counted here.",
        ["reference.codebase.listed"] = "{count, plural, one {# listed} other {# listed}}",
        ["reference.codebase.archived"] = "{count, plural, one {# archived} other {# archived}}",

        // Rule 1, in three words, and its own id in every register it appears in. It must not soften
        // into "verified" or "from our data": what it says is that nothing on this line was taken
        // from anybody's self-description.
        ["reference.measuredNeverAsserted"] = "measured, not declared",
        ["reference.codebase.note"] = "Counted from the catalogue on this request, over the same "
            + "filter the link above carries — so this number and that listing are one query and "
            + "cannot drift apart.",

        ["reference.codebase.offered"] = "offered in their handshakes: {protocols}",

        // ── a protocol page's measured half ───────────────────────────────────────────────────
        ["reference.protocol.heading"] = "Measured adoption",
        ["reference.protocol.none"] = "Nothing measured yet.",
        ["reference.protocol.share"] = "of {listed, plural, one {# listed game} other {# listed games}} — {percent}",

        // Rule 5: the remainder is not "games without the protocol" — it mixes unread handshakes.
        ["reference.protocol.remainder"] = "The games not counted here are not games without the "
            + "protocol. A game is counted when we observed its server offering the option in a "
            + "handshake; the rest are servers that did not offer it to us and servers whose "
            + "handshake we have not read, and we cannot tell you which.",
        ["reference.protocol.caption"] = "Games observed offering {protocol} in a handshake, by the "
            + "codebase we identified them as running.",
        ["reference.protocol.column.codebase"] = "codebase",
        ["reference.protocol.column.offered"] = "offered it",
        ["reference.protocol.column.identified"] = "identified",
        ["reference.seeAlso"] = "See also",

        // ── the client capability matrix, which is documentation rather than measurement ───────
        ["reference.capabilities.heading"] = "Capabilities",
        ["reference.capabilities.established"] = "{count, plural, one {# of {total} established from the project's own documentation} other {# of {total} established from the project's own documentation}}",
        ["reference.capabilities.caveat"] = "Read off each project's own documentation, not measured "
            + "by us — a client has no handshake for us to observe. \"{unknown}\" means we looked and "
            + "did not establish it. It never means no.",
        ["reference.capabilities.caption"] = "Client capabilities, each read off the project's own "
            + "documentation. Unknown means we did not establish it, and never that the client lacks "
            + "it.",
        ["reference.capabilities.column.documented"] = "documented",
        ["reference.capabilities.column.source"] = "source",
        ["reference.capabilities.noSource"] = "we did not find one",

        // Unknown means we looked and did not establish it, never "no" — separate ids from the game
        // pages' capability words, which answer a different question (offered on a wire).
        ["reference.capability.yes"] = "yes",
        ["reference.capability.no"] = "no",
        ["reference.capability.unknown"] = "unknown",

        // ── the plain mirror's own wording ────────────────────────────────────────────────────
        ["reference.plain.runsOn"] = "Runs on: {platforms}",
        ["reference.plain.codebase.heading"] = "Games we have identified as running this codebase",
        ["reference.plain.codebase.none"] = "None yet. That is a statement about what we have "
            + "measured, not about what exists — a game we have not reached, or whose codebase we "
            + "could not read, is not counted here.",
        ["reference.plain.codebase.counts"] = "{listed, plural, one {# listed} other {# listed}}, {archived, plural, one {# archived} other {# archived}}",
        ["reference.plain.codebase.offered"] = "Measured in their handshakes: {protocols}",
        ["reference.plain.codebase.nothingOffered"] = "Nothing was offered in any handshake we have "
            + "read from them.",
        ["reference.plain.protocol.share"] = "{offering} of {listed, plural, one {# listed game} other {# listed games}} were observed offering it ({percent})",
        ["reference.plain.protocol.byCodebase"] = "By codebase, of the games we identified",
        ["reference.plain.protocol.row"] = "{offering} of {identified} offered it",
        ["reference.plain.capabilities.unknown"] = "{count, plural, one {# of {total} rows is unknown} other {# of {total} rows are unknown}}: we did not find the project's own documentation saying either way. A short honest table beats a long guessed one.",

        // ══ the owner dashboard and the claim flow ════════════════════════════════════════════
        // These surfaces address the operator in the second person deliberately — do not neutralise.
        // An owner's answer is a DECLARATION beside what the crawler measured, never a replacement
        // of it; an opt-out is honoured and is never a deletion.

        // ── the dashboard's frame ─────────────────────────────────────────────────────────────
        ["account.title"] = "Your games",
        ["account.noDatabase"] = "Accounts need a database behind them, and this site is running on "
            + "the demo fixture.",
        ["account.signInButton"] = "Sign in",
        ["account.signedInAs"] = "Signed in as {name}.",
        ["account.signOut"] = "Sign out",

        // The empty state's sentence places its own link and its own quoted control, so the word
        // order belongs to the language rather than to the markup. {claimControl} names the button
        // on a game's page; it is a separate id from game.claim because that one is the control's
        // own label and this one is prose quoting it — a language that capitalises or declines a
        // quoted control differently has nowhere else to say so.
        ["account.empty.body"] = "You have not claimed anything yet. Find your game in {listing} "
            + "and press {claimControl} on its page.",
        ["account.empty.listing"] = "the listing",
        ["account.empty.claimControl"] = "claim this game",

        // Resigning is not deleting: §7.5 keeps the record, §8.4 lets the same person reclaim it.
        ["account.resigned.lead"] = "Given up.",
        ["account.resigned.body"] = "The record of it is kept, and you can prove control again any "
            + "time by publishing a fresh token.",
        ["account.saved.lead"] = "Saved.",

        // Each says what WE did (stopped republishing, stopped dialling, etc.), never a claim that
        // the game itself changed.
        ["account.saved.thatGame"] = "That game",
        ["account.saved.fields"] = "{game}'s page now shows it as owner-declared.",
        ["account.saved.screenHidden"] = "We have stopped republishing {game}'s connect screen. The "
            + "page says so plainly rather than leaving a hole.",
        ["account.saved.screenShown"] = "{game}'s connect screen is on its page again.",
        ["account.saved.crawlStopped"] = "We have stopped dialling {game}, on every address we have "
            + "for it. Its page keeps everything measured before you asked.",
        ["account.saved.crawlResumed"] = "We are dialling {game} again, from its next turn in the "
            + "schedule.",
        ["account.saved.unlisted"] = "{game} is out of the listing, the rankings and the daily "
            + "figure. Its page and every URL it has ever had go on answering.",
        ["account.saved.relisted"] = "{game} is back in the listing. One probe that answers is all "
            + "it needs to be measured again.",

        // §8.5: nobody edits a measurement, including us.
        ["account.refused.lead"] = "{field} was not changed.",
        ["account.refused.tooLong"] = "These are one-line answers; {max} characters is the most we "
            + "store.",
        ["account.refused.measured"] = "That field is measured. A claim lets you add what MSSP has "
            + "no room for; it never lets anybody edit what we observed, and that includes us.",

        // ── a claimed game's block ────────────────────────────────────────────────────────────
        // "verified {date}" is a fact about our record (when we read the token), not a measurement
        // of the game.
        ["account.claimed.heading"] = "Claimed",
        ["account.claim.verified"] = "verified {date}",
        ["account.claim.verifiedAndSeen"] = "verified {date}, token last seen {seen}",
        ["account.claim.mssp"] = "check your MSSP",
        ["account.claim.coOwners"] = "{count, plural,"
            + " one {Also owned by {names} — who verified a token of their own.}"
            + " other {Also owned by {names} — each having verified a token of their own.}}",
        ["account.coOwner.unnamed"] = "another account",

        // {unknown}/{archived} are the badge's own bytes, not translatable text — it answers one
        // address to everybody.
        ["account.badge.summary"] = "put your player count on your own site",
        ["account.badge.carries"] = "The badge carries the count and when we measured it, because a "
            + "number with no age is the thing this site exists to replace.",
        ["account.badge.states"] = "It says {unknown} rather than nought when we could not count, "
            + "and {archived} if the game stops answering.",
        ["account.badge.json"] = "There is {json} too, if you would rather draw your own.",

        // ── the audit log ─────────────────────────────────────────────────────────────────────
        // beaconMissing reads as an observation, never a warning — absence never revokes (§8.4).
        ["account.history.summary"] = "history",
        ["account.event.issued"] = "token issued",
        ["account.event.reissued"] = "token issued again",
        ["account.event.verified"] = "verified — we read your token",
        ["account.event.beaconSeen"] = "token still published",
        ["account.event.beaconMissing"] = "token not read this time",
        ["account.event.revoked"] = "claim given up",
        ["account.event.expired"] = "token expired unused",
        ["account.event.counterClaimed"] = "another account proved control and took the game over",
        ["account.event.checkRequested"] = "check requested",

        // {word} is the literal an operator types and is never translated — the form only accepts it.
        ["account.resign.summary"] = "give up this claim",
        ["account.resign.confirm"] = "Type {word} to confirm. Nothing is deleted and you can prove "
            + "control again by publishing a fresh token; the game stays claimed if anybody else "
            + "owns it.",
        ["account.resign.button"] = "Give up {game}",

        // ── waiting on a token, and the passkeys ──────────────────────────────────────────────
        ["account.pending.heading"] = "Waiting on a token",
        ["account.pending.dates"] = "token issued {issued}, good until {expires}",
        ["account.passkeys.heading"] = "Passkeys",
        ["account.passkey.unnamed"] = "unnamed",
        ["account.passkey.added"] = "added {date}",
        ["account.passkey.addedOneDevice"] = "added {date} · on one device only",
        ["account.passkey.single"] = "This passkey lives on one device. If you lose it you can "
            + "still get back in by publishing a fresh token on your game, but a second passkey is "
            + "quicker.",
        ["account.passkey.add"] = "Add another passkey",

        // ── claiming a game you run ───────────────────────────────────────────────────────────
        // claim.title is its own id rather than preview.title.claim's, for the reason account.title
        // is: a heading and a browser-tab title are not the same noun phrase.
        ["claim.noGame"] = "No such game",
        ["claim.title"] = "Claim {game}",
        ["claim.noDatabase"] = "Claiming needs a database behind it, and this site is running on "
            + "the demo fixture.",
        ["claim.needAccount"] = "You need an account first. It takes a passkey and a name.",
        ["claim.signIn"] = "Sign in or create an account",
        ["claim.yourGames"] = "Your games",

        // A probe can't tell joining from taking over — both publish the same line, so the choice
        // is made here in words before the token exists.
        ["claim.hasOwners"] = "{count, plural,"
            + " one {This game already has an owner who proved control of the server.}"
            + " other {This game already has # owners who proved control of the server.}}"
            + " You can prove it too — the test is the same either way — but we need to know what "
            + "you mean by it, because we cannot tell from the token.",
        ["claim.join.button"] = "I run it too — add me as an owner",
        ["claim.join.note"] = "Everyone keeps their claim. This is two people running one game.",
        ["claim.assume.button"] = "I have taken it over — transfer it to me",
        ["claim.assume.note"] = "{count, plural,"
            + " one {When your token verifies, the existing claim is revoked and the game is yours.}"
            + " other {When your token verifies, the existing claims are revoked and the game is yours.}}"
            + " They will see why in their own history. Nothing is deleted, and they can prove "
            + "control again the same way you are about to.",

        // One id per channel, not one with a {channel} hole — the three sources decline differently.
        ["claim.verified.lead"] = "Verified.",
        ["claim.verified.viaMssp"] = "We read your token from the game's MSSP report on {date}.",
        ["claim.verified.viaScreen"] = "We read your token from the connect screen on {date}.",
        ["claim.verified.viaDns"] = "We read your token from a TXT record in your DNS on {date}.",
        ["claim.verified.leaveIt"] = "Leave the token where it is. It doubles as an identity "
            + "signal, so this game stays recognisable if it moves host or changes name. Removing "
            + "it will not un-claim you.",

        // Its own id rather than the one above, because the sentence above is not true of DNS: a
        // TXT record is never read off a probe, so it is not §7.3's identity beacon. Telling a DNS
        // claimant their record keeps the game recognisable would be the site claiming a mechanism
        // it does not have.
        ["claim.verified.leaveItDns"] = "Leave the record where it is. Removing it will not "
            + "un-claim you, and while it is there we can see you still run this game without "
            + "connecting to it.",

        ["claim.publish"] = "Publish this token anywhere the game shows it to an anonymous "
            + "connection. The next probe picks it up, which proves you can write to that server.",
        ["claim.transfer.lead"] = "This is a transfer.",
        ["claim.transfer.body"] = "{count, plural,"
            + " one {When we read this token, the current owner's claim on this game is revoked and it becomes yours.}"
            + " other {When we read this token, the current owners' claims on this game are revoked and it becomes yours.}}",
        ["claim.either.heading"] = "Any of these will do",
        ["claim.mssp.heading"] = "An MSSP variable",
        ["claim.mssp.note"] = "In {codebase} that is a line in {file}; every codebase with MSSP has "
            + "an equivalent.",
        ["claim.mssp.aliases"] = "{aliases} are accepted too.",
        ["claim.screen.heading"] = "A line on the connect screen",
        ["claim.screen.note"] = "Anywhere in the screen, and colour codes around it are fine.",

        // §8.3's third channel. The port qualifier gets a sentence of its own because it is the
        // whole reason the channel is sound: a TXT record proves control of a hostname, and naming
        // the port is how the publisher says which listener they are speaking for.
        ["claim.dns.heading"] = "A DNS TXT record",
        // The "on the spot" half lives here rather than on claim.check.can, which is rendered for
        // every game including the ones with no address on record and therefore no DNS section — a
        // button that promised to read a TXT record beside instructions that never mentioned one
        // would be the page describing a channel it did not offer.
        ["claim.dns.note"] = "The port is not optional. It says which listener on this host the "
            + "record speaks for, and one without it proves nothing — a domain can carry several "
            + "unrelated games. We read this without connecting to your game, so it works while "
            + "the server is down, and the button below reads it on the spot.",
        ["claim.dns.noAssume"] = "A TXT record cannot complete a transfer. Whoever controls a "
            + "domain is not always whoever runs the game on it, so taking a game over has to be "
            + "proved from the server itself — use one of the two above.",
        ["claim.then.heading"] = "Then",
        ["claim.then.body"] = "We check on the ordinary crawl schedule. This token is good until "
            + "{date}. Come back any time; nothing needs writing down.",

        // Neither sentence may promise a probe — pressing the button moves it up the queue, but the
        // crawler still dials on its own schedule under CRAWL DELAY.
        ["claim.check.button"] = "Look sooner",
        ["claim.check.can"] = "Brings your game to the front of the queue. We dial on our own "
            + "schedule, so this is sooner rather than now.",
        ["claim.check.rationed"] = "Just asked. Try again in a few minutes — it is rationed because "
            + "it dials a real server sooner than we would have.",

        // ── what a claim actually grants (the owner panel, §8.5 and §11) ──────────────────────
        // An owner's answer is DECLARED, stored beside what the crawler measured — never replacing,
        // hiding, or silencing a measurement.
        ["owner.declare.heading"] = "What only you can tell us about {game}",
        ["owner.declare.lede"] = "These are the things MSSP has no field for. They appear on your "
            + "game's page as {declared}, with the date you last confirmed them, beside what we "
            + "measured — never instead of it. Nothing measured can be edited from here, by you or "
            + "by us.",
        ["owner.field.declared"] = "declared {age}. Empty this box to withdraw it — the record of "
            + "what it said is kept either way.",
        ["owner.save"] = "Save what you declared",

        ["owner.override.heading"] = "What {game} reports, and what you would rather we showed",
        ["owner.override.lede"] = "Your MSSP is what every crawler reads, and we go on showing it "
            + "beside anything you put here — an answer of yours does not hide one of your game's.",
        ["owner.override.nothingMeasured"] = "Nothing measured can be edited from here: not a "
            + "player count, not a capability, not an hour of reachability.",
        ["owner.override.fixItThere"] = "If a line below is wrong in your {file}, fixing it there "
            + "fixes it everywhere.",
        ["owner.report.value"] = "your game reports {value}, confirmed {age}",
        ["owner.report.none"] = "your game reports nothing here",
        ["owner.rename.note"] = "Changing the name changes what {game} is listed as and the "
            + "address of its page. The old address goes on working for ever — every URL this game "
            + "has ever had redirects to its current one — and clearing the box hands the name back "
            + "to whatever your MSSP says.",

        ["owner.screen.heading"] = "Your connect screen",
        ["owner.screen.suppressed"] = "We are not republishing it. The page says so plainly rather "
            + "than leaving a hole, and the crawler goes on reading it — it is how we recognise "
            + "your game if it moves.",
        ["owner.screen.show"] = "Show it again",
        ["owner.screen.shown"] = "We show it because your server sends it to every anonymous "
            + "connection. If you would rather we did not, say so and we stop. We will not ask why.",
        ["owner.screen.stop"] = "Stop showing our connect screen",

        // §11. An opt-out is honoured, never a deletion — and the empty hours it leaves may not be
        // given a cause, because "you asked us to stop" is OUR fact and not a measurement of the
        // game (rule 5, rule 2). {ourFact} is placed by the message so a language can put the
        // emphasised clause where it belongs.
        ["owner.crawl.heading"] = "Being crawled",
        ["owner.crawl.stopped"] = "We have stopped. Nothing on {game} is dialled, and the page "
            + "keeps everything measured before you asked — the empty hours name no cause, because "
            + "{ourFact} is our fact and not a measurement of your game.",
        ["owner.crawl.ourFact"] = "you asked us to stop",
        ["owner.crawl.resume"] = "Start crawling us again",
        ["owner.crawl.standing"] = "This one came from your own server rather than from here — "
            + "{routes}. To be crawled again, stop publishing it; we will hear that on the next "
            + "cycle.",
        ["owner.crawl.route.mssp"] = "your MSSP report publishes {variable}",
        ["owner.crawl.route.dns"] = "a {label} TXT record asks us to stop",
        ["owner.crawl.route.recorded"] = "a request we recorded",
        // Neither state, and shown as neither: rounding this to "stopped" would tell an owner we
        // had left them alone while we went on dialling the port that is still open.
        ["owner.crawl.partial"] = "We have stopped on {stopped} and are still dialling {dialling}. "
            + "That is usually a port added after the opt-out.",
        ["owner.crawl.stopAll"] = "Stop on every address too",
        ["owner.crawl.dialling"] = "We dial {game} on a schedule and read what any anonymous "
            + "connection is shown. If you would rather we did not, say so and we stop — within "
            + "one cycle, on every address we have for you, and we will not ask why.",
        ["owner.crawl.selfService"] = "Nothing already measured is deleted: your page keeps its "
            + "history and its URL, and one probe after you take this back starts it again. You "
            + "can also say it without us, in your own config — {mssp} in MSSP, or a {dns} TXT "
            + "record — and we honour those whether or not anybody has ever claimed the game here.",
        ["owner.crawl.stop"] = "Stop crawling us",

        // Distinct from crawl opt-out: nothing is deleted here either, the page just stops being
        // somewhere a reader arrives by browsing.
        ["owner.listing.heading"] = "Being listed",
        ["owner.listing.unlisted"] = "{game} is out of the listing, out of the rankings and out of "
            + "the daily figure. Its page and every URL it has ever had go on answering, and "
            + "everything measured before you asked is still on it. Nothing was deleted; it is "
            + "simply not somewhere a reader arrives by browsing.",
        ["owner.listing.relist"] = "Put us back in the listing",
        ["owner.listing.probeRelists"] = "One probe that answers does this too. While your opt-out "
            + "stands we do not dial, so nothing will — but the day you take it back, the address "
            + "comes up within a week and the probe that gets an answer puts you back. You do not "
            + "have to ask us twice.",
        ["owner.listing.mayUnlist"] = "We have stopped dialling you, and your page is still in the "
            + "listing with what we measured before that. If you would rather it were not, say so "
            + "and it comes out — of the listing, the rankings and the daily figure.",
        ["owner.listing.reversible"] = "Nothing is deleted and nothing breaks: the page answers, "
            + "every URL it has ever had still redirects to it, and anyone you send there sees it. "
            + "It stops being somewhere a reader can arrive by browsing. Reversible from here, and "
            + "by any probe that answers after you take your opt-out back.",
        ["owner.listing.unlist"] = "Take us out of the listing too",
        // ══ WHAT WE COULD MEASURE ═════════════════════════════════════════════════════════════
        // Two switches over the two reasons a listing row carries no number. Every string here
        // describes our reach, never the game (rule 5).
        ["facet.group.measure"] = "what we could measure",

        ["facet.measure.note"] = "Hiding these takes them out of your listing; it does not mean the "
            + "game is empty.",

        // Named for what we did, never what the game is — "could not count" not "no players".
        ["facet.group.uncounted"] = "could not count",
        ["facet.group.unreachable"] = "could not reach",

        ["facet.excluded.uncounted"] = "hidden from this listing",
        ["facet.excluded.unreachable"] = "hidden from this listing",

        ["facet.plain.marks"] = "In the left column, * is a value this listing is filtered to and - "
            + "is one it is filtered against. Both are choices in the query, not facts about a game.",
    };

    /// <summary>
    /// The bundles, by tag.
    /// </summary>
    /// <remarks>
    /// Only English is complete by design — no locale is offered before it's human-translated.
    /// <c>qps-ploc</c> is a pseudolocale that exercises routing/fallback/plural selection/width
    /// budget with something real. <c>ru-x-canary</c> is deliberately incomplete so a missing plural
    /// form fails the build instead of reaching a reader.
    /// </remarks>
    private static readonly Dictionary<string, Dictionary<string, string>> TestBundles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Generated from English so it cannot fall behind.
            ["qps-ploc"] = English.ToDictionary(e => e.Key, e => Pseudo(e.Value), StringComparer.Ordinal),

            // Missing its `few`/`many` branches on purpose — the completeness test turns that into
            // a build failure.
            ["ru-x-canary"] = new(StringComparer.Ordinal)
            {
                ["facet.count"] = "{count, plural, one {# игра} few {# игры} many {# игр} other {# игры}}",
                ["listing.total"] = "{count, plural, one {# игра} other {# игр}}, каждый факт измерен.",
                ["provenance.count.measured"] = "измерена",
                ["provenance.game.measured"] = "измерено",
                ["provenance.capability.measured"] = "измерены",
                ["kicker.measured"] = "ИЗМЕРЕНО",
            },
        };

    /// <summary>
    /// One message, rendered for a locale — or the English, where that locale has no approved one.
    /// </summary>
    /// <remarks>
    /// The fallback shows raw English rather than a smoothed-over approximation of a locked string
    /// — a reader should be able to tell a claim hasn't been translated yet.
    /// </remarks>
    public static string For(string tag, string id, IReadOnlyDictionary<string, object?>? args = null)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(id);

        var pattern = Pattern(tag, id)
            ?? throw new KeyNotFoundException($"No message '{id}' in any bundle, including English.");

        return IcuMessage.Format(pattern, tag, args);
    }

    /// <summary>
    /// The same, with the arguments named inline rather than built into a dictionary first.
    /// </summary>
    /// <remarks>
    /// <c>StringComparer.Ordinal</c> matters: an argument name is a token matched exactly, and a
    /// case-folding dictionary would answer a lookup the parser never asked for.
    /// </remarks>
    public static string Say(string tag, string id, params (string Key, object? Value)[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return For(tag, id, args.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal));
    }

    /// <summary>A count and its noun, agreeing — the commonest call by a long way.</summary>
    public static string Count(string tag, int count) =>
        For(tag, "facet.count", new Dictionary<string, object?> { ["count"] = count });

    /// <summary>The raw pattern a locale would use, English included, or null.</summary>
    public static string? Pattern(string tag, string id)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(id);

        return Own(tag, id) ?? English.GetValueOrDefault(id);
    }

    /// <summary>Whether a locale carries its own text for an id, rather than falling back.</summary>
    public static bool HasOwn(string tag, string id) => Own(tag, id) is not null;

    /// <summary>
    /// What a locale itself says for an id — from its resx, or from a test bundle — or null.
    /// </summary>
    /// <remarks>
    /// Uses a raw <c>ResourceManager</c> rather than <see cref="IStringLocalizer"/>: the latter
    /// answers a missing key with the key itself rather than null, which would render
    /// <c>facet.count</c> to a reader and call it a translation.
    /// </remarks>
    private static string? Own(string tag, string id)
    {
        if (TestBundles.TryGetValue(tag, out var bundle))
        {
            return bundle.GetValueOrDefault(id);
        }

        if (string.Equals(tag, Locales.SourceTag, StringComparison.OrdinalIgnoreCase))
        {
            return English.GetValueOrDefault(id);
        }

        if (Culture(tag) is not { } culture)
        {
            return null;
        }

        // tryParents: false — GetString otherwise walks up to neutral resources and answers English
        // for an untranslated locale, when the caller here is asking "does this locale have its own
        // words for this id".
        try
        {
            return Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false)
                ?.GetString(id);
        }
        catch (MissingManifestResourceException)
        {
            return null;
        }
    }

    /// <summary>
    /// The satellite assemblies, keyed off the marker type so the base name cannot drift.
    /// </summary>
    private static readonly ResourceManager Resources = new(typeof(Web.Resources.Messages));

    private static CultureInfo? Culture(string tag)
    {
        try
        {
            return CultureInfo.GetCultureInfo(tag);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    /// <summary>The source text for an id, which is what a resx has to agree with.</summary>
    public static string? Source(string id) => English.GetValueOrDefault(id);



    /// <summary>Every id the site says, in the order the source bundle declares them.</summary>
    public static IReadOnlyList<string> Ids { get; } = [.. English.Keys];

    /// <summary>
    /// The ids a locale has not translated yet. A locale may not move to
    /// <see cref="LocaleStatus.Shipped"/> while any locked id is in this list.
    /// </summary>
    public static IReadOnlyList<string> MissingFor(string tag) =>
        [.. Ids.Where(id => !HasOwn(tag, id))];

    /// <summary>
    /// Accented, expanded English — a language nobody speaks, which is the point.
    /// </summary>
    /// <remarks>
    /// Accents prove a string came through the pipeline rather than being hard-coded; padding
    /// exercises the 1.4x width budget (German/Russian run 30–40% longer than English UI nouns).
    /// </remarks>
    private static string Pseudo(string pattern)
    {
        var b = new System.Text.StringBuilder(pattern.Length * 2);
        var depth = 0;

        foreach (var c in pattern)
        {
            if (c == '{') { depth++; b.Append(c); continue; }
            if (c == '}') { depth--; b.Append(c); continue; }

            // Inside braces is ICU syntax — accenting it would make the message unparseable.
            b.Append(depth > 0 ? c : Accent(c));
        }

        return "⟦" + b + "⟧";
    }

    private static char Accent(char c) => c switch
    {
        'a' => 'á', 'e' => 'é', 'i' => 'í', 'o' => 'ó', 'u' => 'ú', 'n' => 'ñ', 'c' => 'ç',
        'A' => 'Á', 'E' => 'É', 'I' => 'Í', 'O' => 'Ó', 'U' => 'Ú', 'N' => 'Ñ', 'C' => 'Ç',
        _ => c,
    };
}
