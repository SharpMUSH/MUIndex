using MUI.Catalog;
using MUI.Web.Fixtures;
using MUI.Web.Reference;

namespace MUI.Web.Tests;

/// <summary>
/// That the numbers on a reference page come from the catalogue and from nowhere else.
/// </summary>
/// <remarks>
/// Guards against a hand-typed count that looks honest but ages silently. So the count is asserted to
/// <em>move when the catalogue moves</em> — a fixed number would pass a test that only checked a
/// number rendered at all.
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
        var page = Library.Find(ReferenceKind.Codebase, "tinymux")!;
        var games = Enumerable.Range(0, 50).Select(i => Game($"g{i}", "TinyMUX 2.12")).ToArray();

        var figures = await CodebaseFigures.ReadAsync(new StubQueries(games), page.Codebase!);

        await Assert.That(figures.Listed).IsEqualTo(50);
    }

    [Test]
    public async Task ArchivedGamesAreCountedSeparatelyRatherThanDropped()
    {
        // Rule 3: archiving removes a game from the default listing and from nothing else.
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
        var text = MUI.Web.Components.ReferencePlainText.Render(
            MUI.Web.Localization.Locales.SourceTag, page, codebase: figures);

        await Assert.That(Render.Words(text)).Contains(Render.Words(
            MUI.Web.Localization.Messages.For(
                MUI.Web.Localization.Locales.SourceTag, "reference.plain.codebase.none")));
    }

    [Test]
    public async Task AProtocolFigureCountsOnlyMeasuredHandshakes()
    {
        // Measured vs. declared (rule 1): MeasuredProtocols carries what a server offered, never a
        // game's own MSSP claim.
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
        // No third number: the remainder mixes servers lacking the protocol with servers whose
        // handshake we haven't read, and we can't tell them apart.
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
        // The printed count and the linked listing must come from the same question, or the page
        // lies by arithmetic.
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

    /// <summary>A catalogue that answers with exactly what a test handed it, applying the same filter the real one does.</summary>
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

        public Task<IReadOnlyList<RecentGameChange>> RecentFieldChangesAsync(
            int limit, int perGameLimit = 3, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This stub answers listing questions only.");
    }
}
