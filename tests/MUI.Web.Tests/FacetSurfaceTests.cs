using System.Reflection;

using MUI.Catalog;
using MUI.Web.Api;
using MUI.Web.Components;
using MUI.Web.Fixtures;

namespace MUI.Web.Tests;

/// <summary>
/// The facet panel where a reader meets it: one vocabulary, a linkable URL, and unknowns that say
/// what they are.
/// </summary>
/// <remarks>
/// The page and the read API share <see cref="GameFilterBinding"/>, so most of what could go wrong
/// between them is a naming slip rather than a logic error — which is why the first tests here walk
/// <see cref="FacetKeys"/> by reflection instead of listing the facets by hand. A facet added to the
/// query and forgotten in the parser is exactly the drift the shared vocabulary exists to prevent,
/// and it is invisible to any test that only exercises the facets somebody remembered.
/// </remarks>
public class FacetSurfaceTests
{
    private static readonly FixtureGameQueries Queries = new();

    private static IReadOnlyList<string> Keys() =>
    [
        .. typeof(FacetKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!),
    ];

    /// <summary>A value each facet can be given, so a key can be checked for being read at all.</summary>
    private static string Sample(string key) => key switch
    {
        FacetKeys.Text => "corvid",
        FacetKeys.Archived => "true",
        FacetKeys.Adult => "true",
        FacetKeys.Tls => "true",
        FacetKeys.Band => "quiet",
        FacetKeys.LastSeen => "week",
        FacetKeys.Protocol => "GMCP",
        // Not the default. The point of the check below is that the parameter changed something,
        // and asking for the order the filter already arrives in changes nothing by construction.
        FacetKeys.Sort => "name",
        _ => "something",
    };

    [Test]
    public async Task EveryFacetTheCatalogueNamesIsOneTheFilterBindingReads()
    {
        // The pin on "one parser, two callers". A key added to FacetKeys and wired into the query
        // but not into the binding would give the page a facet the API cannot express and the URL
        // cannot carry — and it would fail silently, as an option that does nothing.
        GameFilterBinding.TryRead(string.Empty, out var unfiltered, out _);

        foreach (var key in Keys())
        {
            var read = GameFilterBinding.TryRead($"?{key}={Sample(key)}", out var query, out var error);

            await Assert.That(read).IsTrue().Because($"{key}: {error}");
            await Assert.That(Describe(query.Filter))
                .IsNotEqualTo(Describe(unfiltered.Filter))
                .Because($"?{key}= changed nothing, so nothing reads it");
        }
    }

    [Test]
    public async Task EveryFacetTheListingReturnsIsOneTheVocabularyNames()
    {
        // The other direction: a group the query invents a key for would render a control whose
        // name means nothing to the parser, so the click would silently do nothing.
        var listing = await Queries.SearchAsync(new GameFilter { IncludeArchived = true });
        var keys = Keys();

        await Assert.That(listing.Facets).IsNotEmpty();

        foreach (var group in listing.Facets)
        {
            await Assert.That(keys).Contains(group.Key);
        }
    }

