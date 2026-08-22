using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// The other verdict a person can reach about a suspected-duplicate pair: these are two games
/// (spec §7.3).
/// </summary>
/// <remarks>
/// <para>
/// <b>Without this the queue only ever grows.</b> <see cref="ReviewMergeService.MergeAsync"/> could act
/// on a pair that turned out to be one game and there was nothing at all to do about a pair that did
/// not — the row stayed open for ever. On 2026-08-21 that was thirty-one of the sixty-one rows open in
/// production, every one of them correct to leave unmerged and impossible to clear: a stock connect
/// screen, or one operator's contact address across the four games they run. A queue whose false
/// positives cannot be cleared stops being read, and then the true positives in it stop being acted on.
/// </para>
/// <para>
/// Recording it is the entire act. Nothing about either game moves, because nothing about either game
/// was ever wrong.
/// </para>
/// </remarks>
public class KeepDistinctTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private sealed class World
    {
        public InMemoryGameDirectory Games { get; } = new();

        public InMemoryEndpointDirectory Endpoints { get; } = new();

        public InMemoryGameFieldStore Fields { get; } = new();

        public InMemoryMergeLog Merges { get; } = new();

        public InMemoryDuplicateReviewRepository Reviews { get; } = new();

        public ManualTimeProvider Time { get; } = new();

        public InMemoryUnitOfWorkFactory UnitOfWorks { get; } = new();

        public ReviewMergeService Service =>
            new(Games, Reviews, new MergeApplier(Endpoints, Fields, Merges, Time), Merges, Time, UnitOfWorks);

        public Task<Guid> OpenAsync(Guid a, Guid b, double score = 0.5) =>
            Reviews.OpenAsync(
                a, b, new IdentityScore(b, score, [new IdentitySignal("BannerHash", 0.5, true)]),
                Time.GetUtcNow(), None);
    }

    [Test]
    public async Task TheReviewIsClosedAndTheReasonIsKeptBesideIt()
    {
        var world = new World();
        var rhost = world.Games.Add();
        var alsoRhost = world.Games.Add();
        var reviewId = await world.OpenAsync(rhost, alsoRhost);

        var verdict = await world.Service.KeepDistinctAsync(
            rhost, alsoRhost, "stock RhostMUSH connect screen; different games on one host", None);

        await Assert.That(verdict).IsTypeOf<DistinctVerdict.Kept>();
        await Assert.That(((DistinctVerdict.Kept)verdict).ReviewId).IsEqualTo(reviewId);

        var review = world.Reviews.All.Single();
        await Assert.That(review.IsOpen).IsFalse();
        await Assert.That(review.Resolution).Contains("kept distinct");
        await Assert.That(review.Resolution).Contains("stock RhostMUSH connect screen");
    }

    [Test]
    public async Task NeitherGameIsTouched()
    {
        // The whole difference from a merge: no redirect, no endpoint moved, no field written. A pair
        // judged distinct is the state the catalogue was already in, and saying so must not change it.
        var world = new World();
        var first = world.Games.Add();
        var second = world.Games.Add();
        await world.OpenAsync(first, second);

        await world.Service.KeepDistinctAsync(first, second, "one operator, two games", None);

        await Assert.That(world.Merges.All).IsEmpty();
        await Assert.That(world.Merges.RedirectedTo).IsEmpty();
        await Assert.That(world.Fields.Changes).IsEmpty();
    }

    [Test]
    public async Task TheOrderOfTheTwoSidesDoesNotMatter()
    {
        // duplicate_review stores the pair unordered, and unlike a merge there is no winner to get
        // right, so an operator naming them the other way round must land on the same row.
        var world = new World();
        var first = world.Games.Add();
        var second = world.Games.Add();
        var reviewId = await world.OpenAsync(first, second);

        var verdict = await world.Service.KeepDistinctAsync(second, first, "two games", None);

        await Assert.That(((DistinctVerdict.Kept)verdict).ReviewId).IsEqualTo(reviewId);
    }

    [Test]
    public async Task TheScoreThatWasJudgedIsReported()
    {
        var world = new World();
        var first = world.Games.Add();
        var second = world.Games.Add();
        await world.OpenAsync(first, second, score: 0.65);

        var verdict = await world.Service.KeepDistinctAsync(first, second, "two games", None);

        await Assert.That(((DistinctVerdict.Kept)verdict).Score).IsEqualTo(0.65);
    }

    [Test]
    public async Task APairWithNoOpenReviewIsNotWrittenDown()
    {
        // Deliberately unlike a merge, which is worth recording whether or not the matcher ever flagged
        // the pair. "These two are different games" is what the catalogue already says about every pair
        // it has never been asked about; a row asserting it against no review is one nothing reads.
        var world = new World();
        var first = world.Games.Add();
        var second = world.Games.Add();

        var verdict = await world.Service.KeepDistinctAsync(first, second, "two games", None);

        await Assert.That(verdict).IsTypeOf<DistinctVerdict.NoOpenReview>();
        await Assert.That(world.Reviews.All).IsEmpty();
    }

    [Test]
    public async Task AnAlreadyResolvedReviewIsNotReopenedToCloseItAgain()
    {
        var world = new World();
        var first = world.Games.Add();
        var second = world.Games.Add();
        var reviewId = await world.OpenAsync(first, second);
        await world.Reviews.ResolveAsync(reviewId, "kept distinct: settled already", world.Time.GetUtcNow(), None);

        var verdict = await world.Service.KeepDistinctAsync(first, second, "two games", None);

        await Assert.That(verdict).IsTypeOf<DistinctVerdict.NoOpenReview>();
        await Assert.That(world.Reviews.All.Single().Resolution).IsEqualTo("kept distinct: settled already");
    }

    [Test]
    public async Task APairAlreadyMergedIsRefused()
    {
        // The catalogue would otherwise assert both at once: one listing, by a merge still in force, and
        // two games, by this. Reverting the merge is a decision for the operator to make first, and is
        // deliberately not made from here.
        var world = new World();
        var winner = world.Games.Add();
        var loser = world.Games.Add();
        await world.Service.MergeAsync(winner, loser, "same game", None);
        var reviewId = await world.OpenAsync(winner, loser);

        var verdict = await world.Service.KeepDistinctAsync(winner, loser, "changed my mind", None);

        await Assert.That(verdict).IsTypeOf<DistinctVerdict.AlreadyOneListing>();
        await Assert.That(((DistinctVerdict.AlreadyOneListing)verdict).Listing).IsEqualTo(winner);
        await Assert.That(world.Reviews.All.Single(r => r.Id == reviewId).IsOpen).IsTrue();
    }

    [Test]
    public async Task TwoGamesAbsorbedIntoOneThirdAreAlsoRefused()
    {
        // Neither is the other's winner, but both redirect to the same page, so they are one listing all
        // the same. Matching on the pair alone would miss this.
        var world = new World();
        var survivor = world.Games.Add();
        var first = world.Games.Add();
        var second = world.Games.Add();
        await world.Service.MergeAsync(survivor, first, "same game", None);
        await world.Service.MergeAsync(survivor, second, "same game", None);
        await world.OpenAsync(first, second);

        var verdict = await world.Service.KeepDistinctAsync(first, second, "surely not", None);

        await Assert.That(verdict).IsTypeOf<DistinctVerdict.AlreadyOneListing>();
        await Assert.That(((DistinctVerdict.AlreadyOneListing)verdict).Listing).IsEqualTo(survivor);
    }

    [Test]
    public async Task ARevertedMergeStopsRefusing()
    {
        var world = new World();
        var winner = world.Games.Add();
        var loser = world.Games.Add();
        await world.Service.MergeAsync(winner, loser, "same game", None);
        await world.Merges.RevertAsync(world.Merges.All.Single().Id, world.Time.GetUtcNow(), None);
        await world.OpenAsync(winner, loser);

        var verdict = await world.Service.KeepDistinctAsync(winner, loser, "two games after all", None);

        await Assert.That(verdict).IsTypeOf<DistinctVerdict.Kept>();
    }

    [Test]
    public async Task NamingOneGameTwiceIsRefused()
    {
        var world = new World();
        var only = world.Games.Add();

        await Assert.That(await world.Service.KeepDistinctAsync(only, only, "two games", None))
            .IsTypeOf<DistinctVerdict.SameGame>();
    }

    [Test]
    public async Task AGameThatIsNotThereIsRefused()
    {
        var world = new World();
        var real = world.Games.Add();
        var imaginary = Guid.NewGuid();

        var verdict = await world.Service.KeepDistinctAsync(real, imaginary, "two games", None);

        await Assert.That(verdict).IsTypeOf<DistinctVerdict.UnknownGame>();
        await Assert.That(((DistinctVerdict.UnknownGame)verdict).Id).IsEqualTo(imaginary);
    }

    [Test]
    public async Task AJudgementWithNoReasonIsRefusedBeforeAnythingIsWritten()
    {
        // The same rule --opt-out, --release, --mint-now, --merge and --rename all carry: a judgement
        // nobody wrote down beside the row is one nobody can review later.
        var world = new World();
        var first = world.Games.Add();
        var second = world.Games.Add();
        await world.OpenAsync(first, second);

        await Assert.That(async () => await world.Service.KeepDistinctAsync(first, second, "  ", None))
            .Throws<ArgumentException>();

        await Assert.That(world.Reviews.All.Single().IsOpen).IsTrue();
    }

    [Test]
    public async Task TheSecondOfTwoJudgementsIsToldItsReasonDidNotLand()
    {
        // duplicate_review's UPDATE carries `resolved_at IS NULL`, so the first judgement stands and the
        // second stores nothing. Reporting Kept for the second would tell an operator their reasoning is
        // on a row it never reached -- the same rule as "never record a decision of ours as a
        // measurement of theirs", pointed at ourselves.
        var world = new World();
        var first = world.Games.Add();
        var second = world.Games.Add();
        await world.OpenAsync(first, second);

        var mine = await world.Service.KeepDistinctAsync(first, second, "stock connect screen", None);
        var theirs = await world.Service.KeepDistinctAsync(first, second, "different operators", None);

        await Assert.That(mine).IsTypeOf<DistinctVerdict.Kept>();
        await Assert.That(theirs).IsTypeOf<DistinctVerdict.NoOpenReview>();
        await Assert.That(world.Reviews.All.Single().Resolution).Contains("stock connect screen");
    }
}
