using MUI.Discovery;

namespace MUI.Discovery.Tests;

/// <summary>
/// A referral going away is a fact about the graph, not about the game. Losing the edge must not
/// lose the target (spec §7.1).
/// </summary>
public class ReferralEdgeTests
{
    private static readonly DateTimeOffset Then = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AnEdgeThatDisappearsIsMarkedAbsentRatherThanDeleted()
    {
        var edge = new ReferralEdge(Guid.NewGuid(), "example.org", 4201, Then, Then, Present: true);

        var gone = edge with { Present = false, LastSeenAt = Then.AddDays(30) };

        await Assert.That(gone.Present).IsFalse();
        await Assert.That(gone.ToHost).IsEqualTo("example.org");
        await Assert.That(gone.FirstSeenAt).IsEqualTo(Then);
    }
}
