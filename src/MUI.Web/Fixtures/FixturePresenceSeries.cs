using MUI.Catalog;

namespace MUI.Web.Fixtures;

/// <summary>
/// The demo fixture's presence series, which is empty — because nothing here was measured.
/// </summary>
/// <remarks>
/// <para>
/// The fixture invents a catalogue so the site can be read without a database, and its heatmap is
/// drawn from numbers somebody wrote down. Serving those back through §10's series would be handing
/// a consumer a rolled-up history of measurements that were never taken, in a format whose whole
/// contract is that every bucket is a tally of real probes — and unlike a page, a JSON body carries
/// no demo banner to say so.
/// </para>
/// <para>
/// Empty is the honest answer and it is also the true one: the buckets are absent, and an absent
/// bucket already means <em>not measured</em> everywhere else in this API.
/// </para>
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
