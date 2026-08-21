namespace MUI.Web.Localization;

public static partial class Messages
{
    /// <summary>
    /// The owner dashboard and the claim flow: the dashboard's frame, a claimed game's block, the
    /// audit log, waiting on a token and the passkeys, claiming a game you run, and what a claim
    /// actually grants (the owner panel, §8.5 and §11).
    /// </summary>
    /// <remarks>
    /// These surfaces address the operator in the second person deliberately — do not neutralise.
    /// An owner's answer is a DECLARATION beside what the crawler measured, never a replacement of
    /// it; an opt-out is honoured and is never a deletion.
    /// </remarks>
    private static Dictionary<string, string> OwnerDashboard() => new(StringComparer.Ordinal)
    {
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
    };
}
