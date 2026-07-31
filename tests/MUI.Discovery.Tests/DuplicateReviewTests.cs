using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// §7.3's middle case: "both pages stay live and link to each other reciprocally, because a wrongly
/// hidden game is worse than a visible duplicate".
/// </summary>
public class DuplicateReviewTests
{
    private static readonly DateTimeOffset Then = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    private static IdentityScore Score(double value = 0.6) =>
        new(null, value, [new IdentitySignal("MsspNameAndCreated", 0.6, true)]);

    [Test]
    public async Task APairIsVisibleFromBothSidesAndEachSideNamesTheOther()
    {
        var reviews = new InMemoryDuplicateReviewRepository();
        var corvid = Guid.CreateVersion7();
        var maybe = Guid.CreateVersion7();

        await reviews.OpenAsync(corvid, maybe, Score(), Then, None);

        var fromLeft = (await reviews.OpenPairsForAsync(corvid, None)).Single();
        var fromRight = (await reviews.OpenPairsForAsync(maybe, None)).Single();

        await Assert.That(fromLeft.Id).IsEqualTo(fromRight.Id);
        await Assert.That(fromLeft.OtherThan(corvid)).IsEqualTo(maybe);
        await Assert.That(fromRight.OtherThan(maybe)).IsEqualTo(corvid);
    }

    [Test]
    public async Task NeitherGameIsHiddenOrRedirected()
    {
        // The whole point of the middle band, and it is a claim about what this type does *not* carry:
        // there is no state on a review that could archive, hide or redirect either side. If it had
        // one, a review would be an unreviewed merge wearing a review's name.
        var properties = typeof(DuplicateReview).GetProperties().Select(p => p.Name).ToList();
        var forbidden = new[] { "archiv", "hidden", "hide", "redirect", "merged", "suppress", "winner" };

        var offenders = properties
            .Where(name => forbidden.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        await Assert.That(offenders).IsEmpty();
    }

    [Test]
    public async Task TheOrderOfTheArgumentsDoesNotMatter()
    {
        var reviews = new InMemoryDuplicateReviewRepository();
        var corvid = Guid.CreateVersion7();
        var maybe = Guid.CreateVersion7();

        var first = await reviews.OpenAsync(corvid, maybe, Score(), Then, None);
        var second = await reviews.OpenAsync(maybe, corvid, Score(0.7), Then.AddDays(1), None);

        await Assert.That(second).IsEqualTo(first);
    }

    [Test]
    public async Task OpeningTheSamePairTwiceKeepsOneOpenRow()
    {
        // The pair is reported on every probe of either endpoint until somebody resolves it. It must
        // not accumulate a row per probe.
        var reviews = new InMemoryDuplicateReviewRepository();
        var corvid = Guid.CreateVersion7();
        var maybe = Guid.CreateVersion7();

        await reviews.OpenAsync(corvid, maybe, Score(), Then, None);
        await reviews.OpenAsync(maybe, corvid, Score(0.7), Then.AddDays(1), None);

        await Assert.That((await reviews.OpenPairsForAsync(corvid, None)).Count).IsEqualTo(1);
    }

    [Test]
    public async Task AResolvedPairStopsBeingOpenButIsKept()
    {
        var reviews = new InMemoryDuplicateReviewRepository();
        var corvid = Guid.CreateVersion7();
        var maybe = Guid.CreateVersion7();
        var id = await reviews.OpenAsync(corvid, maybe, Score(), Then, None);

        await reviews.ResolveAsync(id, "distinct", Then.AddDays(2), None);

        await Assert.That(await reviews.OpenPairsForAsync(corvid, None)).IsEmpty();
        await Assert.That(reviews.All.Count).IsEqualTo(1);
        await Assert.That(reviews.All.Single().Resolution).IsEqualTo("distinct");
    }

    [Test]
    public async Task APairJudgedDistinctAndTurningUpAgainIsASecondRowRatherThanASilentOverwrite()
    {
        var reviews = new InMemoryDuplicateReviewRepository();
        var corvid = Guid.CreateVersion7();
        var maybe = Guid.CreateVersion7();

        var first = await reviews.OpenAsync(corvid, maybe, Score(), Then, None);
        await reviews.ResolveAsync(first, "distinct", Then.AddDays(2), None);
        var second = await reviews.OpenAsync(corvid, maybe, Score(0.9), Then.AddYears(1), None);

        await Assert.That(second).IsNotEqualTo(first);
        await Assert.That(reviews.All.Count).IsEqualTo(2);
    }

    [Test]
    public async Task TheEvidenceIsCarriedSoAPersonCanJudgeIt()
    {
        var reviews = new InMemoryDuplicateReviewRepository();
        await reviews.OpenAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), Score(), Then, None);

        await Assert.That(reviews.All.Single().SignalsJson).Contains("MsspNameAndCreated");
    }

    [Test]
    public async Task AGameIsNeverPairedWithItself()
    {
        var reviews = new InMemoryDuplicateReviewRepository();
        var corvid = Guid.CreateVersion7();

        await Assert.That(async () => await reviews.OpenAsync(corvid, corvid, Score(), Then, None))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task AskingForTheCounterpartOfAGameThatIsNotInThePairIsAProgrammingError()
    {
        var review = new DuplicateReview(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 0.6, "[]", Then, null, null);

        await Assert.That(() => review.OtherThan(Guid.CreateVersion7())).Throws<ArgumentException>();
    }
}
