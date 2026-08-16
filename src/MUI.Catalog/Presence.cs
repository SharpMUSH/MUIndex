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

    /// <summary>
    /// The game is on Intermud-3, is up, advertises <c>who</c>, was asked — and said nothing.
    /// </summary>
    /// <remarks>
    /// <b>Silence is not zero.</b> An empty <c>users</c> array is a mud answering that nobody is on,
    /// which is a measured zero and a filled cell; no answer at all is the middle state of §5.4 and
    /// has to say so. The two arrive down the same pipe and look alike in a debugger, which is
    /// exactly why they are different values here.
    ///
    /// There is no sibling reason for a mud that does not advertise <c>who</c>, because we never ask
    /// those and so write no row: not asking is a decision of ours about our manners, and §5.5 says a
    /// decision of ours is never recorded as a measurement of theirs.
    /// </remarks>
    I3NoReply,
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
/// Derived distributions that never contain a player name (spec §11).
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no unique-player estimate here, and there is not going to be one.</b> §11 once
/// promised one, on the strength of salted hashes with a rotating salt. The arithmetic does not
/// survive contact with the hobby: a player who renames — which every platform this site indexes
/// allows, and which MU* culture actively encourages — hashes to two values inside one epoch and is
/// counted twice. The overcount is unbounded and, worse, uncorrectable in principle, because
/// correcting it needs exactly the identity linkage across names that the salt existed to prevent.
/// </para>
/// <para>
/// A number like that published as a count of players would be our parser's limitation printed as a
/// fact about somebody's playerbase, which is rule 5. It was never produced — no probe ever built
/// one of these — so nothing measured was lost in removing it.
/// </para>
/// <para>
/// What remains is derived from <em>times</em> rather than from identities, which is why it survives
/// the same argument.
/// </para>
/// </remarks>
public sealed record PresenceAggregates
{
    public PresenceAggregates(IReadOnlyList<int> idleBuckets)
    {
        ArgumentNullException.ThrowIfNull(idleBuckets);

        IdleBuckets = idleBuckets;
    }

    /// <summary>Idle-time histogram buckets. Derived from times, not from names.</summary>
    public IReadOnlyList<int> IdleBuckets { get; }
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

    /// <summary>
    /// An unmeasurable reading that names the pipe it failed on.
    /// </summary>
    /// <remarks>
    /// The parameterless overload above answers with <see cref="FieldSource.Who"/> because for years
    /// there was one pipe and a row with no count came off it by definition. There are two now, and
    /// an I3 silence stored as <c>who</c> would say a game did not answer a telnet command nobody
    /// sent it — our own confusion, written into their record, which is exactly what §5.5 forbids.
    /// </remarks>
    public static PresenceReading Unmeasurable(UnmeasurableReason reason, FieldSource source) =>
        new(null, source, reason);
}

public enum PresenceOutcome
{
    /// <summary>A count was stored — including a measured zero.</summary>
    Counted,

    /// <summary>A row was stored with no count and a reason.</summary>
    RecordedUnmeasurable,
}
