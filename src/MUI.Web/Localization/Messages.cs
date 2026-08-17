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

        // THE CATALOGUE SURFACES — /ecosystem, /rankings, /archive, and /find's footer.
        //
        // Every sentence on these three pages is made of its own qualifications, which is why
        // none of them is assembled from parts. "Of the 7 games listed, 5 told us what they run"
        // is a sentence about our measurement rather than about the hobby, and a translator handed
        // "Of the", a number, "games listed" and another number has nowhere to stand: the clause
        // order, the agreement and which number is the denominator are all decisions the source
        // language made silently. Each id below is therefore a whole claim with its numbers as
        // arguments, and the count and the set come before the percentage in every one of them.
        // ═════════════════════════════════════════════════════════════════════════════════════

        // ── the ecosystem dashboard: shares, never totals, and never a share without its set ──
        //
        // §15.7 withholds the absolute "how many people play MU*" figure, and no message here has
        // an argument that could carry one — the page's own guard, restated in the shape of the
        // bundle. What every id does carry is its denominator, in the same string as the
        // percentage, because the percentage is the part that travels when somebody quotes it.
        ["ecosystem.title"] = "The ecosystem",
        ["ecosystem.noTotals"] = "Shares, never totals. We do not publish a figure for how many people play MU*: a ratio over the games we measured survives the ones we cannot reach, and a headcount does not.",

        // The three counts at the top. {value} is the numeral as the page draws it — the markup
        // is an argument rather than part of the message, so a language that puts the number last
        // may, and a translator never meets a tag.
        ["ecosystem.listed"] = "{count, plural, one {{value} game listed} other {{value} games listed}}",
        ["ecosystem.handshakes"] = "{count, plural, one {{value} game whose handshake we completed} other {{value} games whose handshake we completed}}",
        ["ecosystem.msspReports"] = "{count, plural, one {{value} game whose MSSP report we hold} other {{value} games whose MSSP report we hold}}",
        ["ecosystem.oldestHandshake"] = "Oldest handshake here: confirmed {age} ago.",

        // A share, in the one order this site states one: the count, the set it was counted in,
        // and only then the percentage. An empty denominator is not nought per cent — 0 of 0 is
        // nothing measured, in the same way an unknown count is not a zero.
        ["ecosystem.share"] = "{count, number} of {total, number} ({fraction, number, ::percent .0})",
        ["ecosystem.share.nothing"] = "{count, number} of {total, number} — nothing measured yet",
        // The percentage alone, for the one cell whose denominator is its column head.
        ["ecosystem.share.percent"] = "{fraction, number, ::percent .0}",

        ["ecosystem.codebases.title"] = "Codebases",
        // Both numbers, because one of them read as the other: "Share of the 144 listed games that
        // told us what they run" put the identified count where the size of the catalogue belongs,
        // and a reader came away believing the site listed 144 games rather than 418.
        ["ecosystem.codebases.basis"] = "Of the {listed, plural, one {# game} other {# games}} listed, {identified, number} told us what they run, and every share below is over those {identified, number}. A codebase we could not read is left out of the denominator, never counted as something else.",
        ["ecosystem.codebases.none"] = "No listed game has told us its codebase yet.",
        ["ecosystem.soleUse"] = "{share} run a codebase no other listed game runs — one game each, which is a name rather than a share. They are inside the denominator above and folded out of the bars, not dropped:",

        ["ecosystem.lineages.title"] = "Lineages",
        ["ecosystem.lineages.basis"] = "The same games, grouped by the tradition their server descends from — our reading of the codebase, not anything a game published. No game reports \"MUSH\": MSSP has no such value, and most of the MUSH world publishes no MSSP at all, so this is the only way the question can be asked.",
        ["ecosystem.lineages.none"] = "No listed game runs a codebase we place in a lineage yet.",
        // Our abstention, kept out of everyone else's bar and said in the same breath as the
        // denominator it stays inside. {family} is the MSSP value itself and is machine voice.
        ["ecosystem.lineages.notClassified"] = "{count, plural, one {# of those games runs a codebase} other {# of those games run codebases}} we do not place in any lineage — several say as much themselves, publishing {family}. They are inside the denominator above and in nobody's share.",

        ["ecosystem.protocols.title"] = "Protocols",
        ["ecosystem.protocols.floor"] = "Read every measured figure below as a floor. We ask for MSSP by name, so silence there is an answer. Nothing else here is requested, and a server may support a protocol without ever offering it.",
        ["ecosystem.mssp.instrument"] = "{instrument} is the one row below that is not a floor: we ask every server for it by name, so the games that did not offer it were asked and declined. It is also the only one with no declared figure, because every game whose report we hold supports it by demonstration and a count of the ones that also listed it would measure a habit against that.",
        ["ecosystem.mssp.gap"] = "We hold {reports, number} reports and {offered, plural, one {# game} other {# games}} offer MSSP today: the other {gap, number} stopped publishing one after we read it, and a report is not thrown away because it stopped being reissued.",
        ["ecosystem.protocols.caption"] = "Protocol adoption. Measured is what a server offered in a completed handshake; declared is what its MSSP claims. Two sets of games, so two denominators.",
        ["ecosystem.column.protocol"] = "protocol",
        ["ecosystem.column.measured"] = "measured — of {basis}",
        ["ecosystem.column.declared"] = "declared — of {basis}",

        // Four whole sentences rather than a share with clauses bolted on. The English builds them
        // by concatenation and the concatenation is the defect: "· 6 games neither offered nor
        // asked" is a fragment in English word order that no translator can reorder.
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
        // Its own id rather than state.notMeasured: that one is an hour of the week and this is a
        // protocol share, and the two take different forms in a language that inflects.
        ["ecosystem.curve.notMeasured"] = "not measured",
        ["ecosystem.snapshot.title"] = "A snapshot, not a curve",
        ["ecosystem.snapshot"] = "A snapshot of what we can measure now. An adoption curve plots games changing their minds, and we record a change when it happens, so the curve becomes drawable once enough have been recorded. Plotting when we first reached each game would measure the crawl, not the hobby.",
        ["ecosystem.transitions"] = "{count, plural, one {# capability change} other {# capability changes}} recorded so far — the material a curve is drawn from.",
        ["ecosystem.transitions.none"] = "No measured capability has changed yet, so there is nothing to plot.",

        // The plain surface's own line, which has no table head to hang the two denominators on.
        ["ecosystem.plain.denominators"] = "Measured is of {measured}; declared is of {declared}. Two sets of games, so two denominators.",
        ["ecosystem.plain.counts"] = "{listed} · {handshakes} · {mssp}.",
        ["ecosystem.plain.oldestHandshake"] = "The oldest handshake in this picture was last confirmed {age} ago.",
        ["ecosystem.plain.lineages"] = "The same games, grouped by the tradition their server descends from. This is {evidence} — {meaning} — and not anything a game published: no game reports \"MUSH\", because MSSP has no such value and most of the MUSH world publishes no MSSP at all.",
        ["ecosystem.plain.measured"] = "measured: {value}",
        ["ecosystem.plain.declared"] = "declared: {value}",

        // ── the rankings: computed from measured data only, and it says so first ──────────────
        //
        // §2 rules out the vote permanently rather than pending a feature, so the claim has to
        // survive translation exactly: no votes, no stars, no ratings, and nothing here ranking
        // quality. A locale that softened that into "our favourites" would be publishing the one
        // thing this page exists to refuse.
        ["rankings.title"] = "Rankings",
        ["rankings.noVote"] = "Computed from measured data only. No votes, stars or ratings, ever. Nothing here ranks quality. We have not measured it.",
        ["rankings.busiest.title"] = "Busiest, by measured concurrent players",
        ["rankings.window.label"] = "Ranking window",
        // One id for all three windows and the fallback alike. A ranking window and a sort window
        // are different jobs, so this is not window.7 under another name.
        ["rankings.span"] = "{days, plural, one {# day} other {# days}}",

        // The basis, as three whole sentences the caller joins rather than one string built from
        // clauses. "0 of 519 games listed produced the 24 counted samples a median needs, on at
        // least 4 days of the window" was arithmetic where a sentence would do.
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

        ["rankings.spells.title"] = "Longest unbroken reachable spell",
        // Reachable, never uptime — schema, API, code and copy, and here most of all, because the
        // sentence exists to say which of the two was measured.
        ["rankings.spells.basis"] = "Every probe since the date given found the game reachable. Reachable, not up: we measure a socket from one host, and a game we cannot route to is perfectly alive. A spell cannot be longer than we have been watching, so the date is the fact and the duration follows.",
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

        // ── the archive: removed from the default listing, and from nothing else ──────────────
        //
        // §7.5. Nothing here is deleted, closed, dead or defunct: the game's page, URL, history
        // and change feed are untouched, it goes on being probed for ever, and one successful
        // probe puts it back the same day. "Archived" is a library catalogue's word for a
        // periodical that ceased publication, and the sentences below say the whole of that so a
        // translator is never left to pick a word from the tone alone.
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

        // ── /find's footer, which the rebuild left in English ────────────────────────────────
        //
        // The other two links here are footer.allGames and a11y.plainText, already said elsewhere
        // in exactly this job. This one had nothing to reuse: nav.random is a nav item reading
        // "random", and a footer link naming what it fetches is a different phrase.
        ["footer.randomGame"] = "random game",

        // THE TREND CHART, THE REACHABILITY STRIP AND THE REST OF THE GAME PAGE
        // ═════════════════════════════════════════════════════════════════════════════════════
        //
        // The last surface that answered in English whatever the reader asked for, and the largest:
        // ninety per-day tooltips on the trend chart and ninety more on the strip beside it were two
        // hundred strings of one shape, built by gluing a date to a fragment.
        //
        // **A day has the same three states an hour has, and they are three ids here.** A day we
        // counted — a measured zero included — a day probed all through that produced no count, and
        // a day with no measurement at all. The third names no cause in any language: a failed probe
        // writes no presence row, so an empty day covers a probe we could not complete and a day we
        // never dialled alike, and a translation reaching for "offline" or "not reachable" would file
        // our crawl schedule as a fact about somebody's game. `trend.day.notMeasured` and
        // `trend.day.notCounted` are deliberately far apart in wording so no locale can quietly
        // render both as *unavailable*, and a test asserts they stay different in every locale.
        //
        // **Dates are `{d, date, …}` arguments, never a month name written here.** The style is part
        // of the pattern, so a translator who needs `2026/05/21` writes `{d, date, short}` in their
        // own copy and gets it — which is the whole reason the date went through the formatter
        // rather than through a `ToString` at the call site.

        // ── the trend chart's per-day title, one per column ───────────────────────────────────
        // Four shapes, and the reader gets exactly one. A flat day — every probe returning the same
        // number — is its own id rather than a range with the same number twice, because "24–24" is
        // arithmetic where a measurement was asked for.
        ["trend.day.counted"] = "{d, date, medium} — {typical} on average, {low}–{high} across {probes, plural, one {# probe} other {# probes}}",
        ["trend.day.flat"] = "{d, date, medium} — {count, plural, =0 {0 players} one {# player} other {# players}}, every one of {probes, plural, one {# probe} other {# probes}}",
        ["trend.day.notCounted"] = "{d, date, medium} — probed, no count could be read",
        ["trend.day.notMeasured"] = "{d, date, medium} — no measurement",

        // ── the sentence above the chart ──────────────────────────────────────────────────────
        // It was five fragments: two numbers spelled into English, a hand-written "N of M days" and
        // a trend word chosen by an English rule and glued on the end. The direction is its own
        // sentence rather than a word substituted into this one, so a language that opens with it
        // can.
        ["trend.summary"] = "Typically {typical} on, peaking at {peak}, over {counted} of {days, plural, one {# day} other {# days}}.",
        ["trend.direction.steady"] = "Steady across the range.",
        ["trend.direction.up"] = "Up about {change, number, percent} from the start of the range to the end.",
        ["trend.direction.down"] = "Down about {change, number, percent} from the start of the range to the end.",

        // Both of the "nothing to draw" sentences, and neither of them gives a cause.
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

        // The legend. "not measured" gets the whole clause rather than the two words plus a dash and
        // a fragment, because what follows the dash is a fact about the drawing and moves in a
        // sentence that puts the verb last.
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
        // *Reachable*, never *up*. We measured a socket from one vantage point, and a game with a
        // routing problem to our host is unreachable and perfectly alive (spec §5.8). Every locale
        // has to keep that distinction, which is why the word is a locked id in four registers
        // rather than one string reused in four places: the stat label, the legend swatch, the noun
        // inside a spell, and the day's own tooltip decline differently in most languages.
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

        // The state as a noun, for the middle of a spell — "3 days unreachable". Separate from the
        // legend above, which is a caption, and from the day tooltips below, which are sentences.
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

        // The sentence the strip illustrates. The percentage's denominator is observed time and not
        // the window, so there are two ids and not one with a substituted noun: a game found an hour
        // ago must not read "Reachable 100.0% of the last 90 days" off a single probe.
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
        // Ours to say and theirs to be measured about: a cause is what our socket saw, so it names
        // the event and never a judgement of the game.
        ["cause.dns"] = "dns did not resolve",
        ["cause.refused"] = "connection refused",
        ["cause.tls"] = "tls failed",
        ["cause.timeout"] = "timed out",
        ["cause.handshakeStalled"] = "handshake stalled",
        ["cause.none"] = "no cause recorded",

        // ── the ANSI capture's frame ──────────────────────────────────────────────────────────
        // The chrome around somebody else's screen, never the screen: what is inside the <pre> is
        // what the server sent and is not a string in any bundle. "read as" rather than "encoded
        // in", because that is the honest verb — it is what we decoded the bytes with, and a game
        // that declares one encoding and sends another makes those two different sentences.
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

        // The archive plate. "Still probed" is the promise §7.5 makes and the reason the page is
        // still here at all, so it is said rather than implied by the page continuing to exist.
        ["game.archived.lastAnswered"] = "Last answered {date}, {ago} ago.",
        ["game.archived.stillProbed"] = "Still probed weekly; this page updates the day it answers.",
        ["game.archived.knownLive"] = "{span} known live",

        // The two ways out of the listing that are not archiving. They say different things because
        // the decisions are different: an exclusion is OURS and has to be arguable, so it prints the
        // argument; an unlisting is THEIRS, so it prints no reason at all.
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

        // ── added by the accessibility review, second pass ────────────────────────────────────
        // Kept in one block at the end because several agents are appending to this file at once.

        // The game page's live figure when there is no count to put in it — rule 2's third state on
        // the hero rather than on a heatmap cell.
        //
        // `PlayersNow` is null for an hour nobody measured, for a probe that answered with nothing
        // countable, and for a count older than the window this figure covers: one null over three
        // situations the page cannot tell apart. It said `state.notCounted`, which the glossary
        // reserves for an unreadable count — so a game we have never once reached was described as
        // one we probed and failed to count, which is our silence published as their measurement.
        // What is left is the absence itself, naming no cause.
        ["game.count.none"] = "no count",
        ["game.count.none.why"] = "no current count, and nothing here says why",
        ["game.plain.playersNoCount"] = "Players now: no count (nothing here says why)",
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
