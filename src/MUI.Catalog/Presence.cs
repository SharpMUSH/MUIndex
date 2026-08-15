namespace MUI.Catalog;

/// <summary>
/// Why a probe that <em>succeeded</em> could still not produce a player count. The existence of this
/// type is the point: without it, "we got in and could not count" is indistinguishable from "we never
/// got in", and the heatmap renders a healthy game as permanently dark (spec §5.4).
/// </summary>
public enum UnmeasurableReason
{
    /// <summary>The WHO response could not be read structurally and MSSP carried no PLAYERS.</summary>
    WhoUnparseable,

    /// <summary>WHO was never attempted — the game answers no pre-login WHO.</summary>
    WhoNotOffered,

    /// <summary>MSSP declared PLAYERS but the value was not a number.</summary>
    PlayersNotNumeric,
}

/// <summary>
/// One presence observation (spec §5.2). <see cref="Count"/> is nullable and that is load-bearing.
/// </summary>
/// <remarks>
/// Three states, not two:
/// <list type="bullet">
/// <item><c>Count = n</c> — probed and counted. A measured <b>zero</b> is a real fact: we got in and
/// nobody was there.</item>
/// <item><c>Count = null</c> with a <see cref="Reason"/> — probed, not countable.</item>
/// <item><b>No row at all</b> — the probe failed, and the availability series carries that instead.</item>
/// </list>
/// </remarks>
public sealed record PresenceSample
{
    public required Guid GameId { get; init; }

    public required DateTimeOffset At { get; init; }

    public int? Count { get; init; }

    public required FieldSource Source { get; init; }

    public UnmeasurableReason? Reason { get; init; }

    /// <summary>Anonymised aggregates (spec §11), present only at per-player parse confidence.</summary>
    public PresenceAggregates? Aggregates { get; init; }

    public bool IsCounted => Count is not null;

    public static PresenceSample Counted(Guid gameId, DateTimeOffset at, int count, FieldSource source) =>
        new() { GameId = gameId, At = at, Count = count, Source = source };

    public static PresenceSample Unmeasurable(Guid gameId, DateTimeOffset at, UnmeasurableReason reason) =>
        new() { GameId = gameId, At = at, Count = null, Source = FieldSource.Who, Reason = reason };
}

/// <summary>
/// Derived distributions that never contain a player name (spec §11). Names are hashed with a
/// rotating salt in memory and discarded; only these survive.
/// </summary>
/// <remarks>
/// <para>
/// <b>An estimate must name its epoch, and the type refuses one that does not.</b> §11 promises that
/// a unique-player estimate stays possible while re-identification across salt epochs does not, and
/// that promise is only keepable if every consumer downstream can tell which numbers may be compared
/// with which. An estimate with no epoch is one nothing can safely do anything with, and the place to
/// catch it is here rather than in whichever surface eventually adds two of them together.
/// </para>
/// <para>
/// Written out rather than positional so that the check runs on every way of making one — including
/// the constructor JSON deserialisation uses, so a hand-edited <c>aggregates</c> column cannot
/// reintroduce an unlabelled estimate on the way back in.
/// </para>
/// </remarks>
public sealed record PresenceAggregates
{
    public PresenceAggregates(
        IReadOnlyList<int> idleBuckets,
        int? distinctEstimate,
        string? saltEpoch = null)
    {
        ArgumentNullException.ThrowIfNull(idleBuckets);

        if (distinctEstimate is not null && string.IsNullOrWhiteSpace(saltEpoch))
        {
            throw new ArgumentException(
                "A unique-player estimate must name the salt epoch it was computed under (spec §11): "
                + "without it, nothing downstream can tell which estimates may be compared and which "
                + "span a rotation.",
                nameof(saltEpoch));
        }

        IdleBuckets = idleBuckets;
        DistinctEstimate = distinctEstimate;
        SaltEpoch = saltEpoch;
    }

    /// <summary>Idle-time histogram buckets. Derived from times, not from names.</summary>
    public IReadOnlyList<int> IdleBuckets { get; }

    /// <summary>
    /// How many distinct players one sample's hashes came to. Meaningful only within
    /// <see cref="SaltEpoch"/>.
    /// </summary>
    public int? DistinctEstimate { get; }

    /// <summary>Which salt the hashes behind <see cref="DistinctEstimate"/> were taken under.</summary>
    public string? SaltEpoch { get; }
}

public interface IPresenceStore
{
    Task AppendAsync(PresenceSample sample, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PresenceSample>> ForGameAsync(
        Guid gameId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Decides which of the three presence states a probe produced, and writes at most one row.
/// </summary>
public interface IPresenceWriter
{
    /// <summary>
    /// Records presence for a probe that <b>answered</b>. Never call this for a failed probe or a
    /// refused dial — both must reach the availability series instead, and neither has a count to
    /// record even as unknown.
    /// </summary>
    Task<PresenceOutcome> WriteAsync(
        Guid gameId,
        PresenceReading reading,
        DateTimeOffset at,
        CancellationToken cancellationToken = default);
}

/// <summary>What a probe yielded about player count, in terms the writer can act on.</summary>
public sealed record PresenceReading(
    int? Count,
    FieldSource Source,
    UnmeasurableReason? Reason = null,
    PresenceAggregates? Aggregates = null)
{
    public static PresenceReading Counted(int count, FieldSource source) => new(count, source);

    public static PresenceReading Unmeasurable(UnmeasurableReason reason) =>
        new(null, FieldSource.Who, reason);
}

public enum PresenceOutcome
{
    /// <summary>A count was stored — including a measured zero.</summary>
    Counted,

    /// <summary>A row was stored with no count and a reason.</summary>
    RecordedUnmeasurable,
}
