using MUI.Catalog.Persistence;

namespace MUI.Catalog.Tests;

/// <summary>
/// That the cause vocabulary is the same size in every place it is written down.
/// </summary>
/// <remarks>
/// A cause added on one side and forgotten on the other must fail loudly, not become a value each
/// reader copes with differently. <c>SqlEnums</c> throws; a `switch` with a default arm would not.
/// </remarks>
public class FailureCauseVocabularyTests
{
    [Test]
    public async Task EveryCauseSurvivesTheRoundTripThroughStorage()
    {
        foreach (var cause in Enum.GetValues<FailureCause>())
        {
            await Assert.That(SqlEnums.ToFailureCause(SqlEnums.ToDb(cause))).IsEqualTo(cause);
        }
    }
}
