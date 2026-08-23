using MUI.Catalog;
using MUI.Web.Data;

namespace MUI.Web.Fixtures;

/// <summary>
/// The demo path's answer about the crawler: there isn't one.
/// </summary>
/// <remarks>
/// Deliberately not a plausible-looking heartbeat: the strip's whole content is a claim that the
/// instrument behind every other number is running, which the demo banner cannot cover if faked.
/// <see cref="CrawlerPulse.Unknown"/> renders as nothing, which is true.
/// </remarks>
public sealed class NoCrawlerPulse : ICrawlerPulse
{
    public Task<CrawlerPulse> ReadAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(CrawlerPulse.Unknown);

    public Task<IReadOnlyList<CrawlCycleRecord>> RecentAsync(
        int count,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CrawlCycleRecord>>([]);

    public Task<CrawlWindow> WindowAsync(
        DateTimeOffset now,
        TimeSpan span,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(CrawlWindow.Empty(span));

    public Task<IReadOnlyList<DueTarget>> DueSoonAsync(
        DateTimeOffset now,
        int count,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DueTarget>>([]);
}
