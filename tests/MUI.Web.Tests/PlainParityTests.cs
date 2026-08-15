using MUI.Catalog;
using MUI.Web.Components;
using MUI.Web.Components.Pages;
using MUI.Web.Fixtures;

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
        return PlainText.Render(page, Now, reach);
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

        await Assert.That(text).Contains("Only 2 row(s) came back");
    }

    [Test]
    public async Task AnOversizedScreenSaysHowLongItIs()
    {
        var text = await GameAsync("batmud");

        await Assert.That(text).Contains("connect screen: 214 lines");
        await Assert.That(text).Contains("Unusually long");
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
        // The rendered row and the plain row have to be two renderings of one fact. The chip is the
        // vocabulary the game page already uses — glyph, relative age, amber when it has aged out —
        // and the row reuses it rather than inventing a second way of saying "we read this off a
        // banner forty minutes ago".
        var html = await Render.PageAsync<Games>([]);

        await Assert.That(html).Contains("class=\"chip measured");
        await Assert.That(html).Contains("class=\"chip declared");

        // Never colour or glyph alone: the word is in the accessibility tree either as the chip's
        // own title or as text only a screen reader reads.
        await Assert.That(Render.Words(html)).Contains("declared");
        await Assert.That(Render.Words(html)).Contains("measured");
    }

    [Test]
    public async Task TheRowsGlyphAgreesWithTheChipBesideIt()
    {
        // The chip was added to the row and the glyph in front of it was left hard-coded measured,
        // so a declared count rendered a green ● beside a ◇ chip — one number described two ways in
        // the same breath, which is the disagreement the chip exists to end. The game page was fixed
        // and the listing that points at it was not.
        var html = await Render.PageAsync<Games>([]);

        // Razor encodes both glyphs, so the assertion reads them as the markup carries them.
        await Assert.That(html).Contains("class=\"state-declared\" aria-hidden=\"true\">&#x25C7;");
        await Assert.That(html).Contains("class=\"state-present\" aria-hidden=\"true\">&#x25CF;");
    }

    [Test]
    public async Task TheGamePagesLiveCountWearsTheSameChipTheListingRowDoes()
    {
        // The rendered game page printed the measured glyph over every count it had, whoever
        // produced the number — so a reader arriving from a listing row that said "declared" met a
        // green dot saying otherwise. One vocabulary, and the same one.
        var declared = await Render.PageAsync<Game>(new() { ["Slug"] = "ashen-court" });
        var measured = await Render.PageAsync<Game>(new() { ["Slug"] = "m-u-s-h" });

        await Assert.That(declared).Contains("class=\"chip declared");
        await Assert.That(Render.Words(declared)).Contains("declared");
        await Assert.That(measured).Contains("class=\"chip measured");

        // A count nobody could take gets no chip, because there is nothing to label.
        var none = await Render.PageAsync<Game>(new() { ["Slug"] = "midnight-sun" });
        await Assert.That(none).Contains("count unknown");
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
        var text = PlainText.RenderHome(counts, await Queries.FeedsAsync(), Now);

        await Assert.That(text).Contains("games known");
        await Assert.That(text).Contains("with players on right now (measured)");
        await Assert.That(text).Contains("answering with nothing we can count");
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
            PlainText.RenderHome(counts, await Queries.FeedsAsync(), Now),
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
