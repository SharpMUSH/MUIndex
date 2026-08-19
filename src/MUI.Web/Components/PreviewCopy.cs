using System.Globalization;

using MUI.Catalog;
using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>
/// What this site says about itself where it is not this site — a search result, a chat client
/// unfurling a pasted link, a bookmark.
/// </summary>
/// <remarks>
/// The five rules do not stop at the edge of the body: a count here carries how it was obtained and
/// its age, an unknown count is omitted rather than rounded to zero, and an asserted number is never
/// described as measured. The demo banner cannot follow it here — no unfurler renders a body — so
/// <see cref="Demo"/> puts the confession in the metadata itself. And previews localize, because a
/// page does: these used to be English constants passed from every page, so a German page had a
/// German body and an English title/description/OG card, invisible to any sweep that only checks
/// visible text and attributes.
/// </remarks>
public static class PreviewCopy
{
    /// <summary>
    /// The wordmark, as an unfurler prints it beside the title.
    /// </summary>
    /// <remarks>
    /// Never a message id: the site's name is machine voice, handed to the bundle as an argument so
    /// a translator can move it but not translate it.
    /// </remarks>
    public const string SiteName = "mu*index";

    /// <summary>What the site is, in one sentence, for every page that has nothing better to say.</summary>
    public static string Site(string tag) => Messages.For(tag, "preview.site");

    /// <summary>Prefixed over the fixture, on every surface no banner reaches.</summary>
    public static string Demo(string tag, string description) =>
        Messages.For(tag, "preview.demo", Args(("description", description)));

    /// <summary>
    /// One sentence per surface, kept together rather than inline in fifteen components.
    /// </summary>
    /// <remarks>
    /// A description repeated across a site is one a search engine discards, and a page with none is
    /// summarised from whatever text comes first in its body — the demo banner, on this site. These
    /// are ids into <c>Messages</c>, gathered here so a page names one thing.
    /// </remarks>
    public static class Pages
    {
        public static string Games(string tag) => Messages.For(tag, "preview.desc.games");

        public static string Activity(string tag) => Messages.For(tag, "preview.desc.activity");

        public static string Archive(string tag) => Messages.For(tag, "preview.desc.archive");

        public static string Rankings(string tag) => Messages.For(tag, "preview.desc.rankings");

        public static string Ecosystem(string tag) => Messages.For(tag, "preview.desc.ecosystem");

        public static string Reference(string tag) => Messages.For(tag, "preview.desc.reference");

        public static string About(string tag) => Messages.For(tag, "preview.desc.about");

        public static string NotFound(string tag) => Messages.For(tag, "preview.desc.notFound");

        public static string Random(string tag) => Messages.For(tag, "preview.desc.random");

        public static string Account(string tag) => Messages.For(tag, "preview.desc.account");

        public static string Claim(string tag) => Messages.For(tag, "preview.desc.claim");
    }

    /// <summary>The title each page puts before the wordmark.</summary>
    /// <remarks>
    /// Separate ids from the <c>&lt;h1&gt;</c> the same page draws, even where the English is the
    /// same word: a title is a browser-tab noun phrase, a heading is a document's first line, and
    /// languages that decline the two differently need separate ids to say so.
    /// </remarks>
    public static class Titles
    {
        public static string Games(string tag) => Messages.For(tag, "preview.title.games");

        public static string Activity(string tag) => Messages.For(tag, "preview.title.activity");

        public static string Archive(string tag) => Messages.For(tag, "preview.title.archive");

        public static string Rankings(string tag) => Messages.For(tag, "preview.title.rankings");

        public static string Ecosystem(string tag) => Messages.For(tag, "preview.title.ecosystem");

        public static string Reference(string tag) => Messages.For(tag, "preview.title.reference");

        public static string About(string tag) => Messages.For(tag, "preview.title.about");

        public static string NotFound(string tag) => Messages.For(tag, "preview.title.notFound");

        public static string Random(string tag) => Messages.For(tag, "preview.title.random");

        public static string Account(string tag) => Messages.For(tag, "preview.title.account");

        /// <summary>A claim page names its game, which is the game's own bytes and not ours.</summary>
        public static string Claim(string tag, string game) =>
            Messages.For(tag, "preview.title.claim", Args(("game", game)));
    }

    /// <summary>
    /// A game, in the two or three facts worth having before you click.
    /// </summary>
    /// <remarks>
    /// In order: what the game says it is, how many were on it and how we know, and where to
    /// connect. An unfurler truncates, so the address survives being cut and the identifying
    /// sentence does not.
    /// </remarks>
    public static string ForGame(string tag, GamePage page, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tag);
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
            // one; what matters is that it went dark and roughly when.
            sentences.Add(summary.LastReachableAt is { } last
                ? Messages.For(
                    tag,
                    "preview.game.archived",
                    Args(("age", Relative.Ago(tag, now - last, AgeSense.Reached))))
                : Messages.For(tag, "preview.game.archived.undated"));
        }
        else if (Count(tag, summary, now) is { } count)
        {
            sentences.Add(count);
        }

        if (Address(page) is { } address)
        {
            sentences.Add(address);
        }

        return sentences.Count == 0
            ? Site(tag)
            : string.Join(" ", sentences.Select(Terminated));
    }

    /// <summary>
    /// The count and its provenance, or nothing.
    /// </summary>
    /// <remarks>
    /// Null is never a zero: a game that publishes nothing we can parse has an unknown count, and
    /// rendering that as "0 players" states our parser's limit as a fact about their game (rule 4,
    /// rule 5). A measured zero — we got in and nobody was there — is published.
    /// </remarks>
    private static string? Count(string tag, GameSummary summary, DateTimeOffset now)
    {
        if (summary.PlayersNow is not { } players)
        {
            return Messages.For(tag, "preview.game.countUnknown");
        }

        if (summary.PlayersNowProvenance is not { } chip)
        {
            // A value with no chip is a value nobody labelled — publishing the number without the
            // label would contradict the whole site's claim (§10.1).
            return null;
        }

        // The noun agrees with the count inside the message rather than being chosen here: an
        // English plural rule compiled into C# has nowhere for a three-form language to fit.
        return Messages.For(
            tag,
            "preview.game.count",
            Args(
                ("count", players),
                ("how", Provenance.How(tag, chip)),
                ("age", Relative.Ago(tag, now - chip.LastConfirmedAt))));
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
    /// Tagline first, since it was written to be short. The self-description is truncated on a word
    /// boundary rather than mid-word — an unfurler cutting it again is fine, mid-word looks like
    /// corruption.
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
    public static string CardAlt(string tag, string? title) =>
        title is null
            ? Messages.For(tag, "preview.cardAlt", Args(("site", SiteName)))
            : Messages.For(tag, "preview.cardAlt.named", Args(("title", title), ("site", SiteName)));

    /// <summary>The document title, with the wordmark the site is known by.</summary>
    /// <remarks>
    /// The front page is the bare wordmark. Every other page joins a page name to it via a message,
    /// since the order and separator are not English's to fix.
    /// </remarks>
    public static string Title(string tag, string? page) =>
        page is null
            ? SiteName
            : Messages.For(tag, "preview.documentTitle", Args(("page", page), ("site", SiteName)));

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] args) =>
        args.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal);
}
