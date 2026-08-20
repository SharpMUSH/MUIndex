using MUI.Catalog;
using MUI.Web.Components;
using MUI.Web.Components.Pages;
using MUI.Web.Fixtures;
using MUI.Web.Localization;

namespace MUI.Web.Tests;

/// <summary>
/// Every component's plain rendering, asserted in words.
/// </summary>
/// <remarks>If a state can only be told apart by a colour, glyph or cell shape, it fails here — meaning the graphic version was decoration rather than information.</remarks>
public class PlainParityTests
{
    /// <summary>
    /// The longest unbroken spell is first, on the page as well as in its text mirror.
    /// </summary>
    /// <remarks>
    /// Previously ranked by <c>Since.Ticks</c> (place = greatest value), but an earlier date is a
    /// *smaller* tick count, so the longest spell printed last under a heading that says longest.
    /// </remarks>
    [Test]
    public async Task TheLongestSpellIsFirstOnThePageAndInTheMirror()
    {
        var rankings = await Queries.RankingsAsync(RankingSpan.Week);
        var spells = rankings.LongestUnbroken;

        await Assert.That(spells.Count).IsGreaterThan(1);

        var longest = spells.MaxBy(s => s.LengthAt(Now))!;

        // Asserted against the list, not by searching rendered text: the game's name also appears in
        // the busiest table higher up the same page.
        await Assert.That(spells[0].Slug).IsEqualTo(longest.Slug);

        var text = PlainText.RenderRankings(rankings, Now, Locales.SourceTag);
        var section = text[text.LastIndexOf(spells[0].Name, StringComparison.Ordinal)..];

        await Assert.That(section).StartsWith(spells[0].Name);

        // Ranked by start date (the measurement), not duration (which reads against a moving clock).
        var places = spells.Select(s => s.Since).ToList();

        await Assert.That(PlaceEarliest(places, longest.Since)).IsEqualTo(1);

        foreach (var spell in spells.Where(s => s.Since > longest.Since))
        {
            await Assert.That(PlaceEarliest(places, spell.Since)).IsGreaterThan(1);
        }

        foreach (var spell in spells)
        {
            await Assert.That(PlaceEarliest(places, spell.Since)).IsLessThanOrEqualTo(spells.Count);
        }
    }

    /// <summary>The page's own rule for a place where the earliest value wins.</summary>
    private static int PlaceEarliest<T>(IEnumerable<T> values, T value) where T : IComparable<T> =>
        1 + values.Count(v => v.CompareTo(value) < 0);

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
    /// <remarks>Over HTTP rather than through the renderer, since the plain page is a different code path and the locale must reach it independently.</remarks>
    [Test]
    public async Task ThePlainMirrorIsAnsweredInTheLanguageTheAddressAsksFor()
    {
        await using var site = await SiteHost.StartAsync();

        var english = Render.Words(await site.Client.GetStringAsync("/g/m-u-s-h?plain=1"));
        var german = Render.Words(await site.Client.GetStringAsync("/de/g/m-u-s-h?plain=1"));

        await Assert.That(english).Contains("Mon —");
        await Assert.That(german).Contains("Mo —");
        await Assert.That(german).DoesNotContain("Mon —");

        await Assert.That(german).Contains(Messages.For("de", "state.notMeasured"));
        await Assert.That(german).Contains(Messages.For("de", "state.uncounted"));
    }

    [Test]
    public async Task TheTrendSurvivesAsASentenceALinePerWeekAndAWayToSeek()
    {
        var text = await GameAsync("m-u-s-h");

        await Assert.That(text).Contains(Messages.For(Locales.SourceTag, "trend.plain.heading"));
        await Assert.That(text).Contains("typically");
        await Assert.That(text).Contains("peak");
        await Assert.That(text)
            .Contains($"{Messages.For(Locales.SourceTag, "trend.plain.earlier")}: ?from=");
    }

