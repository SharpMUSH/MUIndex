namespace MUI.Crawler.Tests.Support;

/// <summary>
/// A clock a test moves by hand.
/// </summary>
/// <remarks>
/// Nothing in this suite sleeps. The salt epoch is a week long, and a test that waited for one would
/// not be a test — so the only way to assert that a rotation produces different bytes is to move the
/// clock across the boundary and ask again.
/// </remarks>
public sealed class SettableClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);

        _now += by;
    }
}
