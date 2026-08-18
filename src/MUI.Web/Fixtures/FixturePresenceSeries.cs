using MUI.Catalog;

namespace MUI.Web.Fixtures;

/// <summary>
/// The demo fixture's presence series, which is empty — because nothing here was measured.
/// </summary>
/// <remarks>
/// Serving the fixture's invented heatmap numbers through §10's series would hand a consumer a
/// rolled-up history of measurements never taken, with no demo banner in a JSON body to say so.
/// Empty is honest: an absent bucket already means <em>not measured</em> everywhere else in this API.
/// </remarks>
public sealed class FixturePresenceSeries : IPresenceSeries
{
    public Task<IReadOnlyList<PresenceRollup>> ForGameAsync(
        Guid gameId,
        PresenceGrain grain,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PresenceRollup>>([]);
}
