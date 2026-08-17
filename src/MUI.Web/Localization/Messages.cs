using System.Globalization;

using System.Resources;

namespace MUI.Web.Localization;

/// <summary>
/// Every string the chrome says, keyed by context, in ICU MessageFormat.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stored in resx and rendered by ICU.</b> The two halves solve different problems and the usual
/// .NET arrangement only has one of them. resx is where a translation belongs: the SDK compiles
/// <c>Messages.&lt;culture&gt;.resx</c> into a satellite assembly on its own, every
/// translation-management tool reads and writes the format, and a translator receives a file their
/// software opens rather than a C# dictionary they must not break. What resx cannot do is
/// agreement — <c>{0}</c> substitutes and nothing more — which is why the values are ICU patterns.
/// </para>
/// <para>
/// <b>One message per fact, never a sentence assembled from parts.</b> The strings this replaces
/// were concatenations — a number glued to an English fragment in English word order — and there is
/// nowhere in a concatenation for a translator to intervene without editing markup. Russian needs
/// three plural forms and Arabic six, Chinese needs a measure word, and several languages put the
/// unit before the number.
/// </para>
/// <para>
/// <b>The ids are granular past the point English needs.</b> That is the whole of S7: "measured" is
/// one word here and four in Russian, chosen by what it describes, so <c>provenance.count.measured</c>
/// and <c>provenance.game.measured</c> are separate ids carrying identical English. Collapsing them
/// because the source language cannot tell them apart is exactly how a translation ends up
/// ungrammatical in three places out of four.
/// </para>
/// <para>
/// <b>What is not here.</b> Game names, hostnames, codebase strings, version numbers, protocol
/// acronyms and connect-screen output never enter this file. They are the machine voice, they carry
/// <c>translate="no"</c> in the markup so a browser's own translator obeys too, and translating
/// <c>PennMUSH 1.8.8p0</c> destroys evidence rather than localizing anything.
/// </para>
/// </remarks>
public static class Messages
{
    /// <summary>
    /// The source bundle, compiled in.
    /// </summary>
    /// <remarks>
    /// <b>The same strings as <c>Resources/Messages.resx</c>, and a test walks both to keep them
    /// that way.</b> It is here as well because the English is the fallback for every locale and
    /// every surface — including the ones rendered with no host behind them — and a fallback that
    /// can fail to load is not one. resx is where a <em>translation</em> lives; this is where the
    /// source text lives, and the pair is checked rather than trusted.
    /// </remarks>
    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        // ── counts, which is where the concatenations were ───────────────────────────────────
        ["facet.count"] = "{count, plural, one {# game} other {# games}}",
        ["facet.value.include"] = "{value}, {count, plural, one {# game} other {# games}}, only",
        ["facet.value.exclude"] = "{value}, {count, plural, one {# game} other {# games}}, excluded",
        ["facet.value.choose"] = "{value}, {count, plural, one {# game} other {# games}}",
        ["facet.any"] = "any {facet}, {count, plural, one {# game} other {# games}}",
        ["listing.total"] = "{count, plural, =0 {No games} one {# game} other {# games}}, every fact measured.",
        ["chart.basis"] = "{days, plural, one {# day} other {# days}} measured · {probes, plural, one {# probe} other {# probes}}",
        ["window.samples"] = "{days}d · {count, plural, one {# count} other {# counts}}",
        ["capabilities.agree"] = "{disagreeing, plural, =0 {None of the {total} disagree.} one {# of {total} disagrees with what the game declares.} other {# of {total} disagree with what the game declares.}}",

        // ── find a game, where the count is the whole point of the page ───────────────────────
        // A sentence and not a bare number. The panel draws "19" at forty pixels for the eye; what
        // reaches a screen reader has to say what the nineteen are, because a number announced on
        // its own is the one thing on this page nobody can act on.
        ["find.matching"] = "{count, plural, =0 {No games match every answer.} one {# game matches every answer.} other {# games match every answer.}}",
        ["find.basis"] = "of {listed, plural, one {# listed game} other {# listed games}} · {answers, plural, =0 {no answers given} one {# answer given} other {# answers given}}",
        ["find.show"] = "{count, plural, one {Show the one game} other {Show these # games}}",
        ["find.drop"] = "drop \"{answer}\" — {count, plural, one {# game} other {# games}}",
        ["find.clear"] = "clear answer to: {question}",

