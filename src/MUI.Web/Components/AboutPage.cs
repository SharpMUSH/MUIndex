using MUI.Crawl;
using MUI.Discovery;
using MUI.Web.Api;

namespace MUI.Web.Components;

/// <summary>
/// The about page, as one view model both surfaces render.
/// </summary>
/// <remarks>
/// <para>
/// The page is an obligation rather than a feature. This project reads other people's directories
/// and connects to other people's servers, and spec §7.6 and §11 both say the same thing about that:
/// credit what we read, publish who is knocking and how to make it stop, and state the limits of
/// what a measurement here actually proves.
/// </para>
/// <para>
/// The prose is data because both surfaces have to carry it. A sentence that exists only in the
/// graphical page would be a sentence the plain page could not say, and on this site that is the
/// definition of decoration — see <see cref="PlainText"/>. So the page is a list of sections, the
/// renderers differ in markup and in nothing else, and the parity test reads words rather than tags.
/// </para>
/// <para>
/// <b>Nothing here is written from the design document alone.</b> Writing the design's intentions
/// here as though they were the deployment's behaviour is the exact shape of the
/// <c>ContactedMaintainer</c> defect this repository already has a record of: a claim about the world
/// compiled in by whoever typed it. The page said for several releases that there was no automated
/// opt-out, which was true and which is the reason that sentence was there.
/// </para>
/// <para>
/// <b>So the opt-out's spellings are read off <see cref="OptOutVocabulary"/> rather than typed into
/// this file.</b> The variable and the record named here are the ones
/// <see cref="MUI.Discovery.OptOutGate"/> actually reads, and a page that advertised a switch wired to
/// nothing would be worse than a page admitting there was none.
/// </para>
/// </remarks>
public sealed record AboutPage(string Lede, IReadOnlyList<AboutSection> Sections)
{
    /// <summary>Every directory credited anywhere on the page, in the order they are shown.</summary>
    public IReadOnlyList<ImportSource> Sources => [.. Sections.SelectMany(s => s.Sources)];

    /// <summary>
    /// Builds the page from the two things about it a deployment can change.
    /// </summary>
    /// <param name="probe">
    /// The crawler's own options, so the identity published here is the object the probe is
    /// constructed from rather than a copy of it that can drift.
    /// </param>
    /// <param name="dataset">The licence terms this deployment serves its dumps under.</param>
    public static AboutPage Build(ProbeOptions probe, DatasetLicenceOptions dataset) => new(
        "Every game here was measured by a machine that connected to it, and every value says where "
        + "it came from and when. This page covers what that proves, what we get wrong, whose "
        + "directories we read, and how to make the crawler stop.",
        [
            Measures(),
            Limits(),
            Never(),
            Crawler(probe),
            Attribution(),
            Licence(dataset),
        ]);

    private static AboutSection Measures() => new(
        "measures",
        "What a fact here is",
        [
            new("Measured beats declared, and both are shown.",
                "A game's MSSP report is the game describing itself. The telnet handshake is what we "
                + "watched it do. Both appear on its page, labelled with how and when. Where they "
                + "disagree, we show the disagreement."),
            new("A player count says where it came from.",
                "Either a WHO or DOING read at the connect screen, which we counted, or the game's "
                + "own MSSP PLAYERS field, which it published. Never merged."),
            new("An answer we cannot read is unknown, never zero.",
                "Servers customise their WHO headers freely, and past a point our parser cannot read "
                + "one. That is uncountable, its own state. A measured zero — we got in, nobody was "
                + "there — is a count, and prints as one."),
            new("Reachable, never uptime.",
                "We open a socket from one host at intervals. A game we cannot route to is "
                + "unreachable and perfectly alive. Nothing here claims a game's uptime, because "
                + "nothing here measured it."),
            new("An hour is counted, uncountable, or not measured.",
                "The activity grid has three states. The third is empty and names no cause: an hour "
                + "we could not reach and an hour we never probed are the same absence, and neither "
                + "is that server's downtime."),
        ]);