    [Test]
    public async Task AFilteredUrlRoundTripsThroughTheFilterAndBackToTheSameWords()
    {
        // A filtered listing has to be linkable, so the URL is the state and nothing else is. What
        // goes in comes back out, including ~unknown, which is a selection and not an empty one.
        const string Url = "?q=corvid&archived=true&band=quiet&seen=week&protocol=GMCP,MSSP"
            + "&tls=true&charset=UTF-8&codebase=Evennia&family=PennMUSH&genre=Fantasy&language=~unknown";

        await Assert.That(GameFilterBinding.TryRead(Url, out var query, out _)).IsTrue();

        var f = query.Filter;
        await Assert.That(f.Text).IsEqualTo("corvid");
        await Assert.That(f.IncludeArchived).IsTrue();
        await Assert.That(f.Tls).IsTrue();
        await Assert.That(f.Band).IsEqualTo(ActivityBand.Quiet);
        await Assert.That(f.LastSeen).IsEqualTo(LastSeenBand.Week);
        await Assert.That(f.MeasuredProtocols).IsEquivalentTo(new[] { "GMCP", "MSSP" });
        await Assert.That(f.Charset!.Value).IsEqualTo("UTF-8");
        await Assert.That(f.Codebase!.Value).IsEqualTo("Evennia");
        await Assert.That(f.Family!.Value).IsEqualTo("PennMUSH");
        await Assert.That(f.Genre!.Value).IsEqualTo("Fantasy");
        await Assert.That(f.Language!.IsUnknown).IsTrue();

        // The echo is built from the filter rather than the query, so this is the trip back.
        var echo = query.Echo;
        await Assert.That(echo.Q).IsEqualTo("corvid");
        await Assert.That(echo.Band).IsEqualTo(ActivityBand.Quiet);
        await Assert.That(echo.Seen).IsEqualTo(LastSeenBand.Week);
        await Assert.That(echo.Charset).IsEqualTo("UTF-8");
        await Assert.That(echo.Language).IsEqualTo(FacetChoice.UnknownToken);
        await Assert.That(echo.Tls).IsTrue();
    }

    [Test]
    public async Task AnUnknownSelectionIsNotTheSameAsNoSelection()
    {
        // A blank parameter asks for anything; ~unknown asks for the games that have nothing. Folding
        // them together would make "codebase we could not identify" unaskable and would quietly
        // re-point every URL that asked it.
        await Assert.That(GameFilterBinding.TryRead("?codebase=", out var blank, out _)).IsTrue();
        await Assert.That(GameFilterBinding.TryRead("?codebase=~unknown", out var none, out _)).IsTrue();

        await Assert.That(blank.Filter.Codebase).IsNull();
        await Assert.That(none.Filter.Codebase!.IsUnknown).IsTrue();
    }

    /// <summary>The old spelling of the codebase question still asks it.</summary>
    /// <remarks>
    /// <c>codebase-family</c> named a filter that ran beside a <c>codebase</c> facet over raw
    /// versioned strings; the two are now one question under one key. Every codebase reference page
    /// has been linking here with the old spelling and readers have bookmarked those links, and a URL
    /// that used to filter and now silently returns the whole catalogue is worse than one that
    /// errors — it looks like an answer.
    /// </remarks>
    [Test]
    public async Task TheOldCodebaseFamilyKeyStillFilters()
    {
        await Assert.That(
            GameFilterBinding.TryRead("?codebase-family=PennMUSH", out var legacy, out _)).IsTrue();
        await Assert.That(legacy.Filter.Codebase).IsEqualTo(FacetChoice.Of("PennMUSH"));

        // Polarity travels with it, because the old key carried tokens rather than bare values.
        await Assert.That(
            GameFilterBinding.TryRead("?codebase-family=!PennMUSH", out var not, out _)).IsTrue();
        await Assert.That(not.Filter.Codebase).IsEqualTo(FacetChoice.Not("PennMUSH"));

        // And a caller who names the current key means it — including when what they name it is
        // nothing. `?codebase=` is a caller clearing the filter, and a stale alias left in the query
        // beside it must not put back the value they just cleared; the older spelling outranking the
        // current one exactly where the two disagree would leave no way to clear it at all.
        await Assert.That(GameFilterBinding.TryRead(
            "?codebase=Evennia&codebase-family=PennMUSH", out var both, out _)).IsTrue();
        await Assert.That(both.Filter.Codebase).IsEqualTo(FacetChoice.Of("Evennia"));

        await Assert.That(GameFilterBinding.TryRead(
            "?codebase=&codebase-family=PennMUSH", out var cleared, out _)).IsTrue();
        await Assert.That(cleared.Filter.Codebase).IsNull();

        // The echo answers in both spellings. A consumer on API version 1 that reads
        // `codebaseFamily` off the response has working code, and a field that vanished from a
        // version that did not change reads to it as "the filter was not applied".
        await Assert.That(legacy.Echo.CodebaseFamily).IsEqualTo("PennMUSH");
        await Assert.That(legacy.Echo.Codebase).IsEqualTo("PennMUSH");
        await Assert.That(both.Echo.CodebaseFamily).IsEqualTo(both.Echo.Codebase);
    }

