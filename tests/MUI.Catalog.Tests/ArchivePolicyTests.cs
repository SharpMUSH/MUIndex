using MUI.Catalog;

namespace MUI.Catalog.Tests;

/// <summary>
/// How long a game gets before it leaves the listing, and why the answer is not a constant.
/// These cases are the table in spec §7.5; if that table moves, this file is what says so.
/// </summary>
public class ArchivePolicyTests
{
    private static TimeSpan Days(double d) => TimeSpan.FromDays(d);

    [Test]
    public async Task AGameWeHaveBarelyMetGetsTheFloor()
    {
        var grace = ArchivePolicy.GraceFor(firstPartyReachable: Days(14));

        await Assert.That(grace).IsEqualTo(ArchivePolicy.Floor);
    }

    [Test]
    public async Task EightMonthsUpStillOnlyEarnsTheFloor()
    {
        // 240 / 4 = 60, which is exactly the floor — the first point where the formula starts to bite.
        var grace = ArchivePolicy.GraceFor(firstPartyReachable: Days(240));

        await Assert.That(grace).IsEqualTo(ArchivePolicy.Floor);
    }

    [Test]
    public async Task AYearUpEarnsAQuarterOfIt()
    {
        var grace = ArchivePolicy.GraceFor(firstPartyReachable: Days(365));

        await Assert.That(grace.TotalDays).IsEqualTo(91.25).Within(0.01);
    }

    [Test]
    public async Task TwoYearsUpEarnsHalfAYear()
    {
        var grace = ArchivePolicy.GraceFor(firstPartyReachable: Days(730));

        await Assert.That(grace.TotalDays).IsEqualTo(182.5).Within(0.01);
    }

    [Test]
    public async Task ADecadeUpIsCappedAtTheCeiling()
    {
        var grace = ArchivePolicy.GraceFor(firstPartyReachable: Days(3650));

        await Assert.That(grace).IsEqualTo(ArchivePolicy.Ceiling);
    }

    [Test]
    public async Task ImportedHistoryCountsForHalf()
    {
        // Four years of somebody else's probing is credited as two of ours.
        var imported = ArchivePolicy.GraceFor(firstPartyReachable: TimeSpan.Zero, importedMeasuredReachable: Days(1460));
        var firstParty = ArchivePolicy.GraceFor(firstPartyReachable: Days(730));

        await Assert.That(imported).IsEqualTo(firstParty);
    }

    [Test]
    public async Task ClaimingAGameEarnsTheCeilingOutright()
    {
        // Someone with server access has staked a claim; how long we happen to have been watching
        // stops being the question.
        var grace = ArchivePolicy.GraceFor(firstPartyReachable: Days(1), isClaimed: true);

        await Assert.That(grace).IsEqualTo(ArchivePolicy.Ceiling);
    }

    [Test]
    public async Task AGameDarkForLessThanItsGraceStaysInTheListing()
    {
        var shouldArchive = ArchivePolicy.ShouldArchive(darkFor: Days(120), firstPartyReachable: Days(730));

        await Assert.That(shouldArchive).IsFalse();
    }

    [Test]
    public async Task AGameDarkPastItsGraceIsArchived()
    {
        var shouldArchive = ArchivePolicy.ShouldArchive(darkFor: Days(200), firstPartyReachable: Days(730));

        await Assert.That(shouldArchive).IsTrue();
    }

    [Test]
    public async Task TheSameOutageArchivesAYoungGameAndSparesAnOldOne()
    {
        // The whole point of tiering: 100 days dark is fatal to a newcomer and survivable for an
        // institution. A constant threshold cannot express this.
        var young = ArchivePolicy.ShouldArchive(darkFor: Days(100), firstPartyReachable: Days(30));
        var old = ArchivePolicy.ShouldArchive(darkFor: Days(100), firstPartyReachable: Days(3650));

        await Assert.That(young).IsTrue();
        await Assert.That(old).IsFalse();
    }

    [Test]
    public async Task NegativeReachableTimeIsRefusedRatherThanQuietlyClamped()
    {
        await Assert.That(() => ArchivePolicy.GraceFor(firstPartyReachable: Days(-1)))
            .Throws<ArgumentOutOfRangeException>();
    }
}