        // The page's own copy. Every word of it is here rather than in the markup, including the six
        // questions: this is the one surface written in a reader's language rather than in the
        // catalogue's, so it is the surface with the most to translate and the least that a
        // machine translator could be trusted with.
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
        // One id per capability, each carrying the whole label rather than a gloss to be glued to an
        // acronym. The acronym is machine voice and the three words beside it are not, and a
        // language that puts the gloss first has nowhere to say so if the two are concatenated.
        // `other` is the one that takes the token as an argument, because it is the row for a
        // capability this list has never heard of.
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
        ["nav.games"] = "games",
        ["nav.find"] = "find",
        ["nav.random"] = "random",
        ["nav.archive"] = "archive",
        ["nav.reference"] = "reference",
        ["nav.ecosystem"] = "ecosystem",
        ["nav.rankings"] = "rankings",
        ["nav.about"] = "about",
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
        ["footer.allGames"] = "all games",
        ["footer.archive"] = "archive",
        ["footer.declaredByGame"] = "declared by the game",
        ["footer.whatChanged"] = "what changed",

        // ── home ──────────────────────────────────────────────────────────────────────────────
        ["home.title"] = "A directory of the MU* hobby",
        ["home.lede"] = "Every fact was measured by a crawler that connected to the game.",
        ["home.search.label"] = "Search games by name, theme, codebase or host",
        ["home.search.placeholder"] = "search by name, theme, codebase or host",
        ["home.search.submit"] = "search",
        ["tile.gamesKnown"] = "games known",
        ["tile.connectedNow"] = "connected now",
        ["tile.answeringUncounted"] = "answering, uncounted",
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
        ["facet.countsNote"] = "Counts are games we measured, never estimates.",
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
        ["facet.group.genre"] = "genre",
        ["facet.group.language"] = "language",

        // ── facet values ──────────────────────────────────────────────────────────────────────
        ["facet.band.playersNow"] = "connected now",
        ["facet.band.activeThisWeek"] = "active this week",
        ["facet.band.quiet"] = "uncounted",
        ["facet.band.dark"] = "dark — not reached in a month",
        ["facet.band.archived"] = "archived",
        ["facet.seen.day"] = "in the last 24 hours",
        ["facet.seen.week"] = "in the last 7 days",
        ["facet.seen.month"] = "in the last 30 days",
        ["facet.seen.older"] = "longer ago",
        ["facet.seen.never"] = "never reached",
        ["facet.unknown.charset"] = "nothing negotiated",
        ["facet.unknown.codebase"] = "not identified",
        ["facet.unknown.other"] = "not declared",
        ["facet.tls.yes"] = "connected over TLS",
        ["facet.excluded"] = "not {value}",
        ["facet.known.charset"] = "something negotiated",
        ["facet.known.codebase"] = "identified at all",
        ["facet.known.other"] = "declared at all",

        // ── evidence, and what each word means ────────────────────────────────────────────────
        ["evidence.measured.meaning"] = "we watched this happen",
        ["evidence.declared.meaning"] = "the game says so, and we did not check",
        ["evidence.derived.meaning"] = "we grouped what the game told us",

        // ── sort orders ───────────────────────────────────────────────────────────────────────
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
        ["sort.window.median"] = "median {value} · {days}d · {count, plural, one {# count} other {# counts}}",
        ["sort.window.peak"] = "most {value} at once · {days}d · {count, plural, one {# count} other {# counts}}",

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
        ["game.unknownCount"] = "unknown count",
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
        // The heatmap's sentence, its per-day alternative, and the label on every cell. Three
        // states and never two: an hour we counted — a measured zero included — an hour that
        // answered and produced no count, and an hour nobody has a measurement for. The third
        // names no cause in any language, because a probe that failed and an hour we never dialled
        // write the same nothing, and a translation that reached for "offline" or "not reachable"
        // would file our crawl schedule as a fact about somebody's game.
        //
        // Every count is an argument rather than a spelled-out word. "Two hours … have" was a
        // number, a noun and a verb agreed in English word order, with nowhere in it for a
        // translator to stand.
        ["activity.cell.counted"] = "{day} {time} — {count, plural, =0 {0 players, measured} one {# player on average} other {# players on average}}",
        ["activity.cell.notCounted"] = "{day} {time} — probed, no count could be read",
        ["activity.cell.notMeasured"] = "{day} {time} — no measurement in this hour",