    private static AboutSection Limits() => new(
        "limits",
        "What we know we get wrong",
        [
            new("Archive grace is measured from the day we found you.",
                "A game that stops answering leaves the default listing after its grace period: a "
                + "quarter of the reachable time we probed, floored at 60 days and capped at 365. A "
                + "game running since 1995 starts at the floor on the day we discover it. We import "
                + "nothing to fill in the years before we arrived."),
            new("We do not credit MSSP CREATED toward that grace.",
                "It is one hand-typed line in a config file, so crediting it would make the archive "
                + "threshold gameable. It is shown as a declaration and buys nothing."),
            new("Claiming a game earns the ceiling.",
                "Proving server access is worth the full year of grace, however long we have been "
                + "watching."),
            new("Everything here is one host, looking at intervals.",
                "A percentage of reachable time is a fraction of the window we observed, never of "
                + "one we did not. No graphic here fills in the rest."),
            new("Nothing is ever deleted.",
                "Archiving takes a game out of the default listing, the rankings and the "
                + "active-today figure, and nothing else. Its page, URL, history and address "
                + "survive, it keeps being probed, and one successful probe puts it back."),
        ]);

    private static AboutSection Never() => new(
        "never",
        "What this site will not do",
        [
            new("No votes, stars, ratings or recommendations.",
                "Rankings are computed from measured data only. A directory ranked by who can "
                + "mobilise the most clicks describes the campaigning, not the hobby, and that is "
                + "what killed the incumbents."),
            new("No forums, reviews, wikis, comments or player profiles.",
                "Orientation material — what a MUSH is, which codebase suits collaborative "
                + "roleplay — is written, signed and versioned like the rest of the site."),
            new("Player names are never persisted.",
                "A WHO reply is parsed in memory for a count and the shape of the header. The names "
                + "are not written down; aggregates use a salted hash with a rotating salt."),
            new("No absolute population figure is published.",
                "Per-codebase and per-protocol shares ship: a ratio over the measured set survives "
                + "the games we cannot count. \"How many people play MU*\" does not, because that "
                + "number would not survive being quoted."),
        ]);

    private static AboutSection Crawler(ProbeOptions probe) => new(
        "crawler",
        "The crawler, and how to make it stop",
        [
            new("A probe is one connection that never logs in.",
                "It opens a socket, negotiates telnet options, reads the connect screen, asks for "
                + "MSSP by negotiating option 70, sends "
                + $"{string.Join(", ", TelnetProbe.PermittedCommands)}, and disconnects. No "
                + "character, no login, nothing changed on the far side. A timeout bounds the "
                + "session so a wedged probe cannot sit on a connection slot."),
            new("CRAWL DELAY wins.",
                "A game that states a preferred minimum gap in its MSSP report gets it, over our own "
                + "schedule in both directions: 720 hours means monthly, not weekly. A dark game is "
                + "still tried for ever at the longer interval, which is how it re-lists itself when "
                + "it comes back."),
            new("A referred address is verified, never trusted.",
                "MSSP lets a game name other games. Every name is resolved before anything is "
                + "dialled, and refused unless every address it resolves to is globally routable. A "
                + "mixed answer refuses the whole target. Our refusal is filed as ours and never "
                + "appears in a game's record as downtime."),
            new("Connect screens are shown because they are sent to everybody.",
                "A server paints its connect screen, unauthenticated, to every anonymous connection. "
                + "We display it as evidence and label it. Ask and it comes down."),
            new("Say stop, and we stop — three ways.",
                $"Publish {OptOutVocabulary.MsspVariable} 1 in your MSSP report, and the probe that "
                + "reads it is the last one. Or publish a TXT record at "
                + $"{OptOutVocabulary.DnsLabel}.your.host reading \"{OptOutVocabulary.DnsValue}\", "
                + "which needs no MSSP support and no account here. Or write to a person. All three "
                + "are honoured within one crawl cycle, recorded with the date and what we read, and "
                + "enforced on the submission form too."),
            new("The MSSP field stops that listener; the record stops the host.",
                "MSSP is published by the port that answered, so it speaks for that port — MU* "
                + "hosting routinely runs unrelated games on one domain, and one must not silence "
                + "its neighbour. A TXT record covers every port unless it names one, as "
                + $"\"{OptOutVocabulary.DnsValue}=4201\". Anything there we cannot read as a port "
                + $"list means the whole host, so \"{OptOutVocabulary.DnsValue}=all\" works."),
            new("The DNS route is the one you can undo without asking us.",
                "A TXT record is readable without connecting to a server that told us not to, so we "
                + "re-read it before every dial. Delete it and we dial again within a week. An MSSP "
                + "field cannot be re-read without doing the thing you asked us to stop, so MSSP "
                + "opt-outs and written requests stand until you say otherwise. That TXT lookup is "
                + "all an opted-out address gets: it touches your nameserver, never your game."),
            new("Stopping is not deleting, and it is not downtime.",
                "A game that opts out keeps its page, its address and everything we measured before "
                + "it asked. Only new data stops: the activity grid stops gaining hours and names no "
                + "cause, because our decision to stop knocking is a fact about us. It is recorded "
                + "on the crawl that did not happen, and in the register of who asked."),
            new("If stopping is not enough, the listing can go too.",
                "Once we have stopped on every address your game answers on, your dashboard offers "
                + "one more thing: take it out of the listing, the rankings and the daily figure. "
                + "The page and every address it has ever had still answer, and nothing is deleted — "
                + "it stops being somewhere a reader arrives by browsing. It needs a verified claim, "
                + "because it is a decision about your game and we record who made it. And a probe "
                + "undoes it: take your opt-out back, and the next dial that gets an answer puts you "
                + "back in the listing without asking us twice."),
        ])
    {
        Identity = AboutIdentity.For(probe),
    };

