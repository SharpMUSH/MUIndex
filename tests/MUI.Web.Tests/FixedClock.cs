namespace MUI.Web.Tests;

/// <summary>A clock that does not move on its own, for assertions about ages the fixture stamped.</summary>
public sealed class FixedClock(DateTimeOffset at) : TimeProvider
{
    private DateTimeOffset _at = at;

    /// <summary>Standing where the fixture's own facts were stamped, so its ages read as written.</summary>
    public static FixedClock AtFixtureNow() => new(Fixtures.FixtureGameQueries.Now);

    public override DateTimeOffset GetUtcNow() => _at;

    /// <summary>
    /// Moves it, for the few tests that need two moments rather than one.
    /// </summary>
    /// <remarks>
    /// Still not a clock that runs: a test that wants time to pass says so and says how much, so what
    /// it asserts is a consequence of the interval it named rather than of how long the test host
    /// took to get there.
    /// </remarks>
    public void Advance(TimeSpan by) => _at += by;
}