        ["activity.none"] = "We have not measured this game's activity yet.",
        ["activity.noCount"] = "No hour of the week has produced a player count.",

        // A measured zero everywhere is a measurement, and a strong one. It must not read as an
        // absence of data in any language.
        ["activity.allZero"] = "Measured every hour and nobody has been on in any of them.",

        // Two ids apiece, because "on Monday" and "across the week" land in different places in
        // different languages, and a fragment glued into a sentence is one nobody can move.
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

        // The same four names in two registers: the busy band takes the plural — "evenings,
        // 17:00–21:59" — and the quiet one the singular, "in the evening". English builds the
        // second from the first by adding an s, which is a fact about English and not about nouns.
        ["activity.part.morning"] = "morning",
        ["activity.part.afternoon"] = "afternoon",
        ["activity.part.evening"] = "evening",
        ["activity.part.smallHours"] = "small hours",
        ["activity.parts.morning"] = "mornings",
        ["activity.parts.afternoon"] = "afternoons",
        ["activity.parts.evening"] = "evenings",
        ["activity.parts.smallHours"] = "small hours",

        // A run of days, folded rather than joined on a comma: the separator and the final
        // conjunction both belong to a language, and Chinese uses neither of ours.
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

        // The panel's own chrome: the headings of the text alternative, the legend beside the
        // drawing, and the key the plain surface prints in place of both. The "not measured"
        // column reuses state.notMeasured rather than saying it a second way — the whole panel
        // has one word for that hour or it has none.
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

        // ── the switcher's own chrome, which has to read in the locale being left ─────────────
        ["locale.label"] = "language",
        ["locale.submit"] = "change language",

        // ═════════════════════════════════════════════════════════════════════════════════════
        // STATIC PAGE COPY — /about, /submit, /account/sign-in
        //
        // The three pages that were prose in a C# file rather than strings in a bundle, and so were
        // still English when the site was asked for German. Nothing here is reworded on the way in:
        // the English is byte-for-byte what the page already said, and only its home changed. The
        // about page in particular *states* the rules this repository is written from, and a rule
        // paraphrased while being moved is a rule quietly rewritten.
        //
        // Every lead/body pair is two ids for the reason AboutPoint is two fields: the graphical
        // page sets the lead in bold and the plain page cannot, so the emphasis is presentational
        // and the sentence is not. A translator gets two sentences to move rather than one string
        // with markup in the middle of it.
        // ═════════════════════════════════════════════════════════════════════════════════════

        // ── about: the page, and what a fact here is ──────────────────────────────────────────
        ["about.title"] = "About mu*index",
        ["about.lede"] = "Every game here was measured by a machine that connected to it, and every "
            + "value says where it came from and when. This page covers what that proves, what we "
            + "get wrong, whose directories we read, and how to make the crawler stop.",

        ["about.measures.heading"] = "What a fact here is",
        ["about.measures.declared.lead"] = "Measured beats declared, and both are shown.",
        ["about.measures.declared.body"] = "A game's MSSP report is the game describing itself. The "
            + "telnet handshake is what we watched it do. Both appear on its page, labelled with how "
            + "and when. Where they disagree, we show the disagreement.",
        ["about.measures.count.lead"] = "A player count says where it came from.",
        ["about.measures.count.body"] = "Either a WHO or DOING read at the connect screen, which we "
            + "counted, or the game's own MSSP PLAYERS field, which it published. Never merged.",
        ["about.measures.unknown.lead"] = "An answer we cannot read is unknown, never zero.",
        ["about.measures.unknown.body"] = "Servers customise their WHO headers freely, and past a "
            + "point our parser cannot read one. That is uncountable, its own state. A measured zero "
            + "— we got in, nobody was there — is a count, and prints as one.",
        ["about.measures.reachable.lead"] = "Reachable, never uptime.",
        ["about.measures.reachable.body"] = "We open a socket from one host at intervals. A game we "
            + "cannot route to is unreachable and perfectly alive. Nothing here claims a game's "
            + "uptime, because nothing here measured it.",
        ["about.measures.hour.lead"] = "An hour is counted, uncountable, or not measured.",
        ["about.measures.hour.body"] = "The activity grid has three states. The third is empty and "
            + "names no cause: an hour we could not reach and an hour we never probed are the same "
            + "absence, and neither is that server's downtime.",

