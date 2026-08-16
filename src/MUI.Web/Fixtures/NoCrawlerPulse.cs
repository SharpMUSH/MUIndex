using MUI.Catalog;
using MUI.Web.Data;

namespace MUI.Web.Fixtures;

/// <summary>
/// The demo path's answer about the crawler: there isn't one.
/// </summary>
/// <remarks>
/// <b>Deliberately not a plausible-looking heartbeat.</b> Every other fixture on this site invents a
/// value so a page can be seen without a database, and the demo banner is what keeps that honest.
/// This one may not: the strip's entire content is a claim that the instrument behind every other
/// number is running, and a fixture that said so would be inventing the one fact whose invention the
/// banner cannot cover. <see cref="CrawlerPulse.Unknown"/> renders as nothing, which is true.
/// </remarks>
public sealed class NoCrawlerPulse : ICrawlerPulse
{
    public Task<CrawlerPulse> ReadAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(CrawlerPulse.Unknown);
}
