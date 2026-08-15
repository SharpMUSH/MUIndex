using System.Globalization;

using MUI.Catalog;

namespace MUI.Web.Components;

/// <summary>
/// What this site says about itself where it is not this site — a search result, a chat client
/// unfurling a pasted link, a bookmark.
/// </summary>
/// <remarks>
/// <para>
/// <b>The five rules do not stop at the edge of the body.</b> A preview is a surface: it is
/// generated from the same measurements, read by more people than the page in some cases, and read
/// by people with no way at all to check it. So the count it quotes carries how it was obtained and
/// how old it is, exactly as the chip beside it on the page does; an unknown count is omitted
/// rather than rounded to zero; and a number a game asserted about itself is never described as one
/// we took.
/// </para>
/// <para>
/// <b>And the demo banner cannot follow it.</b> <c>MainLayout</c> writes "nothing here was
/// measured" into the body, and no unfurler renders a body. Over the fixture the confession has to
/// be in the metadata itself, which is what <see cref="Demo"/> is for — otherwise a pasted demo
/// link is indistinguishable from a measured one in the context where a reader can check least.
/// </para>
/// </remarks>
public static class PreviewCopy
{
    /// <summary>The wordmark, as an unfurler prints it beside the title.</summary>
    public const string SiteName = "mu*index";

    /// <summary>What the site is, in one sentence, for every page that has nothing better to say.</summary>
    public const string Site =
        "An information site for the MU* hobby — MUSHes, MUDs, MUCKs, MOOs — whose data is measured "
        + "rather than asserted. Every fact carries how it was obtained and how old it is.";

    /// <summary>Prefixed over the fixture, on every surface no banner reaches.</summary>
    public static string Demo(string description) =>
        $"Demo data — nothing here was measured. {description}";

    /// <summary>
    /// One sentence per surface, kept together rather than inline in fifteen components.
    /// </summary>
    /// <remarks>
    /// A description repeated across a site is one a search engine discards, and a page with none
    /// is summarised from whatever text happens to come first in its body — which on this site is
    /// the demo banner or a facet panel. They live here for the same reason <c>EcosystemCopy</c> and
    /// <c>SubmitCopy</c> exist: prose that has to stay consistent is easier to keep consistent when
    /// it is in one file, and easier to correct by somebody reading it as prose.
    /// </remarks>
    public static class Pages
    {
        public const string Games =
            "Every MU* we have reached, faceted by what was measured rather than by what was "
            + "claimed — codebase, the protocols a server actually offered in the handshake, TLS, "
            + "charset, language, and when we last got in.";

        public const string Archive =
            "The games that went dark, kept. Every one still has its page, its history and its "
            + "URL, is still probed every week, and comes back to the listing on one successful "
            + "connection. This is the record the incumbent directories threw away.";

        public const string Rankings =
            "Busiest, most reachable, longest running — computed from measurements only. There is "
            + "no vote, star or rating anywhere on this site, which is the thing that killed the "
            + "directories that had them.";

        public const string Ecosystem =
            "Codebase share and protocol adoption across the games we measure, with what servers "
            + "offer set beside what they declare. Shares and never totals: an absolute population "
            + "figure would not survive the biases in what a crawler can reach.";

        public const string Reference =
            "Hand-written pages on the codebases, clients and protocols of the MU* hobby — curated, "
            + "versioned in git, and cross-linked to counts taken from the crawl rather than typed "
            + "in by hand.";

        public const string About =
            "How this catalogue is built: what the crawler does, what it refuses to do, how to make "
            + "it stop, and why every fact on the site carries where it came from and how old it is.";

        public const string NotFound =
            "No game here. Nothing on this site is ever deleted, so a game that once lived at this "
            + "address still does — check the spelling.";

        public const string Random =
            "One game from the catalogue, chosen at random and never the same one twice.";
    }