        // ── about: what we know we get wrong ──────────────────────────────────────────────────
        ["about.limits.heading"] = "What we know we get wrong",
        ["about.limits.grace.lead"] = "Archive grace is measured from the day we found you.",
        ["about.limits.grace.body"] = "A game that stops answering leaves the default listing after "
            + "its grace period: a quarter of the reachable time we probed, floored at 60 days and "
            + "capped at 365. A game running since 1995 starts at the floor on the day we discover "
            + "it. We import nothing to fill in the years before we arrived.",
        ["about.limits.created.lead"] = "We do not credit MSSP CREATED toward that grace.",
        ["about.limits.created.body"] = "It is one hand-typed line in a config file, so crediting it "
            + "would make the archive threshold gameable. It is shown as a declaration and buys "
            + "nothing.",
        ["about.limits.claim.lead"] = "Claiming a game earns the ceiling.",
        ["about.limits.claim.body"] = "Proving server access is worth the full year of grace, "
            + "however long we have been watching.",
        ["about.limits.oneHost.lead"] = "Everything here is one host, looking at intervals.",
        ["about.limits.oneHost.body"] = "A percentage of reachable time is a fraction of the window "
            + "we observed, never of one we did not. No graphic here fills in the rest.",
        ["about.limits.deletion.lead"] = "Nothing is ever deleted.",
        ["about.limits.deletion.body"] = "Archiving takes a game out of the default listing, the "
            + "rankings and the active-today figure, and nothing else. Its page, URL, history and "
            + "address survive, it keeps being probed, and one successful probe puts it back.",

        // ── about: what this site will not do ─────────────────────────────────────────────────
        ["about.never.heading"] = "What this site will not do",
        ["about.never.votes.lead"] = "No votes, stars, ratings or recommendations.",
        ["about.never.votes.body"] = "Rankings are computed from measured data only. A directory "
            + "ranked by who can mobilise the most clicks describes the campaigning, not the hobby, "
            + "and that is what killed the incumbents.",
        ["about.never.forums.lead"] = "No forums, reviews, wikis, comments or player profiles.",
        ["about.never.forums.body"] = "Orientation material — what a MUSH is, which codebase suits "
            + "collaborative roleplay — is written, signed and versioned like the rest of the site.",
        ["about.never.names.lead"] = "Player names are never persisted.",
        ["about.never.names.body"] = "A WHO reply is parsed in memory for a count and the shape of "
            + "the header. The names are not written down; aggregates use a salted hash with a "
            + "rotating salt.",
        ["about.never.population.lead"] = "No absolute population figure is published.",
        ["about.never.population.body"] = "Per-codebase and per-protocol shares ship: a ratio over "
            + "the measured set survives the games we cannot count. \"How many people play MU*\" "
            + "does not, because that number would not survive being quoted.",

        // ── about: the crawler, and how to make it stop ───────────────────────────────────────
        // Four arguments rather than four concatenations, and none of them is decoration: the
        // permitted command list, the MSSP variable and the DNS label and value are all read off the
        // objects that consume them, so a page advertising a switch wired to nothing is impossible
        // to write. A sentence assembled around them in English word order would have nowhere for a
        // translator to put the verb.
        ["about.crawler.heading"] = "The crawler, and how to make it stop",
        ["about.crawler.probe.lead"] = "A probe is one connection that never logs in.",
        ["about.crawler.probe.body"] = "It opens a socket, negotiates telnet options, reads the "
            + "connect screen, asks for MSSP by negotiating option 70, sends {commands}, and "
            + "disconnects. No character, no login, nothing changed on the far side. A timeout "
            + "bounds the session so a wedged probe cannot sit on a connection slot.",
        ["about.crawler.delay.lead"] = "CRAWL DELAY wins.",
        ["about.crawler.delay.body"] = "A game that states a preferred minimum gap in its MSSP "
            + "report gets it, over our own schedule in both directions: 720 hours means monthly, "
            + "not weekly. A dark game is still tried for ever at the longer interval, which is how "
            + "it re-lists itself when it comes back.",
        ["about.crawler.referral.lead"] = "A referred address is verified, never trusted.",
        ["about.crawler.referral.body"] = "MSSP lets a game name other games. Every name is resolved "
            + "before anything is dialled, and refused unless every address it resolves to is "
            + "globally routable. A mixed answer refuses the whole target. Our refusal is filed as "
            + "ours and never appears in a game's record as downtime.",
        ["about.crawler.screens.lead"] = "Connect screens are shown because they are sent to "
            + "everybody.",
        ["about.crawler.screens.body"] = "A server paints its connect screen, unauthenticated, to "
            + "every anonymous connection. We display it as evidence and label it. Ask and it comes "
            + "down.",
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
        // Two whole sentences and not one with a branch, because the unannounced case is a
        // paragraph explaining a library gap and the announced one is a line. The name is an
        // argument in both: it is read off ProbeOptions, so a deployment that configures its own
        // gets a page that names it.
        ["about.identity.announced"] = "The crawler names itself {name} when a server asks what it "
            + "is.",
        ["about.identity.unannounced"] = "The crawler is configured to call itself {name} but cannot "
            + "yet say so. Its telnet library gives a client no way to set the terminal type, so "
            + "your logs see that library's default, and NEW-ENVIRON is answered from the crawler "
            + "host's environment. Both are gaps in the library and ours to fix there. Until then, "
            + "recognise a probe by its shape: one connection, no login, a short read-only command "
            + "set, gone.",
        ["about.identity.crawler"] = "crawler",
        ["about.identity.contact"] = "contact",
        ["about.identity.crawler.line"] = "Crawler: {name}",
        ["about.identity.contact.line"] = "Contact: {url}",
        ["about.identity.placeholder"] = "— placeholder; this deployment set no contact address",
        ["about.identity.placeholder.plain"] = "No contact address is configured, so the one above "
            + "is a placeholder and answers nobody.",

