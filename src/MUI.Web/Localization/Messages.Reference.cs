namespace MUI.Web.Localization;

public static partial class Messages
{
    /// <summary>
    /// THE REFERENCE SECTION'S CHROME — /reference, its codebase and protocol pages, the client
    /// capability matrix, and the plain mirror's own wording for all of it.
    /// </summary>
    /// <remarks>
    /// The Markdown articles are translated as documents by their own route; this bundle holds only
    /// the furniture around them. MSSP, GMCP, TTYPE etc. stay out of the bundle — a translated
    /// protocol acronym is destroyed evidence, not a localized string.
    /// </remarks>
    private static Dictionary<string, string> Reference() => new(StringComparer.Ordinal)
    {
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
    };
}
