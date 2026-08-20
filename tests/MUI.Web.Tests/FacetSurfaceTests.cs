using MUI.Web.Localization;
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
/// The first tests walk <see cref="FacetKeys"/> by reflection rather than listing facets by hand, so
/// a facet added to the query and forgotten in the parser can't hide behind a hand-picked list.
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

        // One word each, like the two bands above and unlike the open-ended facets: their
        // vocabularies are a single value, so the binding refuses anything else rather than
        // narrowing a listing to nothing on a typo.
        FacetKeys.Uncounted => "yes",
        FacetKeys.Unreachable => "yes",
        FacetKeys.Protocol => "GMCP",
        FacetKeys.Trending => "up",
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
            + "&tls=true&charset=UTF-8&codebase=Evennia&family=PennMUSH&trending=up&genre=Fantasy&language=~unknown";

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
        await Assert.That(f.Trending!.Value).IsEqualTo("up");
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
    /// <c>codebase-family</c> is now one question under <c>codebase</c>, but reference pages and
    /// bookmarks still link with the old key — a URL that silently stopped filtering would look like
    /// an answer rather than an error.
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
    /// A reader arriving from a reference page has <c>codebase-family</c> in their URL instead of
    /// <c>codebase</c>; a removal that only knew the new key would leave the filter in place.
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

        await Assert.That(GameFilterBinding.TryRead("?trending=sideways", out _, out var trending)).IsFalse();
        await Assert.That(trending).Contains("up");
    }

    [Test]
    public async Task FamilyIsDrawnAheadOfCodebaseInThePrimaryFacets()
    {
        // Family (MSSP's own FAMILY line) is promoted beside Band/Codebase/LastSeen.
        var html = await PanelAsync(new GameFilter());

        var familyMarker = $"<span class=\"facet-name\">{FacetWords.Group(Locales.SourceTag, FacetKeys.Family)}</span>";
        var codebaseMarker = $"<span class=\"facet-name\">{FacetWords.Group(Locales.SourceTag, FacetKeys.Codebase)}</span>";

        var familyAt = html.IndexOf(familyMarker, StringComparison.Ordinal);
        var codebaseAt = html.IndexOf(codebaseMarker, StringComparison.Ordinal);

        await Assert.That(familyAt).IsGreaterThanOrEqualTo(0).Because("family has no fieldset of its own");
        await Assert.That(codebaseAt).IsGreaterThanOrEqualTo(0).Because("codebase has no fieldset of its own");
        await Assert.That(familyAt).IsLessThan(codebaseAt).Because("family should draw ahead of codebase");
    }

    [Test]
    public async Task EveryChoiceFacetIsDrawnWithNoMoreFiltersDisclosureHidingAny()
    {
        // Every facet the panel can filter on is in the page a reader already has, not behind a
        // second click first — the "more filters (N)" disclosure that used to hold the rest is gone.
        var html = await PanelAsync(new GameFilter());

        await Assert.That(html).DoesNotContain("facet-more-groups");
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

        // Every facet reaches the same querystring, whichever shape its control has: the presence
        // facets are checkboxes the form submits, and the choice facets are links that carry the
        // key themselves — a control that waited for a "show" button was a filter that did nothing
        // when a reader clicked it.
        foreach (var group in (await Queries.SearchAsync(new GameFilter())).Facets)
        {
            var named = html.Contains($"name=\"{group.Key}\"", StringComparison.Ordinal)
                || html.Contains($"/games?{group.Key}=", StringComparison.Ordinal);

            await Assert.That(named).IsTrue().Because($"{group.Key} reaches no querystring");
        }
    }

    [Test]
    public async Task EveryValueOnThePanelCarriesTheCountChoosingItReturns()
    {
        var listing = await Queries.SearchAsync(new GameFilter());
        var html = Render.Words(await PanelAsync(new GameFilter()));

        // A row per value now, rather than an option per value twice over: the count is its own cell
        // beside the name, and it is also in both radios' accessible names — the one that includes
        // the value carries what including it returns, the one that excludes it carries what
        // excluding it returns. A control offering a number the listing would not produce is the
        // failure this guards against, in either direction.
        foreach (var group in listing.Facets.Where(g => g.Kind is FacetKind.Choice))
        {
            var single = FacetWords.IsSingleChoice(group.Key);

            foreach (var value in group.Values)
            {
                var name = FacetWords.Value(Locales.SourceTag, group.Key, value);

                // "only" is on the tri-state groups alone. Activity and last-seen are one ordered
                // scale — a radio group — so their rows say what choosing returns and nothing about
                // a second state they do not have.
                await Assert.That(html).Contains(single
                    ? $"{name}, {Plural(value.Count)}"
                    : $"{name}, {Plural(value.Count)}, only");

                if (!single)
                {
                    await Assert.That(html)
                        .Contains($"{name}, {Plural(group.Total - value.Count)}, excluded");
                }
            }
        }
    }

    /// <summary>
    /// A count and its noun, agreeing — because the panel says it that way.
    /// </summary>
    /// <remarks>
    /// Every accessible name on the panel read "1 games" and "0 games" until this pass. English has
    /// two forms and this is the one place the panel says a number out loud, so it says it
    /// correctly; the test spells the rule out rather than accepting whatever the panel produced.
    /// </remarks>
    private static string Plural(int count) => count == 1 ? "1 game" : $"{count} games";

    [Test]
    public async Task ExclusionIsOfferedOnlyWhereExcludingMeansSomething()
    {
        // Activity and last-seen are nested thresholds on one ordered scale: a game reached an hour
        // ago is also in the last seven days and the last thirty, and "connected now" is a narrower
        // window than "active this week" rather than a different kind of thing. "Everything but
        // games with somebody connected" is not a question anybody has — so those groups are radios
        // with no exclude affordance at all, which is one control and one tab stop per value instead
        // of two.
        var listing = await Queries.SearchAsync(new GameFilter());
        var html = await PanelAsync(new GameFilter());

        foreach (var group in listing.Facets.Where(g => g.Kind is FacetKind.Choice))
        {
            foreach (var value in group.Values)
            {
                var excluded = $"{FacetWords.Value(Locales.SourceTag, group.Key, value)}, {Plural(group.Total - value.Count)}, excluded";

                if (FacetWords.IsSingleChoice(group.Key))
                {
                    await Assert.That(html).DoesNotContain(excluded)
                        .Because($"{group.Key} is one ordered scale and cannot be excluded from");
                }
                else
                {
                    await Assert.That(html).Contains(excluded);
                }
            }
        }
    }

    [Test]
    public async Task ThePanelSaysInWordsThatAnUnknownIsNotANo()
    {
        // Said in the markup, not only in a comment — an unticked box must not read as the game
        // declining a protocol. The per-group notes moved into one disclosure at the panel's foot; a
        // <details> keeps its contents in the accessibility tree, so it's still a fair place for the
        // long form of the rule rather than a place to hide it.
        var words = Render.Words(await PanelAsync(new GameFilter()));

        await Assert.That(words).Contains("A blank is a gap in our measurement, not a no");
        await Assert.That(words).Contains("not identified");
        await Assert.That(words).Contains("Every count here comes from a game we measured");
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
        await Assert.That(Render.Words(html)).Contains("what the badges and the blanks mean");
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

        await Assert.That(FacetWords.Unknown(Locales.SourceTag, FacetKeys.Codebase)).IsNotEqualTo("no");
        await Assert.That(FacetWords.Unknown(Locales.SourceTag, FacetKeys.Genre))
            .IsNotEqualTo(FacetWords.Unknown(Locales.SourceTag, FacetKeys.Charset));
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
            await Assert.That(words).Contains(FacetWords.Evidence(Locales.SourceTag, evidence));
            await Assert.That(words).Contains(FacetWords.EvidenceMeaning(Locales.SourceTag, evidence));
        }

        // And no two of them say the same thing, or the panel has a distinction it cannot draw.
        var registers = Enum.GetValues<FacetEvidence>().Length;
        await Assert.That(Enum.GetValues<FacetEvidence>().Select(e => FacetWords.Evidence(Locales.SourceTag, e)).Distinct().Count())
            .IsEqualTo(registers);
        await Assert.That(
            Enum.GetValues<FacetEvidence>().Select(e => FacetWords.EvidenceMeaning(Locales.SourceTag, e)).Distinct().Count())
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
            var marker = $"<span class=\"facet-name\">{DrawnUnder(group.Key)}</span>";
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
            await Assert.That(label).Contains($"evidence {FacetWords.Evidence(Locales.SourceTag, group.Evidence)}")
                .Because($"{group.Key} has no evidence chip beside its own name");
        }
    }

    /// <summary>
    /// The heading a facet's rows are actually drawn under, which is its own name for all but two.
    /// </summary>
    /// <remarks>
    /// <c>uncounted</c> and <c>unreachable</c> share one legend — one question, two independent
    /// answers — but the evidence chip still sits on that shared heading, so neither count is
    /// reachable without being told what kind of statement it is.
    /// </remarks>
    private static string DrawnUnder(string key) =>
        key is FacetKeys.Uncounted or FacetKeys.Unreachable
            ? Messages.For(Locales.SourceTag, "facet.group.measure")
            : FacetWords.Group(Locales.SourceTag, key);

    [Test]
    public async Task AChosenValueIsMarkedSoTheFormShowsWhatTheUrlAsked()
    {
        // No <select> and no checkbox left on this panel: the state is the row's own class and its
        // aria-current, and the way out is the row's own link. A control that showed nothing at rest
        // until it was opened is the defect the rows replace.
        var filter = new GameFilter { Band = ActivityBand.Archived, IncludeArchived = true };
        var html = await PanelAsync(filter);

        await Assert.That(html).Contains("aria-current=\"true\"");
        await Assert.That(html).Contains("facet-row on");
        await Assert.That(html).DoesNotContain("<select");
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
        var chips = ActiveFilters.For(Locales.SourceTag, listing.Facets, query.Filter, Url);

        await Assert.That(chips.Select(c => c.Value)).Contains("not Evennia");
        await Assert.That(chips.Select(c => c.Value)).Contains("sun");

        // Removing one value of a repeatable facet leaves the other behind. Dropping the whole
        // parameter would take MSSP off the query too, and the chip said nothing about MSSP.
        var gmcp = chips.Single(c => c.Value is "GMCP");
        await Assert.That(gmcp.RemoveHref).Contains("MSSP");
        await Assert.That(gmcp.RemoveHref).DoesNotContain("GMCP");
    }

    /// <summary>Every chip is a translated word, including the four the panel does not supply.</summary>
    /// <remarks>
    /// Three chips (a typed name, two widenings) plus "included" are built by
    /// <see cref="ActiveFilters"/> rather than read off a facet group, and were still literal English
    /// after the rest of the site was localized. Asserted against the bundle rather than the English
    /// text.
    /// </remarks>
    [Test]
    public async Task EveryChipIsAWordFromTheBundleAndNotALiteral()
    {
        const string Url = $"?q=sun&{FacetKeys.Archived}=true&{FacetKeys.Adult}=true";
        var en = Locales.SourceTag;

        await Assert.That(GameFilterBinding.TryRead(Url, out var query, out _)).IsTrue();

        var listing = await Queries.SearchAsync(query.Filter);
        var chips = ActiveFilters.For(en, listing.Facets, query.Filter, Url);
        var facets = chips.Select(c => c.Facet).ToList();

        foreach (var key in (string[])[FacetKeys.Text, FacetKeys.Archived, FacetKeys.Adult])
        {
            await Assert.That(facets)
                .Contains(FacetWords.Group(en, key))
                .Because($"the {key} chip names itself from the bundle");
        }

        // "included" is the only chip value that is a state of ours rather than a token a game gave
        // us, so it is the only one that has to be translated rather than passed through.
        var included = Messages.For(en, "facet.value.included");

        await Assert.That(chips.Count(c => c.Value == included)).IsEqualTo(2);

        // And none of the four is a bare key leaking through FacetWords.Group's fallback, which
        // cannot be told from the English by comparing strings — "archived" is both the facet key
        // and the word. So the bundle is asked whether it owns the id at all: an id the bundle has
        // is an id a translator can reach, and that is the thing being asserted.
        foreach (var id in (string[])
            ["facet.group.search", "facet.group.archived", "facet.group.adult", "facet.value.included"])
        {
            await Assert.That(Messages.Ids).Contains(id);
        }
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

        await Assert.That(ActiveFilters.For(Locales.SourceTag, listing.Facets, query.Filter, string.Empty)).IsEmpty();
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
        // The order moved off this panel and into the listing's own toolbar, over the column whose
        // numbers it changes — a pressed-state switch of links rather than a <select> that armed
        // itself and waited for a "show" button. What the panel may not do is keep a second, stale
        // copy of the control.
        var html = await PanelAsync(new GameFilter { Sort = GameSort.Players });

        await Assert.That(html).DoesNotContain($"name=\"{FacetKeys.Sort}\"");
        await Assert.That(html).DoesNotContain("<select");

        // Every order is still a parameter, and still named, wherever it is drawn.
        foreach (var sort in Enum.GetValues<GameSort>())
        {
            await Assert.That(FacetWords.Sort(Locales.SourceTag, sort)).IsNotEmpty();
            await Assert.That(FacetTokens.Of(sort)).IsNotEmpty();
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
            await Assert.That(FacetWords.Sort(Locales.SourceTag, sort)).DoesNotContain("busiest");
            await Assert.That(FacetWords.Sort(Locales.SourceTag, sort)).DoesNotContain("popular");
        }
    }

    [Test]
    public async Task AValueIsOneRowWithThreeStatesRatherThanTwoOptionsInTwoLists()
    {
        // Finding B3: values used to appear twice in one select ("only" and "anything but"), showing
        // no state at rest. Two radios per row now, sharing the facet's key; no script — the
        // browser's own group behaviour clears the other rows.
        var html = await PanelAsync(new GameFilter());

        var grid = html[html.IndexOf("facet-grid", StringComparison.Ordinal)..];

        // The sort control keeps its optgroups — grouping nine orders by what each one reads is a
        // different job from listing one facet's values twice.
        await Assert.That(grid).DoesNotContain("<optgroup");
        // Links, not controls: a filter that needs a second click on a "show" button before it does
        // anything is a filter panel that does not work, which is what radios made these.
        await Assert.That(html).Contains("href=\"/games?codebase=Evennia\"");
        await Assert.That(html).Contains("href=\"/games?codebase=%21Evennia\"");

        // One tab stop per facet, as the select was. No prose under the group headers either — the
        // control's shape says what it does.
        await Assert.That(Render.Words(html)).DoesNotContain("Tick to include");
        await Assert.That(Render.Words(html)).DoesNotContain("Pick one.");
        await Assert.That(Render.Words(html)).DoesNotContain("Unticked means not measured");
    }

    [Test]
    public async Task AnExcludedValueSaysSoInTheRowAndInTheControlsName()
    {
        // "PennMUSH, 23 games, excluded" — the value, the count excluding it returns, and the state,
        // in the accessible name, because a reader arrowing through a radio group hears the control
        // and not the row around it. The strike-through and the × are the drawing of the same fact,
        // and neither of them is a colour.
        var html = await PanelAsync(new GameFilter { Codebase = FacetChoice.Not("Evennia") });

        await Assert.That(html).Contains("class=\"facet-row excluded\"");
        await Assert.That(Render.Words(html)).Contains("games, excluded\"");
        // And the applied one links to clearing itself, so a row unticks where it was ticked.
        await Assert.That(html).Contains("aria-current=\"true\"");
        await Assert.That(html).Contains("href=\"/games\"");
    }

    // ── what we could measure ────────────────────────────────────────────────────────────────

    [Test]
    public async Task TheDemoTellsAMeasuredZeroFromAnUnreadableCountFromAGameWeNeverReached()
    {
        // The fixture's three-way split: Eldertale was counted and found empty, Midnight Sun answered
        // but couldn't be counted, and Hollow Bell never answered — none of the three is "empty".
        var listing = await Queries.SearchAsync(new GameFilter());

        var uncounted = await Queries.ListAsync(
            new GameFilter { Uncounted = FacetChoice.Of(FacetTokens.Yes) });
        var unreachable = await Queries.ListAsync(
            new GameFilter { Unreachable = FacetChoice.Of(FacetTokens.Yes) });

        await Assert.That(uncounted.Select(g => g.Slug)).IsEquivalentTo(new[] { "midnight-sun" });
        await Assert.That(unreachable.Select(g => g.Slug)).IsEquivalentTo(new[] { "hollow-bell" });

        // Eldertale is the trap: a measured nought, in the same activity band as Midnight Sun.
        var eldertale = listing.Games.Single(g => g.Slug == "eldertale");

        await Assert.That(eldertale.PlayersNow).IsEqualTo(0);
        await Assert.That(uncounted.Any(g => g.Slug == "eldertale")).IsFalse();

        await Assert.That(uncounted.Any(g => g.Slug == "hollow-bell")).IsFalse();
    }

    [Test]
    public async Task TheGroupDrawsBothSwitchesUnderOneHeadingWithTheirRealCounts()
    {
        var html = Render.Words(await PanelAsync(new GameFilter()));
        var listing = await Queries.SearchAsync(new GameFilter());

        await Assert.That(html).Contains(Messages.For(Locales.SourceTag, "facet.group.measure"));

        foreach (var key in new[] { FacetKeys.Uncounted, FacetKeys.Unreachable })
        {
            var group = listing.Facets.Single(f => f.Key == key);
            var value = group.Values.Single();

            await Assert.That(value.Count).IsGreaterThan(0);
            await Assert.That(html)
                .Contains(FacetWords.Value(Locales.SourceTag, key, value))
                .Because($"{key} has no row on the panel");

            // The count beside it is the one the listing keeps, which is the panel's whole promise.
            await Assert.That((await Queries.ListAsync(Choose(key))).Count).IsEqualTo(value.Count);
        }
    }

    [Test]
    public async Task HidingThemIsSaidToBeOurFilterAndNotAClaimAboutTheGames()
    {
        // Rule 5 on the one control most likely to be read the other way. A reader who unticks these
        // has changed their listing, and the panel has to say that in words rather than leave a
        // shorter listing to be read as a shorter hobby.
        var html = Render.Words(await PanelAsync(new GameFilter()));

        await Assert.That(html).Contains(Messages.For(Locales.SourceTag, "facet.measure.note"));

        // And the same sentence survives on the surface that has no styling to carry a tone. Read
        // with the whitespace collapsed, because the plain renderer wraps at 80 columns and the
        // sentence is longer than that — a literal comparison here would be asserting where the line
        // breaks fall rather than that the words are present.
        var listing = await Queries.SearchAsync(new GameFilter());
        var text = PlainText.RenderListing(listing, new GameFilter(), FixtureGameQueries.Now);

        await Assert.That(Flowed(text))
            .Contains(Flowed(Messages.For(Locales.SourceTag, "facet.measure.note")));
    }

    /// <summary>One line of text, whatever column the renderer broke it at.</summary>
    private static string Flowed(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [Test]
    public async Task ThePlainSurfaceDrawsIncludedAndExcludedDifferently()
    {
        // "Only these" and "anything but these" are opposite filters, and one star for both drew
        // them identically on the surface where a reader has nothing else to go on. It matters most
        // here, because excluding is what these two switches are mostly for.
        var included = new GameFilter { Uncounted = FacetChoice.Of(FacetTokens.Yes) };
        var excluded = new GameFilter { Uncounted = FacetChoice.Not(FacetTokens.Yes) };

        var onText = PlainText.RenderListing(
            await Queries.SearchAsync(included), included, FixtureGameQueries.Now);
        var offText = PlainText.RenderListing(
            await Queries.SearchAsync(excluded), excluded, FixtureGameQueries.Now);

        await Assert.That(onText).Contains($"* {FacetTokens.Yes}");
        await Assert.That(offText).Contains($"- {FacetTokens.Yes}");
        await Assert.That(offText).DoesNotContain($"* {FacetTokens.Yes}");

        // And the column has a key, because a mark nobody explains is a mark nobody reads.
        await Assert.That(Flowed(offText))
            .Contains(Flowed(Messages.For(Locales.SourceTag, "facet.plain.marks")));
    }

    [Test]
    [Arguments(FacetKeys.Uncounted)]
    [Arguments(FacetKeys.Unreachable)]
    public async Task EachSwitchRoundTripsThroughTheQuerystringInBothPolarities(string key)
    {
        // Three states and a URL for each, read back by the one parser /games and /api/games share.
        foreach (var token in new[] { FacetTokens.Yes, FacetChoice.ExcludeToken + FacetTokens.Yes })
        {
            var read = GameFilterBinding.TryRead($"?{key}={token}", out var query, out var error);

            await Assert.That(read).IsTrue().Because($"?{key}={token}: {error}");

            var choice = key == FacetKeys.Uncounted ? query.Filter.Uncounted : query.Filter.Unreachable;

            await Assert.That(choice).IsNotNull();
            await Assert.That(choice!.Token).IsEqualTo(token);

            // The panel's own link for that state is in the markup, so the round trip is a click and
            // not only a hand-typed URL.
            var html = await PanelAsync(query.Filter);
            await Assert.That(html).Contains("aria-current=\"true\"");
        }

        // And absent is the third state: not asked, and so not narrowed.
        GameFilterBinding.TryRead(string.Empty, out var bare, out _);
        await Assert.That(key == FacetKeys.Uncounted ? bare.Filter.Uncounted : bare.Filter.Unreachable)
            .IsNull();
    }

    [Test]
    [Arguments(FacetKeys.Uncounted)]
    [Arguments(FacetKeys.Unreachable)]
    public async Task AValueTheseSwitchesDoNotHaveIsRefusedRatherThanQuietlyEmptying(string key)
    {
        // Their vocabulary is one word, so anything else is a typo — and a typo that narrowed the
        // listing to nothing would hand a reader an empty page to read as "no game is like that",
        // which is our own parser published as a measurement of the hobby.
        var read = GameFilterBinding.TryRead($"?{key}=maybe", out _, out var error);

        await Assert.That(read).IsFalse();
        await Assert.That(error).IsNotNull();
        await Assert.That(error!).Contains(FacetTokens.Yes);
    }

    [Test]
    public async Task ExcludingBothLeavesAListingThatStillOffersTheWayBack()
    {
        // The listing shrinks and the two controls that shrank it stay drawn, at the count that
        // undoing them returns. A selection whose only affordance has gone is the defect the rows
        // replaced.
        var filter = new GameFilter
        {
            Uncounted = FacetChoice.Not(FacetTokens.Yes),
            Unreachable = FacetChoice.Not(FacetTokens.Yes),
        };

        var listing = await Queries.SearchAsync(filter);
        var html = await PanelAsync(filter);

        await Assert.That(listing.Games.Any(g => g.Slug == "midnight-sun")).IsFalse();
        await Assert.That(listing.Games.Any(g => g.Slug == "hollow-bell")).IsFalse();

        // Eldertale is a measured nought and survives both exclusions, which is the point of them
        // being two facets rather than one word for "no number".
        await Assert.That(listing.Games.Any(g => g.Slug == "eldertale")).IsTrue();

        foreach (var key in new[] { FacetKeys.Uncounted, FacetKeys.Unreachable })
        {
            await Assert.That(listing.Facets.Single(f => f.Key == key).Values.Single().State)
                .IsEqualTo(FacetState.Excluded);
        }

        await Assert.That(Render.Words(html))
            .Contains(Messages.For(Locales.SourceTag, "facet.excluded.uncounted"));
    }

    private static GameFilter Choose(string key) => key == FacetKeys.Uncounted
        ? new GameFilter { Uncounted = FacetChoice.Of(FacetTokens.Yes) }
        : new GameFilter { Unreachable = FacetChoice.Of(FacetTokens.Yes) };

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
    /// A filter as one comparable string, so two of them can be compared for having read anything.
    /// </summary>
    /// <remarks>
    /// Record equality would compare <see cref="GameFilter.MeasuredProtocols"/> by reference, making
    /// every parsed filter look different regardless of content. Read off the type via reflection
    /// rather than a hand-maintained member list — a list maintained by hand can't be the check on a
    /// list maintained by hand; a previous hand-written version missed a facet added elsewhere.
    /// </remarks>
    private static string Describe(GameFilter f) => string.Join(
        '|',
        typeof(GameFilter)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => p.GetValue(f))
            .Select(v => v is IEnumerable<string> many ? string.Join(',', many) : v?.ToString()));
}
