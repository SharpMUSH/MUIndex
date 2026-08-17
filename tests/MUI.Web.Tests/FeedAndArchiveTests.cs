using MUI.Catalog;
using MUI.Web.Components;
using MUI.Web.Fixtures;

namespace MUI.Web.Tests;

/// <summary>
/// The three liveness feeds and the archive — the two surfaces no incumbent can publish.
/// </summary>
public class FeedAndArchiveTests
{
    private static readonly DateTimeOffset Now = FixtureGameQueries.Now;
    private static readonly FixtureGameQueries Queries = new();

    private static Task<string> CardAsync(FeedEntry entry, FeedKind kind) =>
        Render.ComponentAsync<FeedCard>(new()
        {
            ["Entry"] = entry,
            ["Kind"] = kind,
            ["Now"] = Now,
        });

    [Test]
    public async Task TheThreeRegistersAreOneShapeWithThreeHeadings()
    {
        var entry = new FeedEntry(Guid.NewGuid(), "x", "Somewhere", Now.AddHours(-3), "detail");

        var discovered = await CardAsync(entry, FeedKind.NewlyDiscovered);
        var dark = await CardAsync(entry, FeedKind.WentDark);
        var back = await CardAsync(entry, FeedKind.CameBack);

        await Assert.That(discovered).Contains("newly discovered");
        await Assert.That(dark).Contains("went dark");
        await Assert.That(back).Contains("came back");

        foreach (var html in new[] { discovered, dark, back })
        {
            // A row rather than a card: three columns of boxes gave a name and an age the furniture
            // of a section, on a page whose only other framed thing is somebody else's connect
            // screen. The three registers are still three, and still one shape.
            await Assert.That(html).Contains("<article class=\"feed-row");
            await Assert.That(html).Contains("href=\"/g/x\"");
        }
    }

    [Test]
    public async Task ARowSaysWhatHappenedAndWhenAndNothingTheHeadingAlreadySaid()
    {
        // Three lists of five rows, and every row used to carry a prose line under it — "first
        // seen", "answered again", "we keep knocking", and on the discovery feed the referral that
        // found the game and the codebase it answered with. The column heading above says which
        // register this is, so each of those was the same fact announced twice: twenty extra lines
        // on the front page for a screen reader to wade through.
        //
        // What survives is a *cause*, which is not a repetition of the heading — the socket was
        // refused, or the name did not resolve. It rides in the one right-hand cell beside the age
        // rather than on a second line: "refused · 16h".
        var entry = new FeedEntry(Guid.NewGuid(), "x", "The Long Sleep", Now, "connection refused");

        var back = await CardAsync(entry, FeedKind.CameBack);
        var dark = await CardAsync(entry, FeedKind.WentDark);
        var found = await CardAsync(entry, FeedKind.NewlyDiscovered);

        // No second line on any of the three.
        foreach (var html in new[] { back, dark, found })
        {
            await Assert.That(html).DoesNotContain("class=\"return-line\"");
            await Assert.That(html).DoesNotContain("class=\"mono detail\"");
        }

        // A return says it is back; a cause appears only where there is one to give.
        await Assert.That(Render.Words(back)).Contains("live");
        await Assert.That(Render.Words(dark)).Contains("connection refused");
        await Assert.That(Render.Words(found)).DoesNotContain("connection refused");
    }

    [Test]
    public async Task TheRegisterCanBeSuppressedWhereTheColumnAlreadyNamesIt()
    {
        // Otherwise the front page reads "newly discovered newly discovered".
        var entry = new FeedEntry(Guid.NewGuid(), "x", "Somewhere", Now, "detail");

        var html = await Render.ComponentAsync<FeedCard>(new()
        {
            ["Entry"] = entry,
            ["Kind"] = FeedKind.NewlyDiscovered,
            ["Now"] = Now,
            ["ShowRegister"] = false,
        });

        await Assert.That(html).DoesNotContain("newly discovered");
        await Assert.That(html).Contains("Somewhere");
    }

    [Test]
    public async Task TheArchiveBandIsAQueryAndNotAClientSideFilter()
    {
        var archived = await Queries.ListAsync(new GameFilter
        {
            Band = ActivityBand.Archived,
            IncludeArchived = true,
        });

        await Assert.That(archived.Count()).IsEqualTo(2);
        await Assert.That(archived.All(g => g.State is LifecycleState.Archived)).IsTrue();
    }

    [Test]
    public async Task TheArchiveIsSearchable()
    {
        var found = await Queries.ListAsync(new GameFilter
        {
            Band = ActivityBand.Archived,
            IncludeArchived = true,
            Text = "gaslight",
        });

        await Assert.That(found.Count()).IsEqualTo(1);
        await Assert.That(found[0].Slug).IsEqualTo("gaslight-row");
    }

    [Test]
    public async Task AnArchiveEntryCarriesWhenItWasLastReachableAndHowLongItWasKnownLive()
    {
        var game = (await Queries.ListAsync(new GameFilter { Band = ActivityBand.Archived, IncludeArchived = true }))
            .First(g => g.Slug == "gaslight-row");
        var entry = ArchiveEntry.For(game, await Queries.ForGameAsync(game.Id), Now);

        await Assert.That(entry.LastReachableAt).IsNotNull();
        await Assert.That(entry.KnownLive).IsGreaterThan(TimeSpan.FromDays(365 * 10));
        await Assert.That(entry.LastAnswered).Contains("2023");
        await Assert.That(entry.Run).IsNotNull();
    }

    [Test]
    public async Task AGameWeCouldNeverReachHasNoRunToState()
    {
        // Inventing a run from the dates we happen to hold would be asserting rather than measuring.
        var game = (await Queries.ListAsync(new GameFilter { IncludeArchived = true })).First();
        var entry = ArchiveEntry.For(game, [], Now);

        await Assert.That(entry.Run).IsNull();
        await Assert.That(entry.LastAnswered).Contains("never");
        await Assert.That(entry.KnownLiveWording).IsEqualTo("no reachable time measured");
    }

    [Test]
    public async Task AnArchivedGameIsOutOfTheDefaultListingAndOfNothingElse()
    {
        var listing = await Queries.ListAsync(new GameFilter());
        var page = await Queries.FindAsync("gaslight-row");

        await Assert.That(listing.Any(g => g.Slug == "gaslight-row")).IsFalse();
        await Assert.That(page).IsNotNull();
        await Assert.That(page!.ConnectScreen).IsNotNull();
    }
}
