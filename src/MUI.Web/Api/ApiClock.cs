namespace MUI.Web.Api;

/// <summary>
/// The instant an API response is computed against, truncated to the minute.
/// </summary>
/// <remarks>
/// <para>
/// Every fact this API returns carries its age, and an age is a subtraction from <em>now</em>. Taken
/// raw, that makes every response byte-different from the one a microsecond earlier — which means a
/// strong ETag never matches, a conditional request never returns 304, and the caching the spec asks
/// for buys nothing at all.
/// </para>
/// <para>
/// Truncating the reference instant to the minute fixes that without lying: within a minute the
/// bytes are identical and <c>If-None-Match</c> genuinely hits; across a minute the ages really did
/// change, so a new ETag is the truthful answer rather than a cache miss. A minute is far finer than
/// the refresh window of any field in the registry — the shortest is measured in probe cycles — so
/// nothing a reader can act on is lost.
/// </para>
/// <para>
/// It is a <see cref="TimeProvider"/> and not <c>DateTimeOffset.UtcNow</c> for the same reason the
/// pages take one: the rendered page and this API must not each reach for a clock and disagree.
/// </para>
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
