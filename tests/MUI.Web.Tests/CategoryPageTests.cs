using MUI.Catalog;
using MUI.Web;
using MUI.Web.Components;
using MUI.Web.Localization;

namespace MUI.Web.Tests;

/// <summary>
/// The line between a listing that is a page and a listing that is <c>/games</c> with the panel touched.
/// </summary>
/// <remarks>
/// <para>
/// Both halves matter and they pull against each other. Get it too tight and the site is back where
/// it started, with every category telling a search engine to index <c>/games</c> instead. Get it too
/// loose and the facet panel — which has no script, so it submits every control it has — mints an
/// unbounded supply of near-duplicate URLs that each claim to be a page of their own.
/// </para>
/// <para>
/// So the assertions here come in pairs: one category is its own page, and one refinement of it is
/// not.
/// </para>
/// </remarks>
public class CategoryPageTests
{
    // ── what the rule lets through ───────────────────────────────────────────────────────────

    [Test]
    public async Task OneValueOfOneDimensionIsItsOwnPage()
    {
        await using var site = await SiteHost.StartAsync();

        var canonical = Head.Link(await site.Client.GetStringAsync("/games?codebase=PennMUSH"), "canonical");

        await Assert.That(canonical).IsNotNull();
        await Assert.That(canonical!).EndsWith("/games?codebase=PennMUSH");
    }

    [Test]
    [Arguments("codebase")]
    [Arguments("lineage")]
    [Arguments("genre")]
    [Arguments("language")]
    [Arguments("protocol")]
    [Arguments("charset")]
    [Arguments("tls")]
    [Arguments("band")]
    public async Task EveryDimensionTheRuleNamesIsReachableAsAPage(string key)
    {
        // Asserted per dimension rather than over the list, so a dimension added without copy or
        // without a page fails by name.
        await Assert.That(IndexableFacet.Dimensions).Contains(key);

        // "quiet" is a real band token, so the band case resolves the id it actually uses.
        var category = new IndexableFacet.Category(key, "quiet");

        await Assert.That(CategoryCopy.Title(Locales.SourceTag, category)).IsNotEmpty();
        await Assert.That(CategoryCopy.Heading(Locales.SourceTag, category)).IsNotEmpty();
        await Assert.That(CategoryCopy.Description(Locales.SourceTag, category)).IsNotEmpty();
    }

    [Test]
    public async Task ACategoryPageSaysWhatItIsRatherThanWhatEveryListingIs()
    {
        // The defect this whole change is about: every faceted listing carried /games's title and
        // /games's description, which is the shape of one page duplicated rather than a catalogue.
        await using var site = await SiteHost.StartAsync();

        var category = await site.Client.GetStringAsync("/games?codebase=PennMUSH");
        var listing = await site.Client.GetStringAsync("/games");

        await Assert.That(Head.Meta(category, "description"))
            .IsNotEqualTo(Head.Meta(listing, "description"));

        await Assert.That(category).Contains("PennMUSH games");
        await Assert.That(Head.Meta(category, "og:title")!).Contains("PennMUSH");
    }

    [Test]
    public async Task ACategoryPageLeadsWithItsOwnNameRatherThanTheWordGames()
    {
        await using var site = await SiteHost.StartAsync();

        var body = await site.Client.GetStringAsync("/games?protocol=MSSP");

        await Assert.That(Heading(body)).IsEqualTo("Games offering MSSP");
    }

    [Test]
    public async Task TheHeadingNamesThePageWithoutBeingDrawnAboveIt()
    {
        // The chips already say "codebase: PennMUSH" a line lower, so a heading drawn above them
        // spends a band of the viewport repeating them. It still has to exist: it is the on-page
        // name this whole rule turns on, and a listing with no <h1> is one a screen reader cannot
        // announce. So it is hidden, not removed — and the sentence a search result wants stays in
        // <meta>, per the test below.
        await using var site = await SiteHost.StartAsync();

        var body = await site.Client.GetStringAsync("/games?codebase=PennMUSH");

        await Assert.That(Heading(body)).IsEqualTo("PennMUSH games");
        await Assert.That(body).Contains("<h1 class=\"sr-only\">");
    }