    private static AboutSection Attribution() => new(
        "sources",
        "Where the list of games came from",
        [
            new("We take addresses. Nothing else.",
                "A backfill takes a host and a port. No player counts, no reachability history, no "
                + "descriptions, no fields, and no note of which site an address came from."),
            new("Deliberately less than those sites can give.",
                "Several hold years of dated player counts. Importing that would fill the heatmaps "
                + "of the games somebody else was already watching, and rest this site's central "
                + "claim on another party's prober."),
            new("A game's origin is not one fact.",
                "Any game worth listing appears in several of these directories, so \"imported "
                + "from\" would name whichever fetch ran first. That a game exists is public "
                + "information; where we read it adds nothing and is the part of somebody else's "
                + "work with the least claim to be ours."),
            new("Reading somebody's site is still reading somebody's site.",
                "We ask for a bulk export or a documented endpoint before scraping, read robots.txt "
                + "first, and rate-limit scrapes hard. A source that needs its maintainer's say-so "
                + "is not fetched until a person can state they were asked."),
        ])
    {
        Sources =
        [
            new("TinTin++ MSSP Mud Crawler", "https://tintin.mudhalla.net/protocols/mssp/",
                ImportSourceState.Read,
                "One page, one request. Published by a crawler that connects to each game and "
                + "prints what it read."),
            new("TinTin++ MSDP Mud Crawler", "https://tintin.mudhalla.net/protocols/msdp/",
                ImportSourceState.Read,
                "The same crawler's MSDP listing. Nearly a subset of its MSSP sibling, read for the "
                + "few addresses it reaches that the other does not."),
            new("The Mud Connector", "https://www.mudconnect.com/",
                ImportSourceState.Read,
                "Publishes its whole catalogue on one page, so reading it costs a single request. "
                + "Our largest source of addresses, and of no measurements."),
            new("MudStats", "https://mudstats.com/",
                ImportSourceState.Read,
                "One index page and one page per world, so a scrape rather than an export. On 30 "
                + "July 2026 we fetched 143 of their pages, fifteen seconds apart and honouring "
                + "robots.txt, but before anyone had written to them. That should not have "
                + "happened. The gate now takes a person willing to state the maintainer was "
                + "asked."),
            new("MudVerse", "https://www.mudverse.com/",
                ImportSourceState.Withheld,
                "Implemented, tested, never run. The strongest source here on every axis except "
                + "permission, and nothing will be fetched until somebody has written to them."),
        ],
    };

