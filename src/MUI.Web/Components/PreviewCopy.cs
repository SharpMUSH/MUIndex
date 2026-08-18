using System.Globalization;

using MUI.Catalog;
using MUI.Web.Localization;

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
/// <para>
/// <b>And a preview localizes, because a page does.</b> These were English constants passed to
/// <c>SitePreview</c> from every page, so a German page had a German body and an English
/// <c>&lt;title&gt;</c>, description and Open Graph card — it advertised itself in a language its
/// reader had not chosen, to a reader, a search engine and every client that unfurls a pasted link.
/// A sweep that walks visible text and <c>title</c>/<c>aria-label</c>/<c>placeholder</c> never sees
/// a <c>&lt;meta&gt;</c>, which is why this outlived four locales shipping.
/// </para>
/// </remarks>
public static class PreviewCopy
{
    /// <summary>
    /// The wordmark, as an unfurler prints it beside the title.
    /// </summary>
    /// <remarks>
    /// Not a message id and never one. The site's name is machine voice, like a hostname or a
    /// codebase string; it is handed to the bundle as an argument so a translator can move it
    /// within a sentence and cannot translate it.
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
    /// A description repeated across a site is one a search engine discards, and a page with none
    /// is summarised from whatever text happens to come first in its body — which on this site is
    /// the demo banner or a facet panel. These are the ids rather than the sentences now — the text
    /// is in <c>Messages</c> with the rest of the site's copy — and they stay gathered here so that
    /// a page names one thing and so that the set can still be read as prose in one place.
    /// </remarks>
    public static class Pages
    {
        public static string Games(string tag) => Messages.For(tag, "preview.desc.games");

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
    /// same word. A title is a noun phrase in a browser tab and a heading is the first line of a
    /// document; the languages that decline the two differently have nowhere to stand if they share
    /// an id, which is S7's whole argument for granularity the source language does not need.
    /// </remarks>
    public static class Titles
    {
        public static string Games(string tag) => Messages.For(tag, "preview.title.games");

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
    /// In order: what the game says it is, how many people were on it and how we know, and where to
    /// connect. An unfurler truncates, so the ordering is the design — the address survives being
    /// cut, and the sentence that identifies the game does not.
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
            // one. What matters is that it went dark and roughly when — and §7.4's promise that it
            // is still here, which is the thing no incumbent directory can say.
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
    /// <b>Null is a first-class answer here and is never a zero.</b> A game that answers and
    /// publishes nothing we can parse has an unknown count, and rendering that as "0 players" in a
    /// preview states our parser's limit as a fact about their game (rule 4, rule 5). A measured
    /// zero is the opposite case and is published: we got in and nobody was there.
    /// </remarks>
    private static string? Count(string tag, GameSummary summary, DateTimeOffset now)
    {
        if (summary.PlayersNow is not { } players)
        {
            return Messages.For(tag, "preview.game.countUnknown");
        }

        if (summary.PlayersNowProvenance is not { } chip)
        {
            // A value with no chip is a value nobody labelled, and the whole site's claim is that
            // there are none of those. Saying the number without the label would be the one thing
            // §10.1 named as the API's way of contradicting the project.
            return null;
        }

        // The noun agrees with the count inside the message rather than being chosen here: picking
        // "player" or "players" in C# is an English plural rule compiled into a component, and the
        // languages with three forms have nowhere to put the other two.
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
    public static string CardAlt(string tag, string? title) =>
        title is null
            ? Messages.For(tag, "preview.cardAlt", Args(("site", SiteName)))
            : Messages.For(tag, "preview.cardAlt.named", Args(("title", title), ("site", SiteName)));

    /// <summary>The document title, with the wordmark the site is known by.</summary>
    /// <remarks>
    /// The front page is the bare wordmark and goes nowhere near the bundle: there is no sentence
    /// there, only the name. Every other page is a page name joined to that name, and the joining
    /// is a message because the order and the separator are not English's to fix.
    /// </remarks>
    public static string Title(string tag, string? page) =>
        page is null
            ? SiteName
            : Messages.For(tag, "preview.documentTitle", Args(("page", page), ("site", SiteName)));

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] args) =>
        args.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal);
}