    [Test]
    public async Task TheDescriptionIsInTheHeadWhereASearchResultNeedsItAndNotOnThePage()
    {
        await using var site = await SiteHost.StartAsync();

        var body = await site.Client.GetStringAsync("/games?codebase=PennMUSH");

        await Assert.That(Head.Meta(body, "description")!).Contains("Every PennMUSH game we have reached");

        // Once, in the head. The body carries the heading and the rows.
        var head = body[..body.IndexOf("</head>", StringComparison.Ordinal)];
        var rest = body[body.IndexOf("</head>", StringComparison.Ordinal)..];

        await Assert.That(head).Contains("Every PennMUSH game we have reached");
        await Assert.That(rest).DoesNotContain("Every PennMUSH game we have reached");
    }

    [Test]
    public async Task TheUnfilteredListingGainsNoHeaderCopyAtAll()
    {
        await using var site = await SiteHost.StartAsync();

        var body = await site.Client.GetStringAsync("/games");

        await Assert.That(Heading(body)).IsEqualTo("Games");
        await Assert.That(body).DoesNotContain("/reference/codebases/");
    }

    [Test]
    public async Task ASpellingNobodyLinksToCanonicalizesOntoTheOneEverybodyLinksTo()
    {
        // Otherwise a hand-typed URL is a second indexable page for the same set of games, which is
        // the duplication the rule exists to prevent.
        await using var site = await SiteHost.StartAsync();

        var canonical = Head.Link(await site.Client.GetStringAsync("/games?codebase=pennmush"), "canonical");

        await Assert.That(canonical!).EndsWith("/games?codebase=PennMUSH");
    }

    // ── what the rule holds back ─────────────────────────────────────────────────────────────

    [Test]
    [Arguments("/games", "two facets at once is a refinement, not a category")]
    [Arguments("/games?codebase=PennMUSH&protocol=MSSP", "two facets at once is a refinement")]
    [Arguments("/games?codebase=!Evennia", "an exclusion is not a thing anybody searches for by name")]
    [Arguments("/games?codebase=~unknown", "\"we could not read it\" is a fact about us, not a category")]
    [Arguments("/games?codebase=PennMUSH&sort=name", "a chosen sort is the same page in another order")]
    [Arguments("/games?q=dune", "free text is unbounded by construction")]
    public async Task EverythingElseStaysConsolidatedOntoTheListing(string address, string because)
    {
        await using var site = await SiteHost.StartAsync();

        var canonical = Head.Link(await site.Client.GetStringAsync(address), "canonical");

        await Assert.That(canonical).IsNotNull();
        await Assert.That(canonical!).EndsWith("/games").Because(because);
    }

    [Test]
    public async Task AVolatileFacetNamesThePageWithoutClaimingAnIndexEntry()
    {
        // Being about something and being worth indexing are different questions. A reader who
        // filtered to "quiet" should see that in the heading; a crawler should not be handed a URL
        // whose contents have changed by the time it comes back.
        await using var site = await SiteHost.StartAsync();

        var body = await site.Client.GetStringAsync("/games?band=quiet");

        // Its own line, not the facet panel's label: that one carries a definition after an em
        // dash ("quiet — no count above 0"), which reads as a footnote in a heading.
        await Assert.That(Heading(body)).IsEqualTo("Quiet games");
        await Assert.That(Head.Link(body, "canonical")!).EndsWith("/games");
        await Assert.That(Head.Link(body, "canonical")).DoesNotContain("band");
    }

    [Test]
    public async Task TheSitemapOffersNoVolatileCategory()
    {
        await using var site = await SiteHost.StartAsync(measured: true);

        var sitemap = await site.Client.GetStringAsync("/sitemap.xml");

        await Assert.That(sitemap).DoesNotContain("band=");
        await Assert.That(IndexableFacet.Indexable).DoesNotContain(MUI.Catalog.FacetKeys.Band);
    }

    [Test]
    public async Task AListingWhoseFilterDidNotParseIsNeverACategory()
    {
        // The filter is then the default rather than what was asked for, so a canonical URL built
        // from it would name a page returning a different set of games.
        await using var site = await SiteHost.StartAsync();

        var body = await site.Client.GetStringAsync("/games?band=nonsense");

        await Assert.That(Head.Link(body, "canonical")!).EndsWith("/games");
    }

