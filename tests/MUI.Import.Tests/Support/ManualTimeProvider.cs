namespace MUI.Import.Tests.Support;

/// <summary>
/// A clock the test moves by hand, so every rate-limit assertion is deterministic and instant.
/// </summary>
/// <remarks>
/// Overriding <see cref="GetUtcNow"/> alone is sufficient because <see cref="PolitenessGate"/> exposes
/// its wait as a pure function of "now" (<c>WaitFor</c>) and only sleeps when that returns a positive
/// span, which no test here arranges. Named this way rather than <c>FakeTimeProvider</c> to avoid
/// being mistaken for the real type of that name in a package this repository does not reference.
/// </remarks>
public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
