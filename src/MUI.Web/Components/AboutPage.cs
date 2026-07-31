using MUI.Crawl;
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
/// <b>Nothing here is written from the design document alone.</b> The spec describes an opt-out over
/// an MSSP field and a DNS TXT record, and a crawler that names itself in TTYPE — none of which is
/// implemented today (see <see cref="Crawler"/>). Writing the design's intentions here as though
/// they were the deployment's behaviour is the exact shape of the <c>ContactedMaintainer</c> defect
/// this repository already has a record of: a claim about the world compiled in by whoever typed it.
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
        "Every game listed here was measured by a machine that connected to it, and every value "
        + "says where it came from and when. This page is what that does and does not amount to: "
        + "what we can honestly tell you, what we know we get wrong, whose directories we read to "
        + "find the games in the first place, and how to make the crawler stop.",
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
        "What a fact on this site is",
        [
            new("Measured beats declared, and both are shown.",
                "A game's MSSP report is the game describing itself. The telnet handshake is us "
                + "watching what it actually does. Both appear on a game's page, each labelled with "
                + "how it was obtained and how old it is — and where the two disagree, the "
                + "disagreement is the interesting fact and is not hidden or averaged away."),
            new("A player count comes from one of two places, and says which.",
                "Either a WHO or DOING read at the connect screen before logging in, which is a "
                + "number we counted, or the game's own MSSP PLAYERS field, which is a number the "
                + "game published. They are different claims and are never merged into one figure."),
            new("An answer we cannot read is unknown, never zero.",
                "MU* servers customise their WHO headers freely and past a point our parser cannot "
                + "read one. That produces uncountable, which is its own state. A measured zero — "
                + "we got in and nobody was there — is a count, and is printed as the zero it is."),
            new("Reachable, never uptime.",
                "We open a socket from one host at intervals. That measures whether we could reach "
                + "a game from here, and it does not measure whether the game was up: a game with a "
                + "routing problem to our vantage point is unreachable and perfectly alive. Nothing "
                + "on this site claims to know a game's uptime, because nothing here measured it."),
            new("An hour is counted, uncountable, or not measured.",
                "The activity grid has three states rather than two, and the third is empty and "
                + "names no cause. An hour we could not reach and an hour we never probed look the "
                + "same there, because they are the same absence of a measurement — and colouring "
                + "either of them as downtime would record a decision of ours as a fact about "
                + "somebody else's server."),
        ]);

    private static AboutSection Limits() => new(
        "limits",
        "What we know we get wrong",
        [
            new("Archive grace is measured from the day we found you.",
                "A game that stops answering leaves the default listing once it has been "
                + "unreachable for longer than its grace period, and that period is a quarter of "
                + "the reachable time we ourselves probed, floored at 60 days and capped at 365. "
                + "So a game running continuously since 1995 starts at the 60-day floor on the day "
                + "we discover it and accrues from there. It is a real limitation rather than a "
                + "rounding error, and it is the accepted cost of every fact here being measured "
                + "here: nothing is imported that would fill in the years before we arrived."),
            new("We do not credit MSSP CREATED toward that grace.",
                "A game can declare it has existed since 1995 and probably has. It is also one "
                + "hand-typed line in a configuration file that nothing verifies, and crediting it "
                + "toward the archive threshold would make that threshold gameable by editing that "
                + "line. It is shown as the declaration it is, and it buys nothing."),
            new("Claiming a game earns the ceiling.",
                "Someone who can prove server access has demonstrably staked a claim, and that is "
                + "worth the full year of grace regardless of how long we have been watching."),
            new("Everything here is one host, looking at intervals.",
                "A percentage of reachable time is a fraction of the window we observed and never "
                + "of a window we did not. A game we have measured once is a game we have measured "
                + "once, and no graphic on this site will imply otherwise by filling in the rest."),
            new("Nothing is ever deleted.",
                "Archiving takes a game out of the default listing, the rankings and the "
                + "active-today figure, and out of nothing else. Its page, its URL, its history and "
                + "its address survive, it keeps being probed forever, and one successful probe "
                + "puts it straight back."),
        ]);

    private static AboutSection Never() => new(
        "never",
        "What this site will not do",
        [
            new("There are no votes, stars, ratings or recommendations.",
                "Rankings are computed from measured data and from nothing else. This is not a "
                + "feature we have not got round to: a directory ranked by who can mobilise the "
                + "most clicks stops describing the hobby and starts describing the campaigning, "
                + "and that is what killed the incumbents."),
            new("There are no forums, reviews, wikis, comments or player profiles.",
                "Orientation material — what a MUSH is, how it differs from a MUD, which codebase "
                + "suits collaborative roleplay — is written and signed and versioned like the rest "
                + "of the site, rather than opened to editing and then moderated."),
            new("Player names are never persisted.",
                "A WHO reply is parsed in memory to get a count and the shape of the header, and "
                + "the names in it are not written down. Anything aggregated uses a salted hash "
                + "with a rotating salt, so an estimate of distinct players is possible while "
                + "re-identifying one across salt epochs is not."),
            new("No absolute population figure is published.",
                "Per-codebase and per-protocol shares ship, because a ratio over the measured set "
                + "survives the games we cannot count and the games we have not found. \"How many "
                + "people play MU*\" does not ship, because that number would not survive being "
                + "quoted."),
        ]);

    private static AboutSection Crawler(ProbeOptions probe) => new(
        "crawler",
        "The crawler, and how to make it stop",
        [
            new("A probe is one connection that never logs in.",
                "It opens a socket, negotiates telnet options, reads whatever connect screen the "
                + "server paints, asks for MSSP by negotiating option 70, sends a single "
                + $"{string.Join(" or ", TelnetProbe.PermittedCommands)} at the connect screen, and "
                + "disconnects. It creates no character, sends no login, and changes nothing on the "
                + "far side. The whole session is bounded by a timeout so a wedged probe cannot sit "
                + "on a server's connection slot."),
            new("CRAWL DELAY wins.",
                "A game that states a preferred minimum gap between crawls in its MSSP report gets "
                + "it, and it beats our own schedule in both directions: a game asking for 720 "
                + "hours is probed monthly, not weekly. A game that has gone dark is still tried "
                + "forever at whichever of the two intervals is longer, which is how a game that "
                + "comes back re-lists itself with nobody involved."),
            new("A referred address is verified, never trusted.",
                "MSSP lets a game name other games. Those names are candidates and not facts: every "
                + "one is resolved before anything is dialled and refused unless every address it "
                + "resolves to is globally routable, so a hostname pointed at a private or "
                + "link-local address reaches nothing. A mixed answer refuses the whole target "
                + "rather than picking the good address out of it. A refusal of ours is recorded as "
                + "ours and never appears in a game's record as downtime."),
            new("Connect screens are shown because they are sent to everybody.",
                "A server paints its connect screen, unauthenticated, to every anonymous connection "
                + "that arrives. We display it as evidence and label it as what it is. If you would "
                + "rather we did not, say so and it comes down — no questions and no argument."),
            new("Ask, and we stop.",
                "There is no automated opt-out yet. The design calls for one over an MSSP field and "
                + "a DNS TXT record; neither is implemented, and advertising a switch that is not "
                + "wired to anything would be worse than saying so. Until they exist the route is "
                + "to ask a person, and a request is honoured whether or not the machinery for it "
                + "is tidy."),
        ])
    {
        Identity = AboutIdentity.For(probe),
    };

    private static AboutSection Attribution() => new(
        "sources",
        "Where the list of games came from",
        [
            new("We take addresses. Nothing else.",
                "A day-one directory needs games to probe, and the existing directories are the "
                + "best seed there is. What a backfill takes from them is a host and a port — no "
                + "player counts, no reachability history, no descriptions, no fields, and no note "
                + "of which site an address was read on."),
            new("This is deliberately less than those sites can give.",
                "Several of them hold years of dated player counts. Importing that would fill the "
                + "heatmaps of exactly the games somebody else was already watching, in a way no "
                + "reader could tell from our own measurement without reading the fine print, and "
                + "would leave this site's central claim resting on another party's prober."),
            new("A game's origin is not one fact, and would be a misleading one.",
                "Any game worth listing appears in several of these directories, so \"imported "
                + "from\" would name whichever fetch happened to run first rather than anything "
                + "about the game. That a game exists is public information published by its "
                + "operator to be dialled; where we happened to read it adds nothing a reader can "
                + "use, and it is the part of somebody else's work with the least claim to be ours."),
            new("Reading somebody's site is still reading somebody's site.",
                "Taking less data does not make a crawl less of a crawl. A bulk export or a "
                + "documented endpoint is asked for in preference to scraping, robots.txt is read "
                + "first, scrapes are rate-limited hard, and a source that needs its maintainer's "
                + "say-so is not fetched until a person can state that they were asked."),
        ])
    {
        Sources =
        [
            new("TinTin++ MSSP Mud Crawler", "https://tintin.mudhalla.net/protocols/mssp/",
                ImportSourceState.Read,
                "One page for one request, published by a crawler that connects to each game and "
                + "prints what it read."),
            new("TinTin++ MSDP Mud Crawler", "https://tintin.mudhalla.net/protocols/msdp/",
                ImportSourceState.Read,
                "The same crawler's MSDP listing. Very nearly a subset of its MSSP sibling, read "
                + "for the handful of addresses it reaches that the other does not."),
            new("The Mud Connector", "https://www.mudconnect.com/",
                ImportSourceState.Read,
                "Publishes its whole catalogue on one page, so reading all of it costs a single "
                + "request. It is the largest contributor of addresses here and contributes no "
                + "measurement, which is the split working as intended."),
            new("MudStats", "https://mudstats.com/",
                ImportSourceState.Read,
                "One index page and one page per world, which makes it a scrape rather than an "
                + "export. On 30 July 2026 we fetched 143 of their pages — fifteen seconds apart "
                + "and honouring their robots.txt, but before anyone had written to them. That "
                + "should not have happened. The gate that would have stopped it can no longer be "
                + "satisfied by a default in a source file; it now takes a person willing to state "
                + "that the maintainer was asked."),
            new("MudVerse", "https://www.mudverse.com/",
                ImportSourceState.Withheld,
                "Implemented, tested, and never run. It is the strongest source in this list on "
                + "every axis except permission, and nothing will be fetched from it until somebody "
                + "has written to them."),
        ],
    };

    private static AboutSection Licence(DatasetLicenceOptions dataset) => new(
        "licence",
        "Licence",
        [
            new("The code is MIT.",
                "The site, the crawler and the parsers are open source under the MIT licence."),
            new("The licence for the data is an open question.",
                "It is a separate decision from the code's and has not been taken. Anyone planning "
                + "to build on the dataset should treat the terms below as this deployment's "
                + "current answer rather than as the project's settled position — and a rival "
                + "directory taking the whole catalogue is a success condition here, not a threat, "
                + "so whatever is settled will not be written to stand in the way of one."),
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
/// <para>
/// Read off <see cref="ProbeOptions"/> rather than written out here, so the name published on the
/// page is a property of the object the probe is built from.
/// </para>
/// <para>
/// <b><see cref="Announced"/> is false and that is not a formality.</b> Neither
/// <see cref="ProbeOptions.TerminalTypes"/> nor <see cref="ProbeOptions.InfoUrl"/> reaches the wire:
/// TelnetNegotiationCore's client-mode terminal type is a hardcoded private list with no setter, so
/// an administrator reading their logs sees the library's default and not us. The page says so
/// rather than printing a name nobody will ever observe, because "the crawler identifies itself" is
/// a claim about our behaviour and this one would be false.
/// </para>
/// </remarks>
public sealed record AboutIdentity(string Name, string InfoUrl, bool Announced, bool ContactConfigured)
{
    public static AboutIdentity For(ProbeOptions probe) => new(
        probe.TerminalTypes.Count > 0 ? probe.TerminalTypes[0] : "MUINDEX-CRAWLER",
        probe.InfoUrl,
        // Nothing consumes either field yet. When something does, this becomes a property of the
        // probe rather than a constant, and the sentence below changes with it.
        Announced: false,
        // The built-in value is a placeholder on a domain that has not been chosen. Publishing it as
        // the way to reach us, unmarked, would be publishing an address that answers nobody.
        ContactConfigured: probe.InfoUrl != new ProbeOptions().InfoUrl);

    /// <summary>The honest version of "who is this in my logs", in one sentence.</summary>
    public string Wording => Announced
        ? $"The crawler names itself {Name} when a server asks what it is."
        : $"The crawler is configured to call itself {Name}, and does not yet manage to say so: "
        + "the telnet library it uses gives a client no way to set the terminal type it reports, so "
        + "what reaches your logs is that library's own default, and a NEW-ENVIRON request is "
        + "answered from the crawler host's environment rather than with anything about us. Both "
        + "are gaps in the library and both are ours to fix there. Until they are fixed, the way to "
        + "recognise a probe is its shape: one connection, no login, one WHO, gone.";
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
        _ => "not read — waiting on permission",
    };
}

/// <summary>The two licences, which are two decisions and only one of them has been taken.</summary>
public sealed record AboutLicence(
    string CodeLicence,
    string DataLicenceName,
    string? DataLicenceUrl,
    string Attribution,
    string Notice);
