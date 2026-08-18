using MUI.Catalog.Persistence;

namespace MUI.Catalog.Tests;

/// <summary>
/// That the cause vocabulary is the same size in every place it is written down.
/// </summary>
/// <remarks>
/// Migration 0026 said it about presence reasons and it is just as true here: "a member added on one
/// side and forgotten on the other has to fail at the database rather than become a value every
/// reader downstream copes with differently". These are the sides that cannot fail loudly on their
/// own — <c>SqlEnums</c> throws, so it is honest already, but a `switch` with a default arm is not,
/// and the site had one.
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
