using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// The operator surface spec §7.3 promised and never grew: <c>duplicate_review</c> accumulates and
/// <see cref="MergeApplier"/> can act, but nothing before this stood between them. This is that thing —
/// resolve one pair by hand, winner and loser named by a person, logged exactly as an automatic merge
/// would be.
/// </summary>
public class ReviewMergeServiceTests
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
    }

    /// <summary>
    /// A <see cref="IDuplicateReviewRepository"/> decorator whose <see cref="ResolveAsync"/> always
    /// throws, standing in for a dropped connection or a cancelled request between the two writes
    /// <c>ReviewMergeService.MergeAsync</c> makes.
    /// </summary>
    private sealed class ResolveThrowsReviewRepository(IDuplicateReviewRepository inner) : IDuplicateReviewRepository
    {
        public Task<Guid> OpenAsync(Guid a, Guid b, IdentityScore score, DateTimeOffset at, CancellationToken ct) =>
            inner.OpenAsync(a, b, score, at, ct);

        public Task<IReadOnlyList<DuplicateReview>> OpenPairsForAsync(Guid gameId, CancellationToken ct) =>
            inner.OpenPairsForAsync(gameId, ct);

        public Task ResolveAsync(
            Guid id, string resolution, DateTimeOffset at, CancellationToken ct, IUnitOfWork? unitOfWork = null) =>
            throw new InvalidOperationException("Simulated failure resolving the review.");
    }

    /// <summary>
    /// An <see cref="IMergeLog"/> whose <see cref="RecordAsync"/> always throws
    /// <see cref="MergeAlreadyAbsorbedException"/>, standing in for what
    /// <c>NpgsqlMergeLog.RecordAsync</c> throws once it recognises
    /// <c>merge_log_absorbed_once_idx</c> refusing the insert.
    /// </summary>
    private sealed class AlreadyAbsorbedMergeLog : IMergeLog
    {
        public Task<Guid> RecordAsync(MergeRecord record, CancellationToken ct, IUnitOfWork? unitOfWork = null) =>
            throw new MergeAlreadyAbsorbedException(
                "duplicate key value violates unique constraint \"merge_log_absorbed_once_idx\"");

        public Task RevertAsync(Guid mergeId, DateTimeOffset at, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MergeRecord>> ForGameAsync(Guid gameId, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    [Test]
    public async Task AnOpenReviewIsResolvedAndItsSignalsCarryOntoTheMergeLog()
    {
        var world = new World();
        var winner = world.Games.Add();
        var loser = world.Games.Add();

        var score = new IdentityScore(loser, 0.5, [new IdentitySignal("BannerHash", 0.5, true)]);
        var reviewId = await world.Reviews.OpenAsync(winner, loser, score, world.Time.GetUtcNow(), None);

        var verdict = await world.Service.MergeAsync(winner, loser, "confirmed: same connect screen", None);

        await Assert.That(verdict).IsTypeOf<MergeVerdict.Merged>();
        var result = ((MergeVerdict.Merged)verdict).Result;
        await Assert.That(result.ResolvedReviewId).IsEqualTo(reviewId);
        await Assert.That(world.Merges.RedirectedTo[loser]).IsEqualTo(winner);

        var record = world.Merges.All.Single();
        await Assert.That(record.SignalsJson).Contains("BannerHash");
        await Assert.That(record.Score).IsEqualTo(0.5);

        var review = world.Reviews.All.Single();
        await Assert.That(review.IsOpen).IsFalse();
        await Assert.That(review.Resolution).Contains("confirmed: same connect screen");
    }

    [Test]
    public async Task TheReviewIsFoundRegardlessOfWhichSideIsNamedWinner()
    {
        // duplicate_review stores the pair unordered (left < right); an operator names winner and loser
        // in whichever order they judged, and the lookup must not depend on getting that order right.
        var world = new World();
        var winner = world.Games.Add();
        var loser = world.Games.Add();

        var score = new IdentityScore(loser, 0.5, [new IdentitySignal("BannerHash", 0.5, true)]);
        await world.Reviews.OpenAsync(loser, winner, score, world.Time.GetUtcNow(), None);

        var verdict = await world.Service.MergeAsync(winner, loser, "same game", None);

        await Assert.That(verdict).IsTypeOf<MergeVerdict.Merged>();
        await Assert.That(((MergeVerdict.Merged)verdict).Result.ResolvedReviewId).IsNotNull();
    }

    [Test]
    public async Task APairWithNoOpenReviewCanStillBeMergedByHand()
    {
        // Migration 0018: "a merge an operator made by hand records the score it was made on and no
        // signals." An operator may spot a duplicate the matcher never flagged at all.
        var world = new World();
        var winner = world.Games.Add();
        var loser = world.Games.Add();

        var verdict = await world.Service.MergeAsync(winner, loser, "obviously the same game, I run both", None);

        await Assert.That(verdict).IsTypeOf<MergeVerdict.Merged>();
        await Assert.That(((MergeVerdict.Merged)verdict).Result.ResolvedReviewId).IsNull();
        await Assert.That(world.Merges.RedirectedTo[loser]).IsEqualTo(winner);

        var record = world.Merges.All.Single();
        await Assert.That(record.SignalsJson).IsEqualTo("[]");
    }

    /// <summary>
    /// The operator's own words must survive onto the log even when no review folded them in.
    /// </summary>
    [Test]
    public async Task TheReasonReachesTheMergeLogWithNoOpenReview()
    {
        var world = new World();
        var winner = world.Games.Add();
        var loser = world.Games.Add();

        await world.Service.MergeAsync(winner, loser, "obviously the same game, I run both", None);

        await Assert.That(world.Merges.All.Single().Reason)
            .IsEqualTo("obviously the same game, I run both");
    }

    [Test]
    public async Task TheReasonReachesTheMergeLogWithAnOpenReviewToo()
    {
        var world = new World();
        var winner = world.Games.Add();
        var loser = world.Games.Add();

        var score = new IdentityScore(loser, 0.5, [new IdentitySignal("BannerHash", 0.5, true)]);
        await world.Reviews.OpenAsync(winner, loser, score, world.Time.GetUtcNow(), None);

        await world.Service.MergeAsync(winner, loser, "confirmed: same connect screen", None);

        await Assert.That(world.Merges.All.Single().Reason).IsEqualTo("confirmed: same connect screen");
    }

    [Test]
    public async Task OnlyTheNamedPairsReviewIsResolvedNotEveryOpenReviewOnEitherGame()
    {
        var world = new World();
        var winner = world.Games.Add();
        var loser = world.Games.Add();
        var bystander = world.Games.Add();

        var pairScore = new IdentityScore(loser, 0.5, []);
        var reviewId = await world.Reviews.OpenAsync(winner, loser, pairScore, world.Time.GetUtcNow(), None);

        var unrelatedScore = new IdentityScore(bystander, 0.45, []);
        var unrelatedReviewId =
            await world.Reviews.OpenAsync(winner, bystander, unrelatedScore, world.Time.GetUtcNow(), None);

        await world.Service.MergeAsync(winner, loser, "same game", None);

        await Assert.That(world.Reviews.All.Single(r => r.Id == reviewId).IsOpen).IsFalse();
        await Assert.That(world.Reviews.All.Single(r => r.Id == unrelatedReviewId).IsOpen).IsTrue();
    }

    [Test]
    public async Task AGameCannotBeMergedIntoItselfAndThatIsAVerdictNotAThrow()
    {
        var world = new World();
        var game = world.Games.Add();

        var verdict = await world.Service.MergeAsync(game, game, "typo", None);

        await Assert.That(verdict).IsTypeOf<MergeVerdict.SelfMerge>();
    }

    [Test]
    public async Task TheWinnerMustExist()
    {
        var world = new World();
        var loser = world.Games.Add();
        var ghost = Guid.CreateVersion7();

        var verdict = await world.Service.MergeAsync(ghost, loser, "typo", None);

        await Assert.That(verdict).IsTypeOf<MergeVerdict.UnknownGame>();
        await Assert.That(((MergeVerdict.UnknownGame)verdict).Id).IsEqualTo(ghost);
    }

    [Test]
    public async Task TheLoserMustExist()
    {
        var world = new World();
        var winner = world.Games.Add();
        var ghost = Guid.CreateVersion7();

        var verdict = await world.Service.MergeAsync(winner, ghost, "typo", None);

        await Assert.That(verdict).IsTypeOf<MergeVerdict.UnknownGame>();
        await Assert.That(((MergeVerdict.UnknownGame)verdict).Id).IsEqualTo(ghost);
    }

    /// <summary>
    /// merge_log_absorbed_once_idx refusing the insert (migration 0018) is a schema guard, not this
    /// service's own logic -- so this proves MergeAsync recognises the Npgsql-agnostic exception the
    /// persistence layer translates it into (NpgsqlMergeLog.RecordAsync), rather than proving the
    /// constraint itself, which ReviewMergePostgresTests and MergeLogPostgresTests do against real
    /// Postgres.
    /// </summary>
    [Test]
    public async Task ALoserAlreadyAbsorbedElsewhereIsANamedVerdictNotAThrow()
    {
        var world = new World();
        var winner = world.Games.Add();
        var loser = world.Games.Add();

        var service = new ReviewMergeService(
            world.Games,
            world.Reviews,
            new MergeApplier(world.Endpoints, world.Fields, new AlreadyAbsorbedMergeLog(), world.Time),
            world.Merges,
            world.Time,
            world.UnitOfWorks);

        var verdict = await service.MergeAsync(winner, loser, "already absorbed elsewhere", None);

        await Assert.That(verdict).IsTypeOf<MergeVerdict.AlreadyAbsorbed>();
        await Assert.That(((MergeVerdict.AlreadyAbsorbed)verdict).DatabaseMessage)
            .Contains("merge_log_absorbed_once_idx");
    }

    /// <summary>
    /// merge_log_no_chains is DEFERRABLE INITIALLY DEFERRED, so the real violation surfaces at commit,
    /// not at the insert -- <see cref="InMemoryUnitOfWork.ThrowOnCommit"/> exists to simulate exactly
    /// that timing, and this proves MergeAsync's own commit-time catch, the counterpart to
    /// <see cref="ALoserAlreadyAbsorbedElsewhereIsANamedVerdictNotAThrow"/>'s insert-time one.
    /// </summary>
    [Test]
    public async Task ARedirectChainIsANamedVerdictNotAThrowAndNothingLands()
    {
        var world = new World();
        var winner = world.Games.Add();
        var loser = world.Games.Add();
        world.UnitOfWorks.ThrowOnCommit = new MergeWouldChainException("simulated: merge_log_no_chains");

        var verdict = await world.Service.MergeAsync(winner, loser, "would form a chain", None);

        await Assert.That(verdict).IsTypeOf<MergeVerdict.RedirectChain>();
        await Assert.That(((MergeVerdict.RedirectChain)verdict).DatabaseMessage)
            .Contains("merge_log_no_chains");

        // The merge_log insert was buffered behind the unit of work whose commit then failed -- the
        // same "nothing landed" property AFailureResolvingTheReviewRollsBackTheMergeToo proves for the
        // review-resolve failure.
        await Assert.That(world.Merges.All).IsEmpty();
    }

    /// <summary>
    /// The gap #117 tracked: the merge_log insert and the duplicate_review resolve are two writes with
    /// nothing tying them together, so a failure between them could record a merge while leaving its
    /// review open forever -- merge_log_absorbed_once_idx then refuses the retry that would "finish the
    /// job". Both now join one IUnitOfWork, so a failure on the second write must roll back the first.
    /// </summary>
    [Test]
    public async Task AFailureResolvingTheReviewRollsBackTheMergeToo()
    {
        var world = new World();
        var winner = world.Games.Add();
        var loser = world.Games.Add();

        var score = new IdentityScore(loser, 0.5, [new IdentitySignal("BannerHash", 0.5, true)]);
        await world.Reviews.OpenAsync(winner, loser, score, world.Time.GetUtcNow(), None);

        var breakingService = new ReviewMergeService(
            world.Games,
            new ResolveThrowsReviewRepository(world.Reviews),
            new MergeApplier(world.Endpoints, world.Fields, world.Merges, world.Time),
            world.Merges,
            world.Time,
            world.UnitOfWorks);

        await Assert.That(async () => await breakingService.MergeAsync(winner, loser, "should roll back", None))
            .Throws<InvalidOperationException>();

        // Neither write landed: the merge_log insert was buffered behind the same unit of work as the
        // review resolve that then failed, and an uncommitted unit of work applies nothing.
        await Assert.That(world.Merges.All).IsEmpty();
        await Assert.That(world.Reviews.All.Single().IsOpen).IsTrue();
    }

    /// <summary>
    /// A merge answers more than the one pair it names. If A already absorbed B and C is now judged to
    /// be A as well, the open B–C pair is asking whether two halves of one listing are the same thing,
    /// and nobody would ever think to go and look at it — so the merge closes it.
    /// </summary>
    [Test]
    public async Task AMergeClosesThePairsItLeavesNothingToJudge()
    {
        var world = new World();
        var survivor = world.Games.Add();
        var first = world.Games.Add();
        var second = world.Games.Add();

        await world.Service.MergeAsync(survivor, first, "same game, two addresses", None);

        var score = new IdentityScore(second, 0.5, [new IdentitySignal("BannerHash", 0.5, true)]);
        var strandedId = await world.Reviews.OpenAsync(first, second, score, world.Time.GetUtcNow(), None);
        var namedId = await world.Reviews.OpenAsync(survivor, second, score, world.Time.GetUtcNow(), None);

        var verdict = await world.Service.MergeAsync(survivor, second, "same game again", None);

        var result = ((MergeVerdict.Merged)verdict).Result;
        await Assert.That(result.ResolvedReviewId).IsEqualTo(namedId);
        await Assert.That(result.MootReviewsResolved).IsEqualTo(1);

        var stranded = world.Reviews.All.Single(r => r.Id == strandedId);
        await Assert.That(stranded.IsOpen).IsFalse();
        await Assert.That(stranded.Resolution).Contains("moot");
    }

    /// <summary>
    /// A pair that still has two sides is left alone. The sweep must not become "close everything
    /// touching either game".
    /// </summary>
    [Test]
    public async Task AnUnrelatedPairIsLeftOpen()
    {
        var world = new World();
        var winner = world.Games.Add();
        var loser = world.Games.Add();
        var stranger = world.Games.Add();

        var score = new IdentityScore(stranger, 0.5, [new IdentitySignal("BannerHash", 0.5, true)]);
        var openId = await world.Reviews.OpenAsync(winner, stranger, score, world.Time.GetUtcNow(), None);

        var verdict = await world.Service.MergeAsync(winner, loser, "same game", None);

        await Assert.That(((MergeVerdict.Merged)verdict).Result.MootReviewsResolved).IsEqualTo(0);
        await Assert.That(world.Reviews.All.Single(r => r.Id == openId).IsOpen).IsTrue();
    }
}
