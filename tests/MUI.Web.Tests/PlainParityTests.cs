using MUI.Catalog;
using MUI.Web.Components;
using MUI.Web.Components.Pages;
using MUI.Web.Fixtures;
using MUI.Web.Localization;

namespace MUI.Web.Tests;

/// <summary>
/// Every component's plain rendering, asserted in words.
/// </summary>
/// <remarks>
/// If a state can only be told apart by a colour, a glyph or a cell shape, it fails here — and
/// failing here means the graphic version was decoration rather than information. These are
/// deliberately assertions about sentences and not about markup.
/// </remarks>
public class PlainParityTests
{
    private static readonly DateTimeOffset Now = FixtureGameQueries.Now;
    private static readonly FixtureGameQueries Queries = new();

    /// <summary>The archive as its page assembles it, so a test reads what a reader would.</summary>
    private static async Task<IReadOnlyList<ArchiveEntry>> ArchiveAsync()
    {
        var entries = new List<ArchiveEntry>();

        foreach (var game in await Queries.ListAsync(new GameFilter { Band = ActivityBand.Archived }))
        {
            entries.Add(ArchiveEntry.For(game, await Queries.ForGameAsync(game.Id), Now));
        }

        return entries;
    }

    private static async Task<string> GameAsync(string slug)
    {
        var page = await Queries.FindAsync(slug);
        var reach = ReachSeries.Build(await Queries.ForGameAsync(page!.Summary.Id), Now);
        var range = TrendRange.Default(DateOnly.FromDateTime(Now.UtcDateTime));
        var trend = await new FixturePresenceTrends()
            .ForGameAsync(page.Summary.Id, range.From, range.To);

        return PlainText.Render(page, Now, reach, trend);
    }

    [Test]
    public async Task TheHeatmapSurvivesAsASentenceAndALinePerDay()
    {
        var text = await GameAsync("m-u-s-h");

        await Assert.That(text).Contains("When people are on");
        await Assert.That(text).Contains("Busiest");
        await Assert.That(text).Contains("Mon —");
        await Assert.That(text).Contains("Wed —");
    }

    /// <summary>
    /// The mirror answers in the language the reader asked in, day names included.
    /// </summary>
    /// <remarks>
    /// Over HTTP rather than through the renderer, because the thing being checked is that the
    /// locale reaches this surface at all: the plain page is composed by a different code path from
    /// the rendered one, and the heatmap's words were the last on the site to be answered in English
    /// whatever the address said. A German reader's per-day lines start "Mo", not "Mon".
    /// </remarks>
    [Test]
    public async Task ThePlainMirrorIsAnsweredInTheLanguageTheAddressAsksFor()
    {
        await using var site = await SiteHost.StartAsync();

        var english = Render.Words(await site.Client.GetStringAsync("/g/m-u-s-h?plain=1"));
        var german = Render.Words(await site.Client.GetStringAsync("/de/g/m-u-s-h?plain=1"));

        await Assert.That(english).Contains("Mon —");
        await Assert.That(german).Contains("Mo —");
        await Assert.That(german).DoesNotContain("Mon —");

        // The same three states, and still three. The words for two of them are the glossary's, so
        // in German they are German — and they are still two different words.
        await Assert.That(german).Contains(Messages.For("de", "state.notMeasured"));
        await Assert.That(german).Contains(Messages.For("de", "state.uncounted"));
    }

    [Test]
    public async Task TheTrendSurvivesAsASentenceALinePerWeekAndAWayToSeek()
    {
        // The graphic is an SVG, so this is where it is decided whether the chart carries
        // information or decoration. All three parts have to survive: the direction, the numbers,
        // and the ability to move the window — a text browser that could see the range but not
        // change it would have the navigation and none of its function.
        var text = await GameAsync("m-u-s-h");

        // The heading through the bundle rather than shouted in English. It used to be upper-cased
        // here with ToUpperInvariant, which is an English typographic habit applied to a string a
        // translator writes — and one that mangles ß and Turkish i on the way past.
        await Assert.That(text).Contains(Messages.For(Locales.SourceTag, "trend.plain.heading"));
        await Assert.That(text).Contains("typically");
        await Assert.That(text).Contains("peak");
        await Assert.That(text)
            .Contains($"{Messages.For(Locales.SourceTag, "trend.plain.earlier")}: ?from=");
    }

