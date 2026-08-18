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
}
