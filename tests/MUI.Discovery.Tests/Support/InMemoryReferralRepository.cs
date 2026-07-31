namespace MUI.Discovery.Tests.Support;

/// <summary>
/// The referral graph in memory. Edges are provenance, so this has exactly two jobs: never lose one,
/// and be able to walk from a source to everything it ever pointed at (spec §7.1, §7.2).
/// </summary>
public sealed class InMemoryReferralRepository : IReferralRepository
{
    private readonly Dictionary<(Guid From, string Host, int Port), ReferralEdge> _edges = new();

    /// <summary>Which game a referred address turned out to be, once it answered for itself.</summary>
    /// <remarks>
    /// The subtree walk needs this arrow: an edge names an address, and the next hop's edges are keyed
    /// by the game id that address became. A registry-backed implementation reads it off
    /// <c>crawl_target</c>; here a test states it.
    /// </remarks>
    public Dictionary<(string Host, int Port), Guid> Attributed { get; } = [];

    public IReadOnlyCollection<ReferralEdge> All => _edges.Values.ToList();

    public Task<IReadOnlyList<ReferralEdge>> FromGameAsync(Guid gameId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ReferralEdge>>(
            _edges.Values.Where(e => e.FromGameId == gameId).ToList());

    public Task UpsertAsync(ReferralEdge edge, CancellationToken ct)
    {
        var key = (edge.FromGameId, edge.ToHost, edge.ToPort);

        // FirstSeenAt is kept: when we first saw an edge is a fact that a later sighting cannot change.
        _edges[key] = _edges.TryGetValue(key, out var existing)
            ? existing with { LastSeenAt = edge.LastSeenAt, Present = edge.Present }
            : edge;

        return Task.CompletedTask;
    }

    public Task MarkAbsentAsync(
        Guid fromGameId,
        IReadOnlyCollection<(string Host, int Port)> stillPresent,
        DateTimeOffset at,
        CancellationToken ct)
    {
        foreach (var (key, edge) in _edges.ToList())
        {
            if (edge.FromGameId == fromGameId && edge.Present && !stillPresent.Contains((edge.ToHost, edge.ToPort)))
            {
                // Present goes false and nothing else moves: LastSeenAt means "last seen", not "last
                // looked", and the row is kept for ever.
                _edges[key] = edge with { Present = false };
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ReferralEdge>> SubtreeOfAsync(Guid rootGameId, CancellationToken ct)
    {
        var found = new List<ReferralEdge>();
        var seen = new HashSet<Guid> { rootGameId };
        var queue = new Queue<Guid>();
        queue.Enqueue(rootGameId);

        while (queue.Count > 0)
        {
            var from = queue.Dequeue();

            foreach (var edge in _edges.Values.Where(e => e.FromGameId == from).ToList())
            {
                found.Add(edge);

                // `seen` is what stops a cycle — A referring B referring A is a shape a hostile list
                // can produce for free, and a trace that hangs is a trace nobody runs.
                if (Attributed.TryGetValue((edge.ToHost, edge.ToPort), out var next) && seen.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return Task.FromResult<IReadOnlyList<ReferralEdge>>(found);
    }
}