    [Test]
    public async Task TheTrendSaysWhichKindOfNothingAQuietWeekWas()
    {
        // The same three states as the heatmap, at a coarser grain: a week nobody measured and a
        // week probed without a readable count are different facts about a game, and neither is an
        // empty one. The fixture is built to contain both.
        // Render.Words, because these phrases straddle the eighty-column wrap and a raw Contains
        // would be asserting where the line happened to break rather than what the page says.
        var text = Render.Words(await GameAsync("m-u-s-h"));

        await Assert.That(text).Contains("not measured");
        await Assert.That(text).Contains("probed without a count");

        // And no cause is named for either: a failed probe writes no presence row, so silence
        // cannot tell an outage of theirs from a gap of ours.
        var trend = text[text.IndexOf(
            Messages.For(Locales.SourceTag, "trend.plain.heading"), StringComparison.Ordinal)..];
        var reachable = trend.IndexOf(
            Messages.For(Locales.SourceTag, "reach.plain.heading") + " (", StringComparison.Ordinal);

        await Assert.That(reachable > 0 ? trend[..reachable] : trend)
            .DoesNotContain(Messages.For(Locales.SourceTag, "state.unreachable"));
    }

    [Test]
    public async Task TheThreeHourStatesAreThreeDifferentSentencesInPlainText()
    {
        var text = Render.Words(await GameAsync("m-u-s-h"));

        await Assert.That(text).Contains("have no measurement yet");
        await Assert.That(text).Contains("answered but produced no count");
        await Assert.That(text).Contains("including a measured zero");
    }

    [Test]
    public async Task TheAvailabilityStripSurvivesAsAPercentageAndNamedSpells()
    {
        var text = await GameAsync("m-u-s-h");

        await Assert.That(text).Contains("Reachable (last 90 days)");
        await Assert.That(text).Contains("days unreachable");
        await Assert.That(text).Contains("connection refused");
    }

    [Test]
    public async Task PlainTextNeverSaysUptime()
    {
        var text = await GameAsync("m-u-s-h");

        await Assert.That(text.ToLowerInvariant()).DoesNotContain("uptime");
    }

    [Test]
    public async Task TheCapabilityMatrixSurvivesWithBothColumnsAndTheCount()
    {
        var text = await GameAsync("m-u-s-h");

        await Assert.That(text).Contains("Capabilities (1 of 6 disagree)");
        await Assert.That(text).Contains("measured: NO      declared: yes  ** disagree");
    }

    [Test]
    public async Task AnOwnerSuppressedScreenIsStatedWithoutEditorial()
    {
        var text = await GameAsync("ashen-court");

        await Assert.That(text).Contains("The owner asked us not to republish");
        await Assert.That(text).DoesNotContain("refused to");
    }