    /// <summary>Clearing the codebase clears it under either spelling.</summary>
    /// <remarks>
    /// The chip the panel draws for a selected codebase removes <c>codebase</c>. A reader who
    /// arrived from a reference page has <c>codebase-family</c> in their URL instead, so a removal
    /// that only knew the new key would leave the filter in place — the one gesture on the page whose
    /// whole job is to undo something, doing nothing, for exactly the readers who did not choose it.
    /// </remarks>
    [Test]
    public async Task RemovingTheCodebaseChipDropsTheOldKeyToo()
    {
        await Assert.That(ListingLinks.With("?codebase-family=PennMUSH&genre=Fantasy", FacetKeys.Codebase, null))
            .IsEqualTo("?genre=Fantasy");

        await Assert.That(ListingLinks.With("?codebase-family=PennMUSH", FacetKeys.Codebase, "Evennia"))
            .IsEqualTo("?codebase=Evennia");

        // Symmetric, because an alias that works in one direction is not an alias. Nothing calls this
        // with the old spelling today — the panel only rewrites keys it draws a facet for — and the
        // asymmetry stops being unreachable the moment something does.
        await Assert.That(ListingLinks.With("?codebase=PennMUSH&genre=Fantasy", FacetKeys.CodebaseFamily, null))
            .IsEqualTo("?genre=Fantasy");
        await Assert.That(ListingLinks.With(
            "?codebase=PennMUSH&codebase-family=TinyMUX", FacetKeys.CodebaseFamily, null))
            .IsEqualTo("");
    }

    [Test]
    public async Task AnUnreadableFacetIsRefusedRatherThanQuietlyDropped()
    {
        // Listing the whole catalogue under a filter that did not apply presents our own parse
        // failure as somebody's answer — the same rule that stops an unparseable WHO reading as zero.
        await Assert.That(GameFilterBinding.TryRead("?band=wat", out _, out var band)).IsFalse();
        await Assert.That(band).Contains("activeThisWeek");

        await Assert.That(GameFilterBinding.TryRead("?seen=someday", out _, out var seen)).IsFalse();
        await Assert.That(seen).Contains("never");
    }

    [Test]
    public async Task ThePanelIsAPlainGetFormWithAControlPerFacet()
    {
        // No script, so the querystring is the state: the back button works, a filtered listing is
        // linkable, and the server recomputes every count on every request.
        var html = await PanelAsync(new GameFilter());

        await Assert.That(html).Contains("method=\"get\"");
        await Assert.That(html).Contains("action=\"/games\"");
        await Assert.That(html).DoesNotContain("<script");

        foreach (var group in (await Queries.SearchAsync(new GameFilter())).Facets)
        {
            await Assert.That(html).Contains($"name=\"{group.Key}\"");
        }
    }

    [Test]
    public async Task EveryValueOnThePanelCarriesTheCountChoosingItReturns()
    {
        var listing = await Queries.SearchAsync(new GameFilter());
        var html = Render.Words(await PanelAsync(new GameFilter()));

        foreach (var group in listing.Facets.Where(g => g.Kind is FacetKind.Choice))
        {
            foreach (var value in group.Values)
            {
                await Assert.That(html)
                    .Contains($"{FacetWords.Value(group.Key, value)} ({value.Count})");
            }
        }
    }

