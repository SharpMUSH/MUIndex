namespace MUI.Web.Localization;

public static partial class Messages
{
    /// <summary>
    /// STATIC PAGE COPY — /about, /submit, /account/sign-in, plus dates/ages/provenance and the
    /// per-source display names. Every lead/body pair is two ids because the graphical page bolds
    /// the lead and the plain page cannot — presentation stays out of the string.
    /// </summary>
    private static Dictionary<string, string> StaticPages() => new(StringComparer.Ordinal)
    {
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
    };
}
