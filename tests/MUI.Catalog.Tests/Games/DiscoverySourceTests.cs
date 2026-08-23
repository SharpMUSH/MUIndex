using MUI.Catalog;

namespace MUI.Catalog.Tests;

/// <summary>
/// How an address reached the registry, as a dated fact about our crawl rather than a claim about
/// the game. Every spelling here is written to the database and read back out of it, so a rename is
/// a migration and not an edit.
/// </summary>
public class DiscoverySourceTests
{
    [Test]
    [Arguments(DiscoverySource.OperatorSeed, "operator_seed")]
    [Arguments(DiscoverySource.Submission, "submission")]
    [Arguments(DiscoverySource.Referral, "referral")]
    [Arguments(DiscoverySource.I3Mudlist, "i3_mudlist")]
    [Arguments(DiscoverySource.AresCentral, "ares_central")]
    [Arguments(DiscoverySource.Backfill, "backfill")]
    public async Task EverySourceRoundTripsThroughItsDatabaseSpelling(
        DiscoverySource source, string spelling)
    {
        await Assert.That(DiscoverySources.ToDb(source)).IsEqualTo(spelling);
        await Assert.That(DiscoverySources.From(spelling)).IsEqualTo(source);
    }

    /// <summary>
    /// Every address that existed before this column did has no answer, and a guess would be worse
    /// than silence — the page renders nothing rather than naming a source we never recorded.
    /// </summary>
    [Test]
    public async Task AnUnrecordedSourceStaysUnknown()
    {
        await Assert.That(DiscoverySources.From(null)).IsNull();
        await Assert.That(DiscoverySources.From("")).IsNull();
    }

    /// <summary>
    /// A spelling the database allowed and this build does not know is a deployment mid-rollout, not
    /// a corrupt row. Unknown, never an exception on a page render.
    /// </summary>
    [Test]
    public async Task AnUnrecognisedSpellingIsUnknownRatherThanAThrow()
    {
        await Assert.That(DiscoverySources.From("mudstats")).IsNull();
    }

    /// <summary>
    /// The vocabulary the two CHECK constraints in migration 0033 spell out. A member added here and
    /// not there is a row the database refuses at write time, in production, on a Sunday.
    /// </summary>
    [Test]
    public async Task EveryMemberHasASpellingTheSchemaAllows()
    {
        string[] allowed =
            ["operator_seed", "submission", "referral", "i3_mudlist", "ares_central", "backfill"];

        foreach (var source in Enum.GetValues<DiscoverySource>())
        {
            await Assert.That(allowed).Contains(DiscoverySources.ToDb(source));
        }

        await Assert.That(Enum.GetValues<DiscoverySource>().Length).IsEqualTo(allowed.Length);
    }
}
