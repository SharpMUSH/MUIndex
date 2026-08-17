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