    [Test]
    public async Task AScreenTooSmallToFrameSaysHowSmallItWas()
    {
        var text = await GameAsync("midnight-sun");

        // Through the bundle, which is also how it stopped saying "row(s)": a parenthesised s is a
        // fact about English written where a plural rule belongs, and the graphical frame and this
        // surface now render the one ICU message rather than two spellings of it.
        await Assert.That(text).Contains(Messages.For(
            Locales.SourceTag,
            "ansi.tooSmall",
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["count"] = 2 }));
    }

    [Test]
    public async Task ALongScreenSaysHowLongItIsAndPrintsAllOfIt()
    {
        // No crop on either surface any more, so no note about one. The graphical page scrolls the
        // whole screen in one frame rather than showing the first twenty-four rows and offering the
        // rest twice more; this surface prints every row, as it always did.
        var text = await GameAsync("batmud");

        await Assert.That(text).Contains("connect screen: 214 lines");
        await Assert.That(text).DoesNotContain("Unusually long");
    }

    [Test]
    public async Task TheConnectScreenAppearsAsTextWithNoEscapeCodes()
    {
        var text = await GameAsync("m-u-s-h");

        await Assert.That(text).Contains("Running PennMUSH 1.8.7");
        await Assert.That(text).DoesNotContain("\u001b");
        await Assert.That(text).DoesNotContain("[36m");
    }

    [Test]
    public async Task TheThreeFeedsAreThreeNamedSectionsInWords()
    {
        // The register the cards carry is a tone, and a tone is not a fact. In text the words do
        // all of the work.
        var text = PlainText.RenderFeeds(await Queries.FeedsAsync(), Now);

        await Assert.That(text).Contains("NEWLY DISCOVERED");
        await Assert.That(text).Contains("WENT DARK");
        await Assert.That(text).Contains("CAME BACK");
        await Assert.That(text).Contains("Aardwolf MUD");
    }

    [Test]
    public async Task TheArchiveSaysWhenEachGameWasLastReachableAndHowLongItWasKnownLive()
    {
        var games = await Queries.ListAsync(new GameFilter { Band = ActivityBand.Archived, IncludeArchived = true });
        var entries = new List<ArchiveEntry>();
        foreach (var game in games)
        {
            entries.Add(ArchiveEntry.For(game, await Queries.ForGameAsync(game.Id), Now));
        }

        var text = PlainText.RenderArchive(entries, null, Now);

        await Assert.That(text).Contains("Last reachable:");
        await Assert.That(text).Contains("Known live:");
        await Assert.That(text).Contains("Gaslight Row");
    }

    [Test]
    public async Task TheArchiveUsesTheWordArchivedAndNeverDead()
    {
        // A library catalogue entry for a periodical that ceased publication, not an obituary.
        var games = await Queries.ListAsync(new GameFilter { Band = ActivityBand.Archived, IncludeArchived = true });
        var entries = new List<ArchiveEntry>();
        foreach (var game in games)
        {
            entries.Add(ArchiveEntry.For(game, await Queries.ForGameAsync(game.Id), Now));
        }

        var text = PlainText.RenderArchive(entries, null, Now).ToLowerInvariant();

        await Assert.That(text).Contains("[archived]");
        await Assert.That(text).DoesNotContain("dead");
        await Assert.That(text).DoesNotContain("defunct");
    }

    [Test]
    public async Task NoPlainLineIsWiderThanEightyColumns()
    {
        // Text browsers are eighty wide, and a table that overflows is a table nobody can read.
        var surfaces = new[]
        {
            await GameAsync("m-u-s-h"),
            await GameAsync("midnight-sun"),
            PlainText.RenderFeeds(await Queries.FeedsAsync(), Now),
            PlainText.RenderListing(await Queries.SearchAsync(new GameFilter()), new GameFilter(), Now),

            // The archive too, because its lines are the ones a label lengthens most: an archived
            // game's codebase carries the oldest age on the site.
            PlainText.RenderArchive(await ArchiveAsync(), query: null, Now),
        };

        foreach (var line in surfaces.SelectMany(s => s.Split('\n')))
        {
            await Assert.That(line.TrimEnd().Length).IsLessThanOrEqualTo(PlainText.Columns);
        }
    }

    [Test]
    public async Task TheListingSaysCodebaseNotIdentifiedRatherThanOmittingTheLine()
    {
        // Aardwolf's codebase is genuinely unidentified. A missing line reads as an oversight; the
        // words read as the measurement it is.
        var text = PlainText.RenderListing(await Queries.SearchAsync(new GameFilter()), new GameFilter(), Now);

        await Assert.That(text).Contains("Codebase:    not identified");
    }

    [Test]
    public async Task TheRenderedRowNamesTheFieldBecauseNothingElseOnItDoes()
    {
        // The two surfaces word this one state differently and that is deliberate. Plain text and
        // the facet panel both print "codebase" as a heading and say "not identified" under it; a
        // listing row has no heading, so its label has to name the field itself or read as an
        // absence of nothing in particular.
        var html = await Render.PageAsync<Games>([]);

        await Assert.That(Render.Words(html)).Contains("Unknown Codebase");

        // And the sentence saying whose limit this is survives on the element, because the short
        // label does not carry it: an unidentified codebase is a fact about our parsers' reach and
        // not about the game (rule 5).
        await Assert.That(html).Contains("we could not identify the codebase this game runs");
    }

    [Test]
    public async Task TheListingSaysHowEachCountAndCodebaseWasObtainedAndHowOldItIs()
    {
        // §9's test of the whole system, applied to the listing: if provenance cannot survive in
        // plain text then the chip on the rendered row is decoration. Same two words the game page
        // uses — measured or declared — and the same relative age, so there is one vocabulary.
        var text = PlainText.RenderListing(
            await Queries.SearchAsync(new GameFilter { IncludeArchived = true }),
            new GameFilter { IncludeArchived = true },
            Now);

        // M*U*S*H: fifteen on, read out of a WHO four minutes ago.
        await Assert.That(text).Contains("Players now: 15   (measured, 4m)");

        // Aardwolf publishes its number on the connect screen and nowhere a machine can ask, so we
        // read it off the screen — ours to have measured, whoever did the counting.
        await Assert.That(text).Contains("Players now: 219   (measured, 40m)");

        // Ashen Court reports its count in MSSP, which is the game telling us rather than us
        // reading. Both words have to appear over counts here, or the plain surface proves nothing.
        await Assert.That(text).Contains("Players now: 9   (declared, 9m)");

        // And a value nobody has confirmed in years says so in the word, not in an amber colour.
        await Assert.That(text).Contains("PennMUSH 1.8.5  (declared, 3y, stale)");
    }

    [Test]
    public async Task TheRenderedListingRowCarriesTheSameLabelThePlainOneSpells()
    {
        // The rendered row and the plain row have to be two renderings of one fact. The fields on
        // the meta line keep the chip the game page uses — glyph, relative age, amber when it has
        // aged out — rather than inventing a second way of saying "nobody has confirmed this since
        // 2023".
        var html = await Render.PageAsync<Games>([]);

        await Assert.That(html).Contains("class=\"chip declared");

        // The count is the exception, and finding S1 took it all the way: no glyph and no word
        // beside a number on a listing row at all. Where a number came from is what a reader weighs
        // when choosing between two games, not while scanning five hundred, so it lives on the game
        // page and on the facet groups' own badges — which is where it tells them whether a filter
        // is trustworthy *before* they apply it. Rule 1 is intact one surface along.
        await Assert.That(html).DoesNotContain("class=\"declared-note\"");
        await Assert.That(html).DoesNotContain("class=\"pip");

        // And the chip that stays — the codebase — prints no age, because the row has a freshness
        // column of its own and every row used to state one age twice, four characters apart.
        var meta = html[html.IndexOf("class=\"meta", StringComparison.Ordinal)..];
        await Assert.That(meta[..meta.IndexOf("</p>", StringComparison.Ordinal)])
            .DoesNotContain("<time");
    }

    [Test]
    public async Task TheCountColumnCarriesANumberAndNothingElse()
    {
        // Finding S1, and the localization win with it. The cell used to read "●545 on" — a
        // provenance glyph whose legend lived elsewhere on the page, then the number, then one
        // English word repeated on all 515 rows. The column is named once, above the rows, and the
        // cells hold the bare figure.
        var html = await Render.PageAsync<Games>([]);
        var counts = html.Split("class=\"row-count").Skip(1).ToList();

        await Assert.That(counts).IsNotEmpty();

        foreach (var cell in counts)
        {
            var text = Render.Words(cell[..cell.IndexOf("</p>", StringComparison.Ordinal)]);

            // Either a bare number, or the words that say there is no number. Never a zero standing
            // in for an unknown, and never a unit.
            await Assert.That(text).DoesNotContain(" on");
            await Assert.That(text).DoesNotContain("●");
            await Assert.That(text).DoesNotContain("◇");
        }

        // The column head is where the noun went, once.
        await Assert.That(Render.Words(html)).Contains("connected · reached");
    }

    [Test]
    public async Task OneCountWearsOneGlyphAndNotTwo()
    {
        // The agreement above was reached by printing the glyph twice: the pip at the head of the
        // figure and the chip's own, four characters apart, describing one number. A reader who
        // counts glyphs to count facts was being told there were two, and on a declared count the
        // row read "◇ 10 on ◇ 73m". The pip stays, because it is where the eye lands when scanning
        // a column of numbers; the chip keeps the colour, the title and the word a screen reader
        // hears, and drops the duplicate.
        var html = await Render.PageAsync<Games>([]);
        var figures = html.Split("class=\"row-figure").Skip(1);

        foreach (var figure in figures)
        {
            var row = figure[..figure.IndexOf("</p>", StringComparison.Ordinal)];

            await Assert.That(Glyphs(row))
                .IsLessThanOrEqualTo(1)
                .Because($"one count, one glyph: {row}");
        }

        // And the chip is still there carrying the rest of the label, so what went is the duplicate
        // rather than the provenance.
        await Assert.That(html).Contains("class=\"chip declared");
        await Assert.That(Render.Words(html)).Contains("declared");

        static int Glyphs(string markup) =>
            markup.Split("&#x25C7;").Length - 1 + markup.Split("&#x25CF;").Length - 1;
    }

    [Test]
    public async Task TheGamePagesLiveCountWearsTheSameChipTheListingRowDoes()
    {
        // The rendered game page printed the measured glyph over every count it had, whoever
        // produced the number — so a reader arriving from a listing row that said "declared" met a
        // green dot saying otherwise. One vocabulary, and the same one.
        var declared = await Render.PageAsync<Game>(new() { ["Slug"] = "ashen-court" });
        var measured = await Render.PageAsync<Game>(new() { ["Slug"] = "m-u-s-h" });

        // The chip became the figure's own kicker: the count over the word and the age that
        // produced it, which is one fact read as one block rather than a number with a label
        // trailing it at metadata size.
        await Assert.That(Render.Words(declared)).Contains("declared");
        await Assert.That(declared).Contains("figure-count mono declared");
        await Assert.That(Render.Words(measured)).Contains("measured");
        await Assert.That(measured).Contains("figure-count mono");
        await Assert.That(measured).DoesNotContain("figure-count mono declared");

        // A count nobody could take says so in words, and never as a zero.
        var none = await Render.PageAsync<Game>(new() { ["Slug"] = "midnight-sun" });
        await Assert.That(Render.Words(none)).Contains("not counted");
    }

    [Test]
    public async Task TheArchiveLabelsACodebaseExactlyAsTheListingDoes()
    {
        // Gaslight Row read "PennMUSH 1.8.5 (declared, 3y, stale)" on /games and a bare
        // "Codebase: PennMUSH 1.8.5" on /archive — the same value, one surface saying nobody has
        // confirmed it since 2023 and the other not. The archive is where a value is oldest and
        // where the label matters most.
        var text = PlainText.RenderArchive(await ArchiveAsync(), query: null, Now);

        await Assert.That(text).Contains("PennMUSH 1.8.5  (declared, 3y, stale)");

        // And the rendered page wears the same chip the listing row does.
        var html = await Render.PageAsync<Archive>([]);
        await Assert.That(html).Contains("class=\"chip declared");
    }

    [Test]
    public async Task TheHomePageCountsOnlyWhatWasMeasured()
    {
        var counts = SiteCounts.From(await Queries.ListAsync(new GameFilter { IncludeArchived = true }));
        var text = PlainText.RenderHome(counts, await Queries.FeedsAsync(), CrawlerPulse.Unknown, Now);

        await Assert.That(text).Contains("games known");
        await Assert.That(text).Contains("connected now (measured)");
        await Assert.That(text).Contains("answering, uncounted");
    }

    [Test]
    public async Task NoSurfaceOffersAVoteStarRatingOrRecommendation()
    {
        // Rankings are computed from measured data. This is not a feature gap; it is the thing that
        // killed the incumbents.
        var counts = SiteCounts.From(await Queries.ListAsync(new GameFilter { IncludeArchived = true }));
        var surfaces = new[]
        {
            await GameAsync("m-u-s-h"),
            PlainText.RenderHome(counts, await Queries.FeedsAsync(), CrawlerPulse.Unknown, Now),
            PlainText.RenderListing(await Queries.SearchAsync(new GameFilter()), new GameFilter(), Now),
        };

        foreach (var word in new[] { "vote", "rating", "star", "recommend", "upvote" })
        {
            foreach (var surface in surfaces)
            {
                await Assert.That(surface.ToLowerInvariant()).DoesNotContain(word);
            }
        }
    }
}
