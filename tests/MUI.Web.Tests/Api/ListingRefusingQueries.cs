using MUI.Catalog;

namespace MUI.Web.Tests.Api;

/// <summary>
/// A catalogue that answers only what these routes are entitled to ask, and throws at the rest.
/// </summary>
/// <remarks>
/// <para>
/// The refusal is the assertion. Two routes used to answer by reading the whole listing and picking
/// a row out of it — the feeds, to put an id beside a slug, and a lookup by GUID, because
/// <see cref="IGameQueries"/> had no way to ask for one game's <em>page</em> by id. Both are single
/// lookups now, and the only way to prove a scan is gone is to make the scan throw.
/// </para>
/// <para>
/// <see cref="FindByIdAsync"/> refuses too. It is a real method with real callers — the owner
/// surfaces address a game by id and want exactly a summary — but no route in this API does, and it
/// was the second half of the GUID workaround: assemble a whole summary, fields and presence digest
/// and all, then use its slug and discard the rest. Refusing it here is what stops that coming back.
/// </para>
/// <para>
/// A counter would not do: it would pass a version of this code that read the catalogue once and
/// cached it, which is the same scan with a lifetime bolted on.
/// </para>
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

    public Task<Rankings> RankingsAsync(CancellationToken cancellationToken = default) =>
        inner.RankingsAsync(cancellationToken);
}