    [Test]
    public async Task ThePanelSaysInWordsThatAnUnknownIsNotANo()
    {
        // Said in the markup, not only in a comment. A reader ticking boxes is exactly the person who
        // would otherwise read an unticked box as the game declining a protocol.
        //
        // The prose these used to sit in is gone — the panel explained itself at more length than it
        // took to operate — so each of them now has a place of its own: the tick-box reading is a
        // line inside the fieldset it is about, and the rest is one disclosure. What may not change
        // is that they are all still in the document, which is what this asserts.
        var words = Render.Words(await PanelAsync(new GameFilter()));

        await Assert.That(words).Contains("Unticked is not a no");
        await Assert.That(words).Contains("A blank is a gap in our measurement, not a no");
        await Assert.That(words).Contains("not identified");
        await Assert.That(words).Contains("from the same query as the list below");
        await Assert.That(words).Contains("An unknown count is not a zero");
    }

    [Test]
    public async Task TheDisclosureIsAnAffordanceRatherThanAPlaceToHideThings()
    {
        // <details> is closed by default and its contents are still in the document, in the
        // accessibility tree and in the page a text browser gets — which is what makes it a fair
        // place to put the long form of a rule. It stops being fair the moment the summary stops
        // saying what is inside it, so the summary is asserted too.
        var html = await PanelAsync(new GameFilter());

        await Assert.That(html).Contains("<summary>");
        await Assert.That(Render.Words(html)).Contains("what a blank means");
    }

    [Test]
    public async Task AFacetWithNoValueForAGameSpellsThatOutInItsOwnWords()
    {
        // Three sentences because they are three different facts: a codebase we could not identify
        // is a limit of our parsers, a genre nobody declared is a limit of what the game published,
        // and an encoding nothing negotiated is a limit of the handshake.
        var words = Render.Words(await PanelAsync(new GameFilter()));

        await Assert.That(words).Contains("not identified");
        await Assert.That(words).Contains("not declared");
        await Assert.That(words).Contains("nothing negotiated");

        await Assert.That(FacetWords.Unknown(FacetKeys.Codebase)).IsNotEqualTo("no");
        await Assert.That(FacetWords.Unknown(FacetKeys.Genre))
            .IsNotEqualTo(FacetWords.Unknown(FacetKeys.Charset));
    }

    [Test]
    public async Task EachFacetSaysWhetherItIsAMeasurementOrTheGamesOwnClaim()
    {
        // The distinction is the product, and it is carried in words as well as colour — a legend a
        // reader has to learn is a difference they will not read.
        //
        // It is now one word per facet with the sentence said once, rather than the sentence on
        // every facet. Both halves are asserted: the word has to be on the group (or the panel has
        // stopped saying which half of itself is evidence) and the meaning has to be on the page (or
        // the word is a legend nobody was given).
        var words = Render.Words(await PanelAsync(new GameFilter()));

        // Every register, not the two that were here first: "derived" is the one a reader is least
        // likely to guess at and the one it would matter most to leave unexplained.
        foreach (var evidence in Enum.GetValues<FacetEvidence>())
        {
            await Assert.That(words).Contains(FacetWords.Evidence(evidence));
            await Assert.That(words).Contains(FacetWords.EvidenceMeaning(evidence));
        }

        // And no two of them say the same thing, or the panel has a distinction it cannot draw.
        var registers = Enum.GetValues<FacetEvidence>().Length;
        await Assert.That(Enum.GetValues<FacetEvidence>().Select(FacetWords.Evidence).Distinct().Count())
            .IsEqualTo(registers);
        await Assert.That(
            Enum.GetValues<FacetEvidence>().Select(FacetWords.EvidenceMeaning).Distinct().Count())
            .IsEqualTo(registers);
    }