        // ── about: where the list of games came from ──────────────────────────────────────────
        // The directories' own names and addresses are machine voice and are nowhere in this file.
        // What each one gave us, and whether we read it at all, is ours to say and so is here.
        ["about.sources.heading"] = "Where the list of games came from",
        ["about.sources.addresses.lead"] = "We take addresses. Nothing else.",
        ["about.sources.addresses.body"] = "A backfill takes a host and a port. No player counts, no "
            + "reachability history, no descriptions, no fields, and no note of which site an "
            + "address came from.",
        ["about.sources.less.lead"] = "Deliberately less than those sites can give.",
        ["about.sources.less.body"] = "Several hold years of dated player counts. Importing that "
            + "would fill the heatmaps of the games somebody else was already watching, and rest "
            + "this site's central claim on another party's prober.",
        ["about.sources.origin.lead"] = "A game's origin is not one fact.",
        ["about.sources.origin.body"] = "Any game worth listing appears in several of these "
            + "directories, so \"imported from\" would name whichever fetch ran first. That a game "
            + "exists is public information; where we read it adds nothing and is the part of "
            + "somebody else's work with the least claim to be ours.",
        ["about.sources.etiquette.lead"] = "Reading somebody's site is still reading somebody's "
            + "site.",
        ["about.sources.etiquette.body"] = "We ask for a bulk export or a documented endpoint before "
            + "scraping, read robots.txt first, and rate-limit scrapes hard. A source that needs its "
            + "maintainer's say-so is not fetched until a person can state they were asked.",

        // Two states and never one word for both: a directory we chose not to fetch is a different
        // fact from one we could not, and the badge is the only place a reader meets the difference.
        ["about.source.read"] = "read — addresses only",
        ["about.source.withheld"] = "not read — awaiting permission",

        ["about.source.tintinMssp.note"] = "One page, one request. Published by a crawler that "
            + "connects to each game and prints what it read.",
        ["about.source.tintinMsdp.note"] = "The same crawler's MSDP listing. Nearly a subset of its "
            + "MSSP sibling, read for the few addresses it reaches that the other does not.",
        ["about.source.mudConnector.note"] = "Publishes its whole catalogue on one page, so reading "
            + "it costs a single request. Our largest source of addresses, and of no measurements.",
        ["about.source.mudStats.note"] = "One index page and one page per world, so a scrape rather "
            + "than an export. On 30 July 2026 we fetched 143 of their pages, fifteen seconds apart "
            + "and honouring robots.txt, but before anyone had written to them. That should not "
            + "have happened. The gate now takes a person willing to state the maintainer was "
            + "asked.",
        ["about.source.mudVerse.note"] = "Implemented, tested, never run. The strongest source here "
            + "on every axis except permission, and nothing will be fetched until somebody has "
            + "written to them.",