    [Test]
    public async Task TheTrendSaysWhichKindOfNothingAQuietWeekWas()
    {
        // Render.Words because these phrases straddle the eighty-column wrap.
        var text = Render.Words(await GameAsync("m-u-s-h"));

        await Assert.That(text).Contains("not measured");
        await Assert.That(text).Contains("probed without a count");

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

        await Assert.That(text).Contains("Capabilities (1 of 6 disagrees)");
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

        await Assert.That(text).Contains(Messages.For(
            Locales.SourceTag,
            "ansi.tooSmall",
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["count"] = 2 }));
    }

    [Test]
    public async Task ALongScreenSaysHowLongItIsAndPrintsAllOfIt()
    {
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
        var text = PlainText.RenderFeeds(Locales.SourceTag, await Queries.FeedsAsync(), Now);

        await Assert.That(text).Contains("NEWLY DISCOVERED");
        await Assert.That(text).Contains("WENT DARK");
        await Assert.That(text).Contains("CAME BACK");
        await Assert.That(text).Contains("Aardwolf MUD");
    }

    /// <summary>
    /// The three feeds are three sections in the reader's language, not three English headings.
    /// </summary>
    /// <remarks>
    /// Headings were previously literals in the renderer, with <c>tag</c> reaching only the ages, so
    /// every locale printed the same English. Checked in two languages: the pseudolocale proves a
    /// string went through <c>Messages</c>, German proves the tag reaches a real satellite.
    /// </remarks>
    [Test]
    public async Task TheFeedHeadingsAndEmptyStatesComeFromTheBundleAndNotFromTheRenderer()
    {
        var feeds = await Queries.FeedsAsync();
        var empty = new LivenessFeeds([], [], []);

        var pseudo = PlainText.RenderFeeds("qps-ploc", empty, Now);

        await Assert.That(pseudo).DoesNotContain("NEWLY DISCOVERED");
        await Assert.That(pseudo).Contains(
            Messages.For("qps-ploc", "feed.plain.newlyDiscovered").ToUpperInvariant());
        await Assert.That(pseudo).Contains(Messages.For("qps-ploc", "feed.nothingNew"));

        var german = PlainText.RenderFeeds("de", empty, Now);

        await Assert.That(german).Contains("Nichts Neues.");
        await Assert.That(german).Contains("Nichts ist verstummt.");
        await Assert.That(german).Contains("Nichts ist zurückgekehrt. Wir klopfen weiter.");
        await Assert.That(german).DoesNotContain("Nothing new.");

        foreach (var id in new[] { "feed.plain.newlyDiscovered", "feed.plain.wentDark", "feed.plain.cameBack" })
        {
            await Assert.That(PlainText.RenderFeeds("de", feeds, Now))
                .Contains(Messages.For("de", id).ToUpperInvariant());
        }
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

        var text = PlainText.RenderArchive(entries, null, Now, Locales.SourceTag);

        await Assert.That(text).Contains(Messages.For(Locales.SourceTag, "archive.plain.lastReachable"));
        await Assert.That(text).Contains(Messages.For(Locales.SourceTag, "archive.plain.knownLive"));
        await Assert.That(text).Contains("Gaslight Row");
    }

    [Test]
    public async Task TheArchiveUsesTheWordArchivedAndNeverDead()
    {
        var games = await Queries.ListAsync(new GameFilter { Band = ActivityBand.Archived, IncludeArchived = true });
        var entries = new List<ArchiveEntry>();
        foreach (var game in games)
        {
            entries.Add(ArchiveEntry.For(game, await Queries.ForGameAsync(game.Id), Now));
        }

        var text = PlainText.RenderArchive(entries, null, Now, Locales.SourceTag).ToLowerInvariant();

        await Assert.That(text).Contains("[archived]");
        await Assert.That(text).DoesNotContain("dead");
        await Assert.That(text).DoesNotContain("defunct");
    }

    [Test]
    public async Task NoPlainLineIsWiderThanEightyColumns()
    {
        var surfaces = new[]
        {
            await GameAsync("m-u-s-h"),
            await GameAsync("midnight-sun"),
            PlainText.RenderFeeds(Locales.SourceTag, await Queries.FeedsAsync(), Now),
            PlainText.RenderListing(await Queries.SearchAsync(new GameFilter()), new GameFilter(), Now),
            PlainText.RenderArchive(await ArchiveAsync(), query: null, Now, Locales.SourceTag),
        };

        foreach (var line in surfaces.SelectMany(s => s.Split('\n')))
        {
            await Assert.That(line.TrimEnd().Length).IsLessThanOrEqualTo(PlainText.Columns);
        }

        // Find's addresses genuinely can't hold to eighty columns (a wrapped address isn't
        // clickable), so only its prose is checked. German and loaded, so the longest option labels
        // and addresses are actually present.
        const string Loaded = "?plain=1&genre=Fantasy&language=English&lineage=MUSH&archived=true";

        string[] find =
        [
            PlainText.RenderFind(await FindScreen.BuildAsync(Queries, "?plain=1")),
            PlainText.RenderFind(await FindScreen.BuildAsync(Queries, "?plain=1", "de"), "de"),
            PlainText.RenderFind(await FindScreen.BuildAsync(Queries, Loaded)),
            PlainText.RenderFind(await FindScreen.BuildAsync(Queries, Loaded, "de"), "de"),
        ];

        foreach (var line in find.SelectMany(s => s.Split('\n')))
        {
            if (line.TrimStart().StartsWith('/'))
            {
                continue;
            }

            await Assert.That(line.TrimEnd().Length).IsLessThanOrEqualTo(PlainText.Columns);
        }
    }

    [Test]
    public async Task TheListingSaysCodebaseNotIdentifiedRatherThanOmittingTheLine()
    {
        var text = PlainText.RenderListing(await Queries.SearchAsync(new GameFilter()), new GameFilter(), Now);

        await Assert.That(text).Contains("Codebase:    not identified");
    }

    [Test]
    public async Task TheRenderedRowNamesTheFieldBecauseNothingElseOnItDoes()
    {
        var html = await Render.PageAsync<Games>([]);

        await Assert.That(Render.Words(html)).Contains("Unknown Codebase");

        // Rule 5: unidentified is a fact about our parsers' reach, not the game.
        await Assert.That(html).Contains("we could not identify the codebase this game runs");
    }

    [Test]
    public async Task TheListingSaysHowEachCountAndCodebaseWasObtainedAndHowOldItIs()
    {
        var text = PlainText.RenderListing(
            await Queries.SearchAsync(new GameFilter { IncludeArchived = true }),
            new GameFilter { IncludeArchived = true },
            Now);

        await Assert.That(text).Contains("Players now: 15   (measured, 4m)");
        await Assert.That(text).Contains("Players now: 219   (measured, 40m)");
        await Assert.That(text).Contains("Players now: 9   (declared, 9m)");

        // Asserted through the bundle: a literal here would pass while the German page said something else.
        var label = Messages.For(
            Locales.SourceTag,
            "chip.plain.stale",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["how"] = Messages.For(Locales.SourceTag, "provenance.game.declared"),
                ["age"] = Relative.Format(Locales.SourceTag, TimeSpan.FromDays(365 * 3)),
            });

        await Assert.That(text).Contains($"PennMUSH 1.8.5  {label}");
    }

    [Test]
    public async Task TheRenderedListingRowCarriesTheSameLabelThePlainOneSpells()
    {
        var html = await Render.PageAsync<Games>([]);

        await Assert.That(html).Contains("class=\"chip declared");

        // The count is the exception: no glyph, no word beside the number on a listing row.
        await Assert.That(html).DoesNotContain("class=\"declared-note\"");
        await Assert.That(html).DoesNotContain("class=\"pip");

        // The codebase chip prints no age — the row has its own freshness column.
        var meta = html[html.IndexOf("class=\"meta", StringComparison.Ordinal)..];
        await Assert.That(meta[..meta.IndexOf("</p>", StringComparison.Ordinal)])
            .DoesNotContain("<time");
    }

    [Test]
    public async Task TheCountColumnCarriesANumberAndNothingElse()
    {
        // The column is named once above the rows; cells hold the bare figure.
        var html = await Render.PageAsync<Games>([]);
        var counts = html.Split("class=\"row-count").Skip(1).ToList();

        await Assert.That(counts).IsNotEmpty();

        foreach (var cell in counts)
        {
            var text = Render.Words(cell[..cell.IndexOf("</p>", StringComparison.Ordinal)]);

            // Either a bare number or the words for no number — never a unit, never a zero standing in for unknown.
            await Assert.That(text).DoesNotContain(" on");
            await Assert.That(text).DoesNotContain("●");
            await Assert.That(text).DoesNotContain("◇");
        }

        await Assert.That(Render.Words(html)).Contains("connected · trending · reached");
    }

    [Test]
    public async Task TheColumnHeadingNamesDiscoveredRatherThanReachedUnderThatSort()
    {
        // Decorative and aria-hidden, but still the only column guide a sighted reader sees — it has
        // to say what the last column actually shows while GameSort.Discovered is active, not the
        // "reached" wording that sort leaves behind.
        var html = await Render.PageAsync<Games>([], "?sort=discovered");

        await Assert.That(Render.Words(html)).Contains("connected · trending · discovered");
        await Assert.That(Render.Words(html)).DoesNotContain("connected · trending · reached");
    }

    [Test]
    public async Task TheGrowthArrowMatchesTheDirectionAndNeverAppearsWithoutOne()
    {
        var html = await Render.PageAsync<Games>([]);
        var rows = html.Split("class=\"game-row").Skip(1)
            .Select(r => r[..r.IndexOf("</li>", StringComparison.Ordinal)])
            .ToList();

        // M*U*S*H is fixed at Growth: GrowthDirection.Up in the fixture.
        var mush = rows.Single(r => r.Contains("href=\"/g/m-u-s-h\"", StringComparison.Ordinal));

        await Assert.That(mush).Contains("class=\"row-trend");
        await Assert.That(Render.Words(mush))
            .Contains(Messages.For(Locales.SourceTag, "facet.trending.up"));

        // Eldertale carries no Growth in the fixture — no direction was ever measured for it, and
        // the row must not invent one.
        var eldertale = rows.Single(r => r.Contains("href=\"/g/eldertale\"", StringComparison.Ordinal));

        await Assert.That(eldertale).DoesNotContain("class=\"row-trend");
    }

    [Test]
    public async Task TheGrowthArrowCarriesTheFittedLinesOwnPercentageAndSitsBesideTheCountNotBelowIt()
    {
        var html = await Render.PageAsync<Games>([]);
        var rows = html.Split("class=\"game-row").Skip(1)
            .Select(r => r[..r.IndexOf("</li>", StringComparison.Ordinal)])
            .ToList();

        // M*U*S*H is fixed at Growth: GrowthDirection.Up, GrowthChange: 0.25 in the fixture — a bare
        // glyph said "up" without saying by how much.
        var mush = rows.Single(r => r.Contains("href=\"/g/m-u-s-h\"", StringComparison.Ordinal));

        await Assert.That(Render.Words(mush)).Contains("+25%");

        // row-main's own grid places row-trend in row-count's row rather than the implicit row below
        // it that an unpositioned grid item falls to — the count and the trend read on one line.
        var countAt = mush.IndexOf("class=\"row-count", StringComparison.Ordinal);
        var trendAt = mush.IndexOf("class=\"row-trend", StringComparison.Ordinal);
        var countCell = mush[countAt..mush.IndexOf("</p>", countAt, StringComparison.Ordinal)];

        await Assert.That(countAt).IsGreaterThanOrEqualTo(0);
        await Assert.That(trendAt).IsGreaterThan(countAt);
        await Assert.That(countCell).DoesNotContain("row-trend");
    }

    [Test]
    public async Task TheAccessibleNameForTheTrendCarriesTheSamePercentageASightedReaderSees()
    {
        // aria-label used to name only the direction ("trending up"), leaving the +25% a screen
        // reader's own listener never hears — the number sighted readers see right beside it.
        var html = await Render.PageAsync<Games>([]);
        var rows = html.Split("class=\"game-row").Skip(1)
            .Select(r => r[..r.IndexOf("</li>", StringComparison.Ordinal)])
            .ToList();

        var mush = rows.Single(r => r.Contains("href=\"/g/m-u-s-h\"", StringComparison.Ordinal));
        var trend = mush[mush.IndexOf("class=\"row-trend", StringComparison.Ordinal)..];
        var ariaLabel = Render.Words(trend[..trend.IndexOf('>')]);

        await Assert.That(ariaLabel).Contains("+25%");
    }

    [Test]
    public async Task TheGrowthArrowNeverLivesInsideTheCountColumn()
    {
        // The count column's own rule (TheCountColumnCarriesANumberAndNothingElse) is a bare figure
        // or the words for none — a second fact folded into that cell would break both promises.
        var html = await Render.PageAsync<Games>([]);
        var counts = html.Split("class=\"row-count").Skip(1);

        foreach (var cell in counts)
        {
            await Assert.That(cell[..cell.IndexOf("</p>", StringComparison.Ordinal)])
                .DoesNotContain("row-trend");
        }
    }

    [Test]
    public async Task ThePlainListingSaysTheSameTrendTheRowDraws()
    {
        var listing = await Queries.SearchAsync(new GameFilter());
        var text = PlainText.RenderListing(listing, new GameFilter(), Now);

        await Assert.That(text)
            .Contains($"Trending:    {Messages.For(Locales.SourceTag, "facet.trending.up")}");
    }

    [Test]
    public async Task OneCountWearsOneGlyphAndNotTwo()
    {
        // Previously printed the glyph twice (pip and chip) — one fact told as two.
        var html = await Render.PageAsync<Games>([]);
        var figures = html.Split("class=\"row-figure").Skip(1);

        foreach (var figure in figures)
        {
            var row = figure[..figure.IndexOf("</p>", StringComparison.Ordinal)];

            await Assert.That(Glyphs(row))
                .IsLessThanOrEqualTo(1)
                .Because($"one count, one glyph: {row}");
        }

        await Assert.That(html).Contains("class=\"chip declared");
        await Assert.That(Render.Words(html)).Contains("declared");

        static int Glyphs(string markup) =>
            markup.Split("&#x25C7;").Length - 1 + markup.Split("&#x25CF;").Length - 1;
    }

    [Test]
    public async Task TheGamePagesLiveCountWearsTheSameChipTheListingRowDoes()
    {
        // Previously printed the measured glyph over every count regardless of source, so a reader
        // arriving from a "declared" listing row met a green dot saying otherwise.
        var declared = await Render.PageAsync<Game>(new() { ["Slug"] = "ashen-court" });
        var measured = await Render.PageAsync<Game>(new() { ["Slug"] = "m-u-s-h" });

        await Assert.That(Render.Words(declared)).Contains("declared");
        await Assert.That(declared).Contains("figure-count mono declared");
        await Assert.That(Render.Words(measured)).Contains("measured");
        await Assert.That(measured).Contains("figure-count mono");
        await Assert.That(measured).DoesNotContain("figure-count mono declared");

        // A count the page doesn't have says so in words, never as a zero — the null covers all
        // three of rule 2's states.
        var none = await Render.PageAsync<Game>(new() { ["Slug"] = "midnight-sun" });
        var figure = none[none.IndexOf("class=\"game-figure\"", StringComparison.Ordinal)..];
        var words = Render.Words(figure[..figure.IndexOf("</div>", StringComparison.Ordinal)]);

        await Assert.That(words).Contains(Messages.For(Locales.SourceTag, "game.count.none"));
        await Assert.That(words).DoesNotContain("0");
    }

    [Test]
    public async Task TheArchiveLabelsACodebaseExactlyAsTheListingDoes()
    {
        // Previously "PennMUSH 1.8.5 (declared, 3y, stale)" on /games but a bare "Codebase: PennMUSH
        // 1.8.5" on /archive — same value, only one surface saying it's unconfirmed.
        var text = PlainText.RenderArchive(await ArchiveAsync(), query: null, now: Now, tag: Locales.SourceTag);

        var label = Messages.For(
            Locales.SourceTag,
            "chip.plain.stale",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["how"] = Messages.For(Locales.SourceTag, "provenance.game.declared"),
                ["age"] = Relative.Format(Locales.SourceTag, TimeSpan.FromDays(365 * 3)),
            });

        await Assert.That(text).Contains($"PennMUSH 1.8.5  {label}");

        var html = await Render.PageAsync<Archive>([]);
        await Assert.That(html).Contains("class=\"chip declared");
    }

    [Test]
    public async Task TheHomePageCountsOnlyWhatWasMeasured()
    {
        var counts = SiteCounts.From(await Queries.ListAsync(new GameFilter { IncludeArchived = true }));
        var text = PlainText.RenderHome(
            Locales.SourceTag, counts, await Queries.FeedsAsync(), [], CrawlerPulse.Unknown, Now);

        await Assert.That(text).Contains("games known");
        await Assert.That(text).Contains("populated (measured)");
        await Assert.That(text).Contains("unknown population");
    }

    /// <summary>
    /// The four front-page figures, and the crawler's own line, in the reader's language.
    /// </summary>
    /// <remarks>The count labels and crawler strip were previously the last English on a localized page. The wordmark stays exempt: a site's name is machine voice, like a hostname.</remarks>
    [Test]
    public async Task TheHomeCountsAndTheCrawlerLineComeFromTheBundle()
    {
        var counts = SiteCounts.From(await Queries.ListAsync(new GameFilter { IncludeArchived = true }));
        var cycle = new CrawlCycleRecord(
            Now.AddMinutes(-2), Now.AddMinutes(-1), 8, 8, 6, 2, 0, 0, 0, 0, 0, 6, 0, 0, 0);
        var pulse = new CrawlerPulse(Now.AddMinutes(-1), Now.AddMinutes(3), 4, 710, cycle);

        var pseudo = PlainText.RenderHome("qps-ploc", counts, await Queries.FeedsAsync(), [], pulse, Now);

        // The wordmark is the one line that must not go through the bundle.
        await Assert.That(pseudo).Contains("MU*INDEX");

        await Assert.That(pseudo).DoesNotContain("crawler live");

        // The counts carry plural branches (the pseudolocale skips text inside braces), so the
        // signal is the ⟦⟧ wrapper: a renderer that interpolated its own line would print the same
        // words with no brackets.
        await Assert.That(pseudo)
            .Contains(Messages.For("qps-ploc", "home.plain.known", Count(counts.Known)));
        await Assert.That(pseudo)
            .Contains(Messages.For("qps-ploc", "home.plain.archived", Count(counts.Archived)));
        await Assert.That(pseudo).DoesNotContain($"\n{counts.Known} games known");
        await Assert.That(pseudo)
            .Contains(CrawlerCopy.Registry("qps-ploc", pulse))
            .And.DoesNotContain($"\n{pulse.TargetsKnown} addresses in the registry");

        var german = PlainText.RenderHome("de", counts, await Queries.FeedsAsync(), [], pulse, Now);

        foreach (var id in new[]
                 {
                     "home.plain.known", "home.plain.connectedNow",
                     "home.plain.uncounted", "home.plain.archived",
                 })
        {
            await Assert.That(german).Contains(Messages.For("de", id, Count(CountFor(id, counts))));
        }

        await Assert.That(german).Contains(CrawlerCopy.State("de", pulse, Now));
        await Assert.That(german).Contains(CrawlerCopy.LastCycle("de", pulse)!);
        await Assert.That(german).Contains(CrawlerCopy.Registry("de", pulse));

        var first = (await Queries.FeedsAsync()).NewlyDiscovered[0];

        await Assert.That(german).Contains(Relative.Ago("de", Now - first.At));

        static Dictionary<string, object?> Count(int count) =>
            new(StringComparer.Ordinal) { ["count"] = count };

        static int CountFor(string id, SiteCounts counts) => id switch
        {
            "home.plain.known" => counts.Known,
            "home.plain.connectedNow" => counts.WithPlayersOn,
            "home.plain.uncounted" => counts.CountUnknown,
            _ => counts.Archived,
        };
    }

    [Test]
    public async Task NoSurfaceOffersAVoteStarRatingOrRecommendation()
    {
        var counts = SiteCounts.From(await Queries.ListAsync(new GameFilter { IncludeArchived = true }));
        var surfaces = new[]
        {
            await GameAsync("m-u-s-h"),
            PlainText.RenderHome(
                Locales.SourceTag, counts, await Queries.FeedsAsync(), [], CrawlerPulse.Unknown, Now),
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