    [Test]
    public async Task TheSameDocumentUnderPlainModeIsStillTheOneCategoryUrl()
    {
        // ?plain=1 is the same document (spec §9), so it must not become a second address for it.
        // It used to: the plain branch rendered no metadata at all, so the text mirror of every page
        // was an untitled, uncanonicalised twin — including this one.
        await using var site = await SiteHost.StartAsync();

        var canonical = Head.Link(
            await site.Client.GetStringAsync("/games?codebase=PennMUSH&plain=1"), "canonical");

        await Assert.That(canonical).IsNotNull();
        await Assert.That(canonical!).EndsWith("/games?codebase=PennMUSH");
        await Assert.That(canonical).DoesNotContain("plain");
    }

    // ── the filter rule itself, without a request in the way ─────────────────────────────────

    [Test]
    public async Task ABaseListingNamesNoCategory()
    {
        await Assert.That(IndexableFacet.Of(new GameFilter { IncludeAdult = false })).IsNull();
    }

    [Test]
    public async Task AdultAndArchivedAreRefinementsRatherThanCategories()
    {
        // Both change which games come back without changing what the page is about.
        await Assert.That(IndexableFacet.Of(new GameFilter
        {
            IncludeAdult = false,
            IncludeArchived = true,
            Codebase = FacetChoice.Of("PennMUSH"),
        })).IsNull();

        await Assert.That(IndexableFacet.Of(new GameFilter
        {
            IncludeAdult = true,
            Codebase = FacetChoice.Of("PennMUSH"),
        })).IsNull();
    }

    [Test]
    public async Task TwoProtocolsAtOnceIsARefinement()
    {
        await Assert.That(IndexableFacet.Of(new GameFilter
        {
            IncludeAdult = false,
            MeasuredProtocols = ["MSSP", "GMCP"],
        })).IsNull();
    }

    [Test]
    public async Task TheOldCodebaseSpellingReachesTheSameCategory()
    {
        // ?codebase-family= is still accepted in a querystring and binds to the same filter, so
        // reading the filter rather than the querystring is what makes the two agree.
        await using var site = await SiteHost.StartAsync();

        var canonical = Head.Link(
            await site.Client.GetStringAsync("/games?codebase-family=PennMUSH"), "canonical");

        await Assert.That(canonical!).EndsWith("/games?codebase=PennMUSH");
    }

    [Test]
    public async Task AQueryIsBuiltEscapedSoAValueWithASpaceStaysOneParameter()
    {
        var query = IndexableFacet.Query(new IndexableFacet.Category("genre", "Science fiction"));

        await Assert.That(query).IsEqualTo("?genre=Science%20fiction");
    }

    // ── the collection graph the category page publishes ─────────────────────────────────────

    [Test]
    public async Task AMeasuredCategoryPagePublishesItselfAsACollectionOfGames()
    {
        await using var site = await SiteHost.StartAsync(measured: true);

        var blocks = Head.StructuredData(await site.Client.GetStringAsync("/games?codebase=PennMUSH"));

        await Assert.That(blocks.Any(b => b.Contains("CollectionPage", StringComparison.Ordinal))).IsTrue();
        await Assert.That(blocks.Any(b => b.Contains("ItemList", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task NoCollectionOfInventedGamesIsPublishedOverTheFixture()
    {
        // Same rule as the game graph: these are game names, and over the fixture they are made up.
        await using var site = await SiteHost.StartAsync();

        foreach (var block in Head.StructuredData(await site.Client.GetStringAsync("/games")))
        {
            await Assert.That(block).DoesNotContain("ItemList");
        }
    }

    [Test]
    public async Task TheCollectionNamesTheAddressTheHeadDeclares()
    {
        // A refined listing canonicalizes to /games, so its collection must say /games too — a graph
        // naming the reader's own URL would contradict the canonical link three lines above it.
        await using var site = await SiteHost.StartAsync(measured: true);

        var blocks = Head.StructuredData(
            await site.Client.GetStringAsync("/games?codebase=PennMUSH&sort=name"));

        foreach (var block in blocks.Where(b => b.Contains("CollectionPage", StringComparison.Ordinal)))
        {
            await Assert.That(block).DoesNotContain("sort=name");
        }
    }

    /// <summary>The document's first heading, which is what a category page renames.</summary>
    /// <remarks>Matched by tag rather than by <c>&lt;h1&gt;</c> exactly: the listing's carries a class.</remarks>
    private static string Heading(string body)
    {
        var tag = body.IndexOf("<h1", StringComparison.Ordinal);
        var open = body.IndexOf('>', tag) + 1;
        var close = body.IndexOf("</h1>", open, StringComparison.Ordinal);

        return System.Net.WebUtility.HtmlDecode(body[open..close]).Trim();
    }
}