    [Test]
    public async Task AGroupsEvidenceIsSaidOnTheGroupAndNotOnlyInTheKey()
    {
        // A word in a key at the bottom of the panel is not the same as a word on the control. The
        // point of the compression was to stop the panel repeating a sentence per facet, not to move
        // the fact off the facets.
        var html = await PanelAsync(new GameFilter());
        var listing = await Queries.SearchAsync(new GameFilter());

        foreach (var group in listing.Facets)
        {
            // Read off the label element itself rather than off an offset into the page: the facet
            // names are ordinary English words and several of them occur in the option text of other
            // facets, so anything positional here measures the wrong thing.
            var marker = $"<span class=\"facet-name\">{FacetWords.Group(group.Key)}</span>";
            var at = html.IndexOf(marker, StringComparison.Ordinal);

            await Assert.That(at).IsGreaterThanOrEqualTo(0).Because($"{group.Key} has no name of its own");

            var rest = html[(at + marker.Length)..];
            var label = rest[..new[]
            {
                rest.IndexOf("</label>", StringComparison.Ordinal),
                rest.IndexOf("</legend>", StringComparison.Ordinal),
            }.Where(i => i >= 0).Min()];

            // The class and the word are the same string on purpose: a second copy of the mapping
            // here is a place for the test and the panel to drift apart, and it did — a register
            // added to the enum arrived in this helper as "declared".
            await Assert.That(label).Contains($"evidence {FacetWords.Evidence(group.Evidence)}")
                .Because($"{group.Key} has no evidence chip beside its own name");
        }
    }

    [Test]
    public async Task AChosenValueIsMarkedSoTheFormShowsWhatTheUrlAsked()
    {
        var filter = new GameFilter { Band = ActivityBand.Archived, IncludeArchived = true };
        var html = await PanelAsync(filter);

        await Assert.That(html).Contains("selected");
        await Assert.That(html).Contains("checked");
    }

    [Test]
    public async Task ThePlainSurfaceCarriesTheWholePanelIncludingItsCounts()
    {
        // A text browser cannot operate a <select> but can perfectly well edit a URL, so the plain
        // listing prints every value, its count and the parameter that selects it. If a fact cannot
        // survive here, its graphic on the main site is decoration.
        var listing = await Queries.SearchAsync(new GameFilter());
        var text = PlainText.RenderListing(listing, new GameFilter(), FixtureGameQueries.Now);

        await Assert.That(text).Contains("FILTERS");

        foreach (var group in listing.Facets)
        {
            await Assert.That(text).Contains($"(?{group.Key}=");

            foreach (var value in group.Values)
            {
                await Assert.That(text).Contains(value.Token);
            }
        }

        // Read off the collapsed text: the paragraph is wrapped to eighty columns, so asserting on
        // the raw bytes would be asserting on where the wrap happened to fall.
        await Assert.That(Render.Words(text)).Contains("never a \"no\"");
    }

    [Test]
    public async Task NoPlainLineTheFacetsAddIsWiderThanEightyColumns()
    {
        var listing = await Queries.SearchAsync(new GameFilter { IncludeArchived = true });
        var text = PlainText.RenderListing(listing, new GameFilter(), FixtureGameQueries.Now);

        foreach (var line in text.Split('\n'))
        {
            await Assert.That(line.TrimEnd().Length).IsLessThanOrEqualTo(PlainText.Columns);
        }
    }

    [Test]
    public async Task WhatTheQueryIsAskingForIsRepeatedAsChipsThatRemoveThemselves()
    {
        // The panel is not the only place the query is visible. A <select> sitting on an option in
        // its "anything but" group looks like every other select until it is opened, so without
        // these the third state is invisible at rest — and a reader who wants to drop one filter has
        // to find the control that set it and remember what "any" was called.
        const string Url = "?codebase=!Evennia&protocol=GMCP&protocol=MSSP&q=sun";

        await Assert.That(GameFilterBinding.TryRead(Url, out var query, out _)).IsTrue();
        var listing = await Queries.SearchAsync(query.Filter);
        var chips = ActiveFilters.For(listing.Facets, query.Filter, Url);

        await Assert.That(chips.Select(c => c.Value)).Contains("not Evennia");
        await Assert.That(chips.Select(c => c.Value)).Contains("sun");

        // Removing one value of a repeatable facet leaves the other behind. Dropping the whole
        // parameter would take MSSP off the query too, and the chip said nothing about MSSP.
        var gmcp = chips.Single(c => c.Value is "GMCP");
        await Assert.That(gmcp.RemoveHref).Contains("MSSP");
        await Assert.That(gmcp.RemoveHref).DoesNotContain("GMCP");
    }

