using MUI.Catalog;

namespace MUI.Web.Tests.Api;

/// <summary>
/// A catalogue that answers only what these routes are entitled to ask, and throws at the rest.
/// </summary>
/// <remarks>
/// The refusal is the assertion: the only way to prove a scan is gone is to make the scan throw. A
/// counter would not do — it would pass code that scans once and caches, the same scan with a
/// lifetime bolted on. <see cref="FindByIdAsync"/> refuses too, since no API route uses it even
/// though it has real callers elsewhere.
/// </remarks>
internal sealed class ListingRefusingQueries(IGameQueries inner) : IGameQueries
{
    private const string Refusal =
        "This route must answer without reading the whole catalogue.";

    public Task<GameListing> SearchAsync(
        GameFilter filter, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(Refusal);

    public Task<IReadOnlyList<GameSummary>> ListAsync(
        GameFilter filter, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(Refusal);

    public Task<GamePage?> FindAsync(string slug, CancellationToken cancellationToken = default) =>
        inner.FindAsync(slug, cancellationToken);

    public Task<GamePage?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        inner.FindAsync(id, cancellationToken);

    public Task<GameSummary?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "No API route needs a summary by id; the page route asks for a page.");

    public Task<LivenessFeeds> FeedsAsync(CancellationToken cancellationToken = default) =>
        inner.FeedsAsync(cancellationToken);

    public Task<EcosystemDashboard> EcosystemAsync(CancellationToken cancellationToken = default) =>
        inner.EcosystemAsync(cancellationToken);

    public Task<Rankings> RankingsAsync(
        RankingSpan span = RankingSpan.Week,
        CancellationToken cancellationToken = default) =>
        inner.RankingsAsync(span, cancellationToken);

    public Task<IReadOnlyList<RecentGameChange>> RecentFieldChangesAsync(
        int limit, int perGameLimit = 3, CancellationToken cancellationToken = default) =>
        inner.RecentFieldChangesAsync(limit, perGameLimit, cancellationToken);
}