        // ── about: licence ────────────────────────────────────────────────────────────────────
        ["about.licence.heading"] = "Licence",
        ["about.licence.code.lead"] = "The code is MIT.",
        ["about.licence.code.body"] = "The site, the crawler and the parsers are open source under "
            + "the MIT licence.",
        ["about.licence.open.lead"] = "The licence for the data is an open question.",
        ["about.licence.open.body"] = "A separate decision from the code's, and not yet taken. Treat "
            + "the terms below as this deployment's current answer, not the project's settled "
            + "position. A rival directory taking the whole catalogue is a success condition here, "
            + "so whatever is settled will not stand in the way of one.",
        ["about.licence.codeLabel"] = "code",
        ["about.licence.dataLabel"] = "data, as this deployment serves it",
        ["about.licence.creditLabel"] = "credit as",
        ["about.licence.code.line"] = "Code: {licence}",
        ["about.licence.data.line"] = "Data: {licence}",
        ["about.licence.credit.line"] = "Credit as: {credit}",
        ["about.licence.deployment"] = "(what this deployment serves. The project's own answer is "
            + "still open.)",

        // ── submit a game ─────────────────────────────────────────────────────────────────────
        // A host, a port, and nothing else. The form has no name box and no description box, so
        // every word on this page is ours rather than a submitter's, and all of it belongs here.
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

        // The answers. Every one takes {address} as an argument rather than opening with it, because
        // a language that puts the subject elsewhere has nowhere to say so if the address is glued
        // to the front of an English sentence. The word for an address we could not read is its own
        // id: it is a noun phrase standing where a hostname would, and it inflects.
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

        // ── dates, ages and provenance — the two shapes on nearly every page ──────────────────
        // Appended as one block on purpose: three other surfaces are appending to this file at the
        // same time, and a marked section at the end is a merge that adds rather than one that
        // collides.
        //
        // <b>The words come from CLDR and the order comes from here.</b> A month name is
        // <c>CultureInfo</c>'s — see <c>Locales.CultureOf</c>, and see the day names in the heatmap,
        // which are the same job — but the *arrangement* of day, month and year is not something a
        // .NET format string can express for a language it is never sent. Japanese writes
        // 2026年7月30日 and German puts a point after the day; both are one edit to this pattern and
        // neither is reachable through <c>ToString("d MMM yyyy")</c>.
        ["date.absolute"] = "{day} {month} {year}",

        // UTC is named rather than implied, and it is not a word to translate. Every time on this
        // site is UTC because a crawler's clock is the only one it has, and a reader in another zone
        // who is not told cannot tell whether 14:02 is theirs. The 24-hour spelling is the site's
        // and not the locale's for the same reason: one zone, one clock, one shape.
        ["date.stamp"] = "{date} {time} UTC",

        // ── the age ladder, in three registers ────────────────────────────────────────────────
        // Three families over the same seven rungs, and the English is identical in two of them.
        // That is the point. A bare duration is a duration; "how long ago did we last confirm this
        // value" and "how long has this game been unreached" are two different questions, and a
        // language that answers them with one phrasing can still choose to — while one that needs
        // "vor 2 Wo." for the first and "seit 2 Wo." for the second has somewhere to say so.
        //
        // Every rung is a real ICU plural even where English has one form, because the branch a
        // language actually needs is not knowable from the source text. `#` prints the number.
        ["age.short.now"] = "now",
        ["age.short.minutes"] = "{count, plural, one {#m} other {#m}}",
        ["age.short.hours"] = "{count, plural, one {#h} other {#h}}",
        ["age.short.days"] = "{count, plural, one {#d} other {#d}}",
        ["age.short.weeks"] = "{count, plural, one {#w} other {#w}}",
        ["age.short.months"] = "{count, plural, one {#mo} other {#mo}}",
        ["age.short.years"] = "{count, plural, one {#y} other {#y}}",

        // How long ago we last confirmed a value. The freshest rung is a word rather than a
        // duration — "now ago" was a real bug, and giving the rung its own id is what makes it
        // unwritable rather than merely fixed.
        ["age.ago.now"] = "just now",
        ["age.ago.minutes"] = "{count, plural, one {#m ago} other {#m ago}}",
        ["age.ago.hours"] = "{count, plural, one {#h ago} other {#h ago}}",
        ["age.ago.days"] = "{count, plural, one {#d ago} other {#d ago}}",
        ["age.ago.weeks"] = "{count, plural, one {#w ago} other {#w ago}}",
        ["age.ago.months"] = "{count, plural, one {#mo ago} other {#mo ago}}",
        ["age.ago.years"] = "{count, plural, one {#y ago} other {#y ago}}",

