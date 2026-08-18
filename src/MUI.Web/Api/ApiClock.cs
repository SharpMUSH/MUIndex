namespace MUI.Web.Api;

/// <summary>
/// The instant an API response is computed against, truncated to the minute.
/// </summary>
/// <remarks>
/// Truncated to the minute so identical facts serialize to identical bytes within that window and
/// <c>If-None-Match</c> actually hits; no registry field refreshes faster than a minute, so nothing
/// observable is lost. Takes a <see cref="TimeProvider"/> rather than <c>DateTimeOffset.UtcNow</c> so
/// the rendered page and this API can't reach for a clock and disagree.
/// </remarks>
public static class ApiClock
{
    public static readonly TimeSpan Granularity = TimeSpan.FromMinutes(1);

    public static DateTimeOffset Now(TimeProvider clock) => Truncate(clock.GetUtcNow());

    public static DateTimeOffset Truncate(DateTimeOffset at)
    {
        var utc = at.UtcDateTime;
        return new DateTimeOffset(utc.AddTicks(-(utc.Ticks % Granularity.Ticks)), TimeSpan.Zero);
    }
}