    /// <summary>
    /// A game, in the two or three facts worth having before you click.
    /// </summary>
    /// <remarks>
    /// In order: what the game says it is, how many people were on it and how we know, and where to
    /// connect. An unfurler truncates, so the ordering is the design — the address survives being
    /// cut, and the sentence that identifies the game does not.
    /// </remarks>
    public static string ForGame(GamePage page, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(page);

        var summary = page.Summary;
        var sentences = new List<string>();

        if (Lede(page) is { } lede)
        {
            sentences.Add(lede);
        }

        if (summary.State is LifecycleState.Archived)
        {
            // A count on an archived game would be a stale number given equal billing with a live
            // one. What matters is that it went dark and roughly when — and §7.4's promise that it
            // is still here, which is the thing no incumbent directory can say.
            sentences.Add(summary.LastReachableAt is { } last
                ? $"Archived — last reachable {Relative.Ago(now - last)}, and still probed"
                : "Archived, and still probed");
        }
        else if (Count(summary, now) is { } count)
        {
            sentences.Add(count);
        }

        if (Address(page) is { } address)
        {
            sentences.Add(address);
        }

        return sentences.Count == 0
            ? Site
            : string.Join(" ", sentences.Select(Terminated));
    }

    /// <summary>
    /// The count and its provenance, or nothing.
    /// </summary>
    /// <remarks>
    /// <b>Null is a first-class answer here and is never a zero.</b> A game that answers and
    /// publishes nothing we can parse has an unknown count, and rendering that as "0 players" in a
    /// preview states our parser's limit as a fact about their game (rule 4, rule 5). A measured
    /// zero is the opposite case and is published: we got in and nobody was there.
    /// </remarks>
    private static string? Count(GameSummary summary, DateTimeOffset now)
    {
        if (summary.PlayersNow is not { } players)
        {
            return "Player count unknown — the game answers, and publishes no number we can read";
        }

        var word = players == 1 ? "player" : "players";

        if (summary.PlayersNowProvenance is not { } chip)
        {
            // A value with no chip is a value nobody labelled, and the whole site's claim is that
            // there are none of those. Saying the number without the label would be the one thing
            // §10.1 named as the API's way of contradicting the project.
            return null;
        }

        return $"{players} {word}, {Provenance.How(chip)} {Relative.Ago(now - chip.LastConfirmedAt)}";
    }

    /// <summary>Where you connect, which is the fact a reader most often wanted the page for.</summary>
    private static string? Address(GamePage page)
    {
        var endpoint = page.Endpoints.FirstOrDefault(e => e.IsCurrent) ?? page.Endpoints.FirstOrDefault();

        if (endpoint is null)
        {
            return null;
        }

        var codebase = page.Summary.Codebase is { } known ? $"{known} at " : string.Empty;

        return $"{codebase}{endpoint.Host} {endpoint.Port.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// The game's own one-liner, or its own paragraph cut to one sentence.
    /// </summary>
    /// <remarks>
    /// The tagline first because it was written to be short. The self-description is a paragraph and
    /// gets truncated on a word boundary rather than mid-word — an unfurler will cut it again
    /// anyway, and being cut twice is fine while being cut mid-word looks like corruption.
    /// </remarks>
    private static string? Lede(GamePage page)
    {
        var text = page.Summary.Tagline ?? page.Description;

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        const int limit = 140;

        if (text.Length <= limit)
        {
            return text.TrimEnd('.');
        }

        var cut = text.LastIndexOf(' ', limit);

        return (cut > 0 ? text[..cut] : text[..limit]).TrimEnd('.', ',', ';', ' ') + "…";
    }

    private static string Terminated(string sentence) =>
        sentence.EndsWith('.') || sentence.EndsWith('…') || sentence.EndsWith('!') || sentence.EndsWith('?')
            ? sentence
            : sentence + ".";

    /// <summary>The alt text on a preview card, for a reader whose client reads it out.</summary>
    public static string CardAlt(string? title) =>
        title is null
            ? $"{SiteName} — measured, not asserted"
            : $"{title} on {SiteName}";

    /// <summary>The document title, with the wordmark the site is known by.</summary>
    public static string Title(string? page) =>
        page is null ? SiteName : $"{page} — {SiteName}";
}