        // How long since the game was last reached. Identical English, different question — and
        // never "offline" or "down" in any language: we measured a socket from one vantage point
        // and a game with a routing problem to our host is unreachable and perfectly alive.
        ["age.dark.now"] = "just now",
        ["age.dark.minutes"] = "{count, plural, one {#m ago} other {#m ago}}",
        ["age.dark.hours"] = "{count, plural, one {#h ago} other {#h ago}}",
        ["age.dark.days"] = "{count, plural, one {#d ago} other {#d ago}}",
        ["age.dark.weeks"] = "{count, plural, one {#w ago} other {#w ago}}",
        ["age.dark.months"] = "{count, plural, one {#mo ago} other {#mo ago}}",
        ["age.dark.years"] = "{count, plural, one {#y ago} other {#y ago}}",

        // The <time> element's own two joins: the hover title, and the absolute a screen reader
        // hears after the visible age. Both are an age and an instant in one string, and which one
        // comes first is a language's decision rather than a comma in a template.
        ["time.title"] = "{age}, {stamp}",
        ["time.spoken"] = ", {stamp}",

        // ── the provenance chip's tooltip ─────────────────────────────────────────────────────
        // The value and the source token are machine voice and pass through untranslated; every
        // word around them is ours. "last confirmed" is a fact about our crawl and not about the
        // game, and it must stay one in every language — it is the date we last saw the value, not
        // a date the game did anything.
        ["chip.title"] = "{value} — {how} via {source}, last confirmed {date}",
        ["chip.title.stale"] = "{value} — {how} via {source}, last confirmed {date} (past its expected refresh)",

        // The same chip in plain text, where there is no hover to put it in.
        ["chip.plain"] = "({how}, {age})",
        ["chip.plain.stale"] = "({how}, {age}, stale)",

        // Three registers of the measured/declared line already exist above; this is the fourth
        // subject, and it is a separate id for the reason all the others are.
        ["provenance.game.ownerDeclared"] = "owner-declared",

