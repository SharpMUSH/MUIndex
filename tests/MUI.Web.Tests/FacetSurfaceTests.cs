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
        FacetKeys.Tls => "true",
        FacetKeys.Band => "quiet",
        FacetKeys.LastSeen => "week",
        FacetKeys.Protocol => "GMCP",
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
        var words = Render.Words(await PanelAsync(new GameFilter()));

        await Assert.That(words).Contains("leaving one unticked never means a game lacks it");
        await Assert.That(words).Contains("none of those is a no");
        await Assert.That(words).Contains("computed from the same query as the list below");
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
        var words = Render.Words(await PanelAsync(new GameFilter()));

        await Assert.That(words).Contains("we measured this");
        await Assert.That(words).Contains("the game says so");
        await Assert.That(FacetWords.Evidence(FacetEvidence.Measured))
            .IsNotEqualTo(FacetWords.Evidence(FacetEvidence.Declared));
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
        await Assert.That(Render.Words(text)).Contains("is never a \"no\"");
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
        f.Tls,
        f.Band,
        f.LastSeen,
        f.Charset?.Token,
        f.Codebase?.Token,
        f.Family?.Token,
        f.Genre?.Token,
        f.Language?.Token,
        string.Join(',', f.MeasuredProtocols));
}
