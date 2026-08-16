using MUI.Catalog;
using MUI.Web.Fixtures;
using MUI.Web.Reference;

namespace MUI.Web.Tests;

/// <summary>
/// That the numbers on a reference page come from the catalogue and from nowhere else.
/// </summary>
/// <remarks>
/// The failure being guarded against is the one that makes a directory worthless: a page that says
/// "47 games run this" because somebody typed 47. It looks exactly like the honest version, it ages
/// silently, and no reader can tell. So the count is asserted to <em>move when the catalogue moves</em>
/// — a fixed number would pass any test that only checked the page rendered a number at all.
/// </remarks>
public class ReferenceFiguresTests
{
    private static readonly ReferenceLibrary Library = ReferenceLibrary.Shipped;

    [Test]
    public async Task ACodebaseCountMovesWhenTheCatalogueMoves()
    {
        var page = Library.Find(ReferenceKind.Codebase, "pennmush")!;

        var sparse = await CodebaseFigures.ReadAsync(new StubQueries(Game("a", "PennMUSH 1.8.8p0")), page.Codebase!);
        var fuller = await CodebaseFigures.ReadAsync(
            new StubQueries(Game("a", "PennMUSH 1.8.8p0"), Game("b", "PennMUSH 1.8.7"), Game("c", "PennMUSH 1.8.5")),
            page.Codebase!);

        await Assert.That(sparse.Listed).IsEqualTo(1);
        await Assert.That(fuller.Listed).IsEqualTo(3);
    }

    [Test]
    public async Task ACodebaseCountIsWhateverTheQueryWasGivenAndNeverWhatTheFileSays()
    {
        // Deliberately absurd: a query answering with fifty TinyMUX games makes the TinyMUX page say
        // fifty. Nothing in the content file has a say in it.
        var page = Library.Find(ReferenceKind.Codebase, "tinymux")!;
        var games = Enumerable.Range(0, 50).Select(i => Game($"g{i}", "TinyMUX 2.12")).ToArray();

        var figures = await CodebaseFigures.ReadAsync(new StubQueries(games), page.Codebase!);

        await Assert.That(figures.Listed).IsEqualTo(50);
    }

    [Test]
    public async Task ArchivedGamesAreCountedSeparatelyRatherThanDropped()
    {
        // Archiving removes a game from the default listing and from nothing else. A codebase with
        // four live games and thirty archived ones is a fact about the hobby, not a smaller number.
        var figures = await CodebaseFigures.ReadAsync(
            new StubQueries(
                Game("live", "SMAUG 1.4"),
                Game("gone", "SMAUG 1.4") with { State = LifecycleState.Archived }),
            "SMAUG");

        await Assert.That(figures.Listed).IsEqualTo(1);
        await Assert.That(figures.Archived).IsEqualTo(1);
        await Assert.That(figures.Known).IsEqualTo(2);
    }

    [Test]
    public async Task AFamilyWithNothingIdentifiedIsNotAZeroLeftToSpeakForItself()
    {
        var figures = await CodebaseFigures.ReadAsync(new StubQueries(Game("a", "TinyMUX 2.12")), "PennMUSH");

        await Assert.That(figures.Known).IsEqualTo(0);

        var page = Library.Find(ReferenceKind.Codebase, "pennmush")!;
        var text = MUI.Web.Components.ReferencePlainText.Render(page, codebase: figures);

        await Assert.That(Render.Words(text)).Contains("None yet");
        await Assert.That(Render.Words(text)).Contains("not about what exists");
    }

    [Test]
    public async Task AProtocolFigureCountsOnlyMeasuredHandshakes()
    {
        // GameSummary.MeasuredProtocols carries what a server offered. A game's own MSSP claim never
        // reaches this list, and that is the whole difference between this matrix and every protocol
        // table the hobby already has.
        var queries = new StubQueries(
            Game("a", "PennMUSH 1.8.8p0", "MSSP", "GMCP"),
            Game("b", "PennMUSH 1.8.8p0", "MSSP"),
            Game("c", "Evennia", "GMCP"));

        var figures = await ProtocolFigures.ReadAsync(
            queries, "GMCP", Library.OfKind(ReferenceKind.Codebase));

        await Assert.That(figures.Offering).IsEqualTo(2);
        await Assert.That(figures.Listed).IsEqualTo(3);

        var penn = figures.ByCodebase.Single(r => r.Codebase == "PennMUSH");
        await Assert.That(penn.Identified).IsEqualTo(2);
        await Assert.That(penn.Offering).IsEqualTo(1);
    }

    [Test]
    public async Task AProtocolMatrixNeverPublishesTheComplementAsAnAbsence()
    {
        // ProtocolByCodebase carries Identified and Offering and no third number, so there is no
        // "does not support" for a renderer to reach for. The remainder mixes servers that lack the
        // protocol with servers whose handshake we have not read, and we cannot tell them apart.
        var properties = typeof(ProtocolByCodebase).GetProperties().Select(p => p.Name.ToLowerInvariant());

        foreach (var name in properties)
        {
            await Assert.That(name).DoesNotContain("absent");
            await Assert.That(name).DoesNotContain("missing");
            await Assert.That(name).DoesNotContain("unsupported");
        }
    }

    [Test]
    public async Task TheCodebaseLinkAndTheCountAreOneFilter()
    {
        // The page prints a number and offers a link, and a reader who follows the link counts the
        // rows. If those two came from different questions the page would be lying by arithmetic.
        var page = Library.Find(ReferenceKind.Codebase, "evennia")!;
        var queries = new FixtureGameQueries();

        var figures = await CodebaseFigures.ReadAsync(queries, page.Codebase!);
        var listing = await queries.ListAsync(new GameFilter { Codebase = FacetChoice.Of(page.Codebase!) });

        await Assert.That(figures.Listed).IsEqualTo(listing.Count);
        await Assert.That(page.GamesPath).IsEqualTo("/games?codebase=Evennia");
    }

    private static GameSummary Game(string slug, string? codebase, params string[] protocols) => new(
        Guid.NewGuid(), slug, slug, null, LifecycleState.Active, IsClaimed: false,
        PlayersNow: 0, codebase, protocols);

    /// <summary>
    /// A catalogue that answers with exactly what a test handed it, applying the same filter the
    /// real one does. It exists so a count can be shown to <em>track</em> the query rather than
    /// merely to be a number.
    /// </summary>
    private sealed class StubQueries(params GameSummary[] games) : IGameQueries
    {
        public Task<IReadOnlyList<GameSummary>> ListAsync(
            GameFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GameSummary>>(
            [
                .. games
                    .Where(g => filter.IncludeArchived || g.State is not LifecycleState.Archived)
                    .Where(g => filter.Codebase is not { } family
                        || family.Admits(family.Covers(CodebaseFamily.For(g.Codebase)))),
            ]);

        public Task<GamePage?> FindAsync(string slug, CancellationToken cancellationToken = default) =>
            Task.FromResult<GamePage?>(null);

        public Task<GamePage?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<GamePage?>(null);

        public Task<LivenessFeeds> FeedsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LivenessFeeds([], [], []));

        public Task<GameSummary?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<GameSummary?>(games.FirstOrDefault(g => g.Id == id));

        public async Task<GameListing> SearchAsync(
            GameFilter filter, CancellationToken cancellationToken = default) =>
            new(await ListAsync(filter, cancellationToken), []);

        public Task<EcosystemDashboard> EcosystemAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This stub answers listing questions only.");

        public Task<Rankings> RankingsAsync(
            RankingSpan span = RankingSpan.Week,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This stub answers listing questions only.");
    }
}