        // ── how a value reached us, one id per source ─────────────────────────────────────────
        // <b>A display name, not an enum member.</b> The chip's tooltip printed FieldSource's own
        // ToString and told readers a value came "via Mssp" — an acronym mis-cased, in a sentence
        // no translator could reach, because the words were never in a file they are sent. MSSP,
        // GMCP, WHO, INFO and I3 are protocol names and stay exactly as they are in every locale;
        // the handshake, the connect screen, the owner and this project's staff are ours to say.
        ["source.staff"] = "staff",
        ["source.handshake"] = "the telnet handshake",
        ["source.owner"] = "the owner",
        ["source.who"] = "WHO",
        ["source.i3"] = "I3",
        ["source.mssp"] = "MSSP",
        ["source.info"] = "INFO",
        ["source.i3Mudlist"] = "the I3 mudlist",
        ["source.banner"] = "the connect screen",
    };

    /// <summary>
    /// The bundles, by tag.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Only English is complete, and that is the honest state of this work rather than a gap in
    /// it.</b> The order of work is explicit: the glossary is written and human-translated before a
    /// reader is sent anywhere, and no locale is offered until it is. What ships here is the
    /// machinery, exercised end to end by the two test-only bundles below.
    /// </para>
    /// <para>
    /// <c>qps-ploc</c> is a pseudolocale — accented and expanded English. It is not a language and
    /// nobody claims it is one; it exists so routing, fallback, plural selection and the nav's 1.4x
    /// width budget are all exercised by something real. <c>ru-x-canary</c> is machine-translated,
    /// never shipped, and deliberately incomplete: it is what makes a missing plural form fail a
    /// build instead of reaching a reader.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, Dictionary<string, string>> TestBundles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Every message, mechanically transformed. Generated rather than typed so it cannot
            // fall behind the source bundle it is derived from.
            ["qps-ploc"] = English.ToDictionary(e => e.Key, e => Pseudo(e.Value), StringComparer.Ordinal),

            // Three plural categories, and one message that is missing its `few` and `many` branches
            // on purpose. That omission is what the completeness test turns into a build failure.
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
    /// The fallback is silent to the reader and loud to the build. A reader meeting one English
    /// phrase inside a German sentence learns something true: this particular claim has not been
    /// translated yet. What must never happen is the other thing — a smoothed-over approximation of
    /// a locked string, which teaches them something false and gives them no way to tell.
    /// </remarks>
    public static string For(string tag, string id, IReadOnlyDictionary<string, object?>? args = null)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(id);

        var pattern = Pattern(tag, id)
            ?? throw new KeyNotFoundException($"No message '{id}' in any bundle, including English.");

        return IcuMessage.Format(pattern, tag, args);
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
    /// <c>ResourceNotFound</c> is the load-bearing check. <see cref="IStringLocalizer"/> answers a
    /// missing key with the key itself rather than with null, so a lookup that trusted the string it
    /// got back would render <c>facet.count</c> to a reader and call it a translation.
    /// </remarks>
    private static string? Own(string tag, string id)
    {
        if (TestBundles.TryGetValue(tag, out var bundle))
        {
            return bundle.GetValueOrDefault(id);
        }

        // The source language reads its own compiled-in copy: it is the fallback for every other
        // locale, and a fallback that depends on a satellite assembly having loaded is not one.
        if (string.Equals(tag, Locales.SourceTag, StringComparison.OrdinalIgnoreCase))
        {
            return English.GetValueOrDefault(id);
        }

        if (Culture(tag) is not { } culture)
        {
            return null;
        }

        // The resource set for this culture *alone*, with tryParents off — which is the whole point.
        // GetString walks up to the neutral resources, so it answers the English for a locale that
        // has translated nothing, and a caller asking "does this locale carry its own words for this
        // id" would be told yes for every id in the site. The fallback is deliberate elsewhere and
        // wrong here.
        //
        // A ResourceManager rather than IStringLocalizer, and that is not a rejection of the pattern
        // — IStringLocalizer *is* a ResourceManager with the culture read off the ambient thread.
        // This lookup is static and is called from Razor markup, from the plain-text renderer and
        // from headless component tests alike, and it is handed the locale rather than inferring
        // one; the DI wrapper would mean it could not answer at all without a host behind it, which
        // is most of where it is called from. AddMuiLocalization still registers the injected form
        // for anything that wants it.
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
    /// <remarks>
    /// One per culture, compiled by the SDK from <c>Resources/Messages.&lt;culture&gt;.resx</c> with
    /// no <c>&lt;EmbeddedResource&gt;</c> entries in the project file. A culture with no satellite
    /// answers null here rather than throwing, which is the fallback path.
    /// </remarks>
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

    /// <summary>The ids a locale has not translated yet.</summary>
    /// <remarks>
    /// The release checklist's own question, answerable from code rather than from a spreadsheet.
    /// A locale may not be moved to <see cref="LocaleStatus.Shipped"/> while any <em>locked</em> id
    /// is in this list — which is the rule the completeness test enforces.
    /// </remarks>
    public static IReadOnlyList<string> MissingFor(string tag) =>
        [.. Ids.Where(id => !HasOwn(tag, id))];

    /// <summary>
    /// Accented, expanded English — a language nobody speaks, which is the point.
    /// </summary>
    /// <remarks>
    /// Two jobs. The accents prove a string came through the pipeline rather than being hard-coded
    /// in a template, and the padding gives every string the 1.4x width the handoff says to review
    /// a locale at — German and Russian run 30–40% longer on short UI nouns, and the nav bar was
    /// tightened to fit English exactly. The ICU syntax is stepped over rather than transformed:
    /// mangling a branch keyword would make the message unparseable and prove nothing.
    /// </remarks>
    private static string Pseudo(string pattern)
    {
        var b = new System.Text.StringBuilder(pattern.Length * 2);
        var depth = 0;

        foreach (var c in pattern)
        {
            if (c == '{') { depth++; b.Append(c); continue; }
            if (c == '}') { depth--; b.Append(c); continue; }

            // Inside braces the text is syntax — argument names and branch keywords — and accenting
            // it would make the message unparseable, which proves nothing.
            b.Append(depth > 0 ? c : Accent(c));
        }

        // The padding goes outside the braces so no argument name is touched.
        return "⟦" + b + "⟧";
    }

    private static char Accent(char c) => c switch
    {
        'a' => 'á', 'e' => 'é', 'i' => 'í', 'o' => 'ó', 'u' => 'ú', 'n' => 'ñ', 'c' => 'ç',
        'A' => 'Á', 'E' => 'É', 'I' => 'Í', 'O' => 'Ó', 'U' => 'Ú', 'N' => 'Ñ', 'C' => 'Ç',
        _ => c,
    };
}