    [Test]
    public async Task NothingSelectedIsNoChipsAtAll()
    {
        // Through the binding rather than from `new GameFilter()`, because "nothing selected" is a
        // fact about a URL and the binding is what turns one into a filter. The two are not the
        // same object: GameFilter.IncludeAdult defaults to true so that the data dump and the home
        // page's counts go on counting the whole catalogue, and it is the empty *query* — this
        // listing, with nothing asked of it — that has adult games out.
        await Assert.That(GameFilterBinding.TryRead(string.Empty, out var query, out _)).IsTrue();

        var listing = await Queries.SearchAsync(query.Filter);

        await Assert.That(ActiveFilters.For(listing.Facets, query.Filter, string.Empty)).IsEmpty();
    }

    [Test]
    public async Task AnUnreadableSortIsRefusedRatherThanQuietlyIgnored()
    {
        // The same rule as band and seen. A consumer who asked for ?sort=busiest and silently got
        // the alphabet would read the first name on the page as the busiest game on the site.
        await Assert.That(GameFilterBinding.TryRead("?sort=busiest", out _, out var error)).IsFalse();
        await Assert.That(error).Contains("players");
    }

    [Test]
    public async Task TheSortIsAControlOnThePanelAndAParameterInTheUrl()
    {
        var html = await PanelAsync(new GameFilter { Sort = GameSort.Players });

        await Assert.That(html).Contains($"name=\"{FacetKeys.Sort}\"");

        foreach (var sort in Enum.GetValues<GameSort>())
        {
            await Assert.That(Render.Words(html)).Contains(FacetWords.Sort(sort));
        }
    }

    [Test]
    public async Task NoSortCallsItselfBusiestBecauseTheRankingsPageAlreadyMeansSomethingByThat()
    {
        // /rankings means a median over ninety days with a sample floor under it. A sort over one
        // instantaneous count is a cruder question and must not borrow the word — two measurements
        // answering to one name on one site is how a reader ends up comparing them.
        foreach (var sort in Enum.GetValues<GameSort>())
        {
            await Assert.That(FacetWords.Sort(sort)).DoesNotContain("busiest");
            await Assert.That(FacetWords.Sort(sort)).DoesNotContain("popular");
        }
    }

    private static async Task<string> PanelAsync(GameFilter filter)
    {
        var listing = await Queries.SearchAsync(filter);

        return await Render.ComponentAsync<FacetPanel>(new()
        {
            ["Facets"] = listing.Facets,
            ["Filter"] = filter,
        });
    }

    /// <summary>
    /// A filter as one comparable string.
    /// </summary>
    /// <remarks>
    /// Record equality would compare <see cref="GameFilter.MeasuredProtocols"/> by reference and so
    /// call every parsed filter different from every other — which would make the test above pass
    /// whatever the parser did with a key.
    /// </remarks>
    private static string Describe(GameFilter f) => string.Join(
        '|',
        f.Text,
        f.IncludeArchived,
        f.IncludeAdult,
        f.Tls,
        f.Band,
        f.LastSeen,
        f.Charset?.Token,
        f.Codebase?.Token,
        f.CodebaseVersion?.Token,
        f.Lineage?.Token,
        f.Family?.Token,
        f.Genre?.Token,
        f.Language?.Token,
        f.Sort,
        string.Join(',', f.MeasuredProtocols));
}