    private static AboutSection Licence(DatasetLicenceOptions dataset) => new(
        "licence",
        "Licence",
        [
            new("The code is MIT.",
                "The site, the crawler and the parsers are open source under the MIT licence."),
            new("The licence for the data is an open question.",
                "A separate decision from the code's, and not yet taken. Treat the terms below as "
                + "this deployment's current answer, not the project's settled position. A rival "
                + "directory taking the whole catalogue is a success condition here, so whatever is "
                + "settled will not stand in the way of one."),
        ])
    {
        Licence = new AboutLicence(
            "MIT",
            dataset.LicenceName,
            dataset.LicenceUrl,
            dataset.Attribution,
            dataset.Notice),
    };
}

/// <summary>One headed run of prose, plus whatever structured block belongs under it.</summary>
/// <remarks>
/// The extras are nullable rather than a separate ordered list, so both renderers walk one sequence
/// and neither has an if-ladder deciding where a block goes. A block that appeared in one surface
/// and not the other is the failure this page is a test of.
/// </remarks>
public sealed record AboutSection(string Id, string Heading, IReadOnlyList<AboutPoint> Points)
{
    /// <summary>Who the crawler says it is, when this section is the one about the crawler.</summary>
    public AboutIdentity? Identity { get; init; }

    /// <summary>The directories credited under this section.</summary>
    public IReadOnlyList<ImportSource> Sources { get; init; } = [];

    /// <summary>The terms the data goes out under, when this section is the one about licensing.</summary>
    public AboutLicence? Licence { get; init; }
}

/// <summary>
/// A lead-in and the paragraph it introduces.
/// </summary>
/// <remarks>
/// Split rather than one string because the graphical page sets the lead in bold and the plain page
/// cannot. Keeping them apart means the emphasis is presentational and the sentence is not — the two
/// surfaces read identically aloud.
/// </remarks>
public sealed record AboutPoint(string Lead, string Body)
{
    public string Sentence => $"{Lead} {Body}";
}

/// <summary>
/// What a server administrator sees when we knock, and what to do about it.
/// </summary>
/// <remarks>
/// Read off <see cref="ProbeOptions"/> rather than written out here, so the name published on the
/// page is a property of the object the probe is built from.
/// </remarks>
public sealed record AboutIdentity(string Name, string InfoUrl, bool Announced, bool ContactConfigured)
{
    public static AboutIdentity For(ProbeOptions probe) => new(
        probe.TerminalTypes.Count > 0 ? probe.TerminalTypes[0] : "MUINDEX-CRAWLER",
        probe.InfoUrl,
        Announced: true,
        // The built-in value is a placeholder on a domain that has not been chosen. Publishing it as
        // the way to reach us, unmarked, would be publishing an address that answers nobody.
        ContactConfigured: probe.InfoUrl != new ProbeOptions().InfoUrl);

    /// <summary>The honest version of "who is this in my logs", in one sentence.</summary>
    public string Wording => Announced
        ? $"The crawler names itself {Name} when a server asks what it is."
        : $"The crawler is configured to call itself {Name} but cannot yet say so. Its telnet "
        + "library gives a client no way to set the terminal type, so your logs see that library's "
        + "default, and NEW-ENVIRON is answered from the crawler host's environment. Both are gaps "
        + "in the library and ours to fix there. Until then, recognise a probe by its shape: one "
        + "connection, no login, a short read-only command set, gone.";
}

/// <summary>Whether a directory was actually read, which is not the same as whether we can read it.</summary>
public enum ImportSourceState
{
    /// <summary>Fetched. Addresses were taken from it.</summary>
    Read,

    /// <summary>
    /// Implemented and deliberately not run, because it is a scrape and nobody has asked its
    /// maintainer yet. Credited anyway: the reader is owed the whole list, and a source we have
    /// chosen not to fetch is a different fact from one we never considered.
    /// </summary>
    Withheld,
}

/// <summary>One directory, credited by name, with what was taken from it and whether it was read.</summary>
public sealed record ImportSource(string Name, string Url, ImportSourceState State, string Note)
{
    public string StatusWording => State switch
    {
        ImportSourceState.Read => "read — addresses only",
        _ => "not read — awaiting permission",
    };
}

/// <summary>The two licences, which are two decisions and only one of them has been taken.</summary>
public sealed record AboutLicence(
    string CodeLicence,
    string DataLicenceName,
    string? DataLicenceUrl,
    string Attribution,
    string Notice);
