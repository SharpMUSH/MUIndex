namespace MUI.Web.Tests;

/// <summary>A clock that does not move, for assertions about ages the fixture stamped.</summary>
public sealed class FixedClock(DateTimeOffset at) : TimeProvider
{
    /// <summary>Standing where the fixture's own facts were stamped, so its ages read as written.</summary>
    public static FixedClock AtFixtureNow() => new(Fixtures.FixtureGameQueries.Now);

    public override DateTimeOffset GetUtcNow() => at;
}
