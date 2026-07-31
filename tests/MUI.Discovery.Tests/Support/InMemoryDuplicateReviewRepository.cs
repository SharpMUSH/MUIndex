namespace MUI.Discovery.Tests.Support;

/// <summary>
/// Suspected-duplicate pairs in memory. The pair is unordered on the way in and stored ordered, which
/// is what makes "have we already opened this?" one lookup.
/// </summary>
public sealed class InMemoryDuplicateReviewRepository : IDuplicateReviewRepository
{
    private readonly List<DuplicateReview> _reviews = [];

    public IReadOnlyList<DuplicateReview> All => _reviews;

    public Task<Guid> OpenAsync(Guid a, Guid b, IdentityScore score, DateTimeOffset at, CancellationToken ct)
    {
        if (a == b)
        {
            throw new ArgumentException("A game cannot be a suspected duplicate of itself.", nameof(b));
        }

        var (left, right) = a.CompareTo(b) < 0 ? (a, b) : (b, a);

        var open = _reviews.FirstOrDefault(r => r.IsOpen && r.LeftGameId == left && r.RightGameId == right);
        if (open is not null)
        {
            // Reported on every probe of either endpoint until somebody resolves it; it must not
            // accumulate a row per probe.
            return Task.FromResult(open.Id);
        }

        var review = new DuplicateReview(
            Guid.CreateVersion7(), left, right, score.Score, IdentitySignals.ToJson(score.Signals), at, null, null);

        _reviews.Add(review);
        return Task.FromResult(review.Id);
    }

    public Task<IReadOnlyList<DuplicateReview>> OpenPairsForAsync(Guid gameId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DuplicateReview>>(
            _reviews.Where(r => r.IsOpen && (r.LeftGameId == gameId || r.RightGameId == gameId)).ToList());

    public Task ResolveAsync(Guid id, string resolution, DateTimeOffset at, CancellationToken ct)
    {
        var index = _reviews.FindIndex(r => r.Id == id && r.IsOpen);
        if (index >= 0)
        {
            // Resolved, never deleted: a pair somebody judged distinct and which turns up again is a
            // second row with its own history rather than a silent overwrite.
            _reviews[index] = _reviews[index] with { ResolvedAt = at, Resolution = resolution };
        }

        return Task.CompletedTask;
    }
}
