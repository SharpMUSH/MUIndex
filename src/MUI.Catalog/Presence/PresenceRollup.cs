namespace MUI.Catalog;

/// <summary>How coarse a rolled-up presence bucket is (spec §5.2).</summary>
public enum PresenceGrain
{
    /// <summary>One hour. What §9's day-of-week × hour heatmap is drawn from.</summary>
    Hour,

    /// <summary>One day. The grain §5.2 keeps for ever.</summary>
    Day,
}

/// <summary>
/// What one bucket of presence samples added up to (spec §5.2), with §5.4's three states intact.
/// </summary>
/// <remarks>
/// <see cref="CountedSamples"/> and <see cref="UnmeasurableSamples"/> are counts of probes, not
/// players, kept separate because an hour probed twice and uncountable both times is a different
/// fact from an hour with nobody logged in. <see cref="MinCount"/>, <see cref="MaxCount"/> and
/// <see cref="MeanCount"/> are over counted samples alone and are <c>null</c> when there are none —
/// never a zero inferred. The third state (unmeasured) has no representation here: it is the
/// absence of a rollup, exactly as it is the absence of a <see cref="PresenceSample"/>.
/// </remarks>
public sealed record PresenceRollup
{
    public required Guid GameId { get; init; }

    public required PresenceGrain Grain { get; init; }

    /// <summary>The start of the bucket, in UTC.</summary>
    public required DateTimeOffset Bucket { get; init; }

    /// <summary>Probes in this bucket that yielded a number — <b>including a measured zero</b>.</summary>
    public required int CountedSamples { get; init; }

    /// <summary>Probes that got in and could not count. §5.4's hatched cell, tallied.</summary>
    public required int UnmeasurableSamples { get; init; }

    public int? MinCount { get; init; }

    public int? MaxCount { get; init; }

    public long? SumCount { get; init; }

    /// <summary>
    /// The mean over counted samples. Stored as a quotient of a sum and a tally rather than as an
    /// average of averages, so a day made of hours with different numbers of probes is exact.
    /// </summary>
    public decimal? MeanCount { get; init; }

    /// <summary>A filled cell — something in this bucket was counted, a measured zero included.</summary>
    public bool IsCounted => CountedSamples > 0;

    /// <summary>A hatched cell — probed, and not countable, for every probe in the bucket.</summary>
    public bool IsUncountable => CountedSamples == 0 && UnmeasurableSamples > 0;
}

/// <summary>
/// Rolled-up presence over a window, for the read side (spec §10's time series).
/// </summary>
/// <remarks>
/// A read port rather than the whole store, so an API route is never one mistaken injection away
/// from the write/rollup/watermark/partition-drop surface. A bucket nobody measured is absent,
/// never a zero — filling gaps in would publish our crawl schedule as their quiet hours.
/// </remarks>
public interface IPresenceSeries
{
    /// <summary>One game's buckets at one grain, inclusive of both ends, oldest first.</summary>
    Task<IReadOnlyList<PresenceRollup>> ForGameAsync(
        Guid gameId,
        PresenceGrain grain,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// How long measured presence is kept, at each of the three grains (spec §5.2, §15.4).
/// </summary>
/// <remarks>
/// Configuration, not a literal: §15.4 leaves the retention policy explicitly open, and a constant
/// in source would settle by accident a question the design didn't. §5.2's stated shape (raw ninety
/// days, hourly two years, daily for ever) is available as <see cref="AsDesigned"/>.
/// <para>
/// The default keeps everything. Between an unsettled retention period and a deletion, the
/// conservative default is to delete nothing — turning retention on later costs one setting; turning
/// it on too early costs data that cannot be measured again.
/// </para>
/// </remarks>
public sealed record PresenceRetentionOptions
{
    /// <summary>
    /// §5.2's heatmap window, and the floor under any raw retention.
    /// </summary>
    /// <remarks>
    /// Kept as a floor because the rollup is the only copy left once raw goes, and a rollup pass
    /// failing quietly is discovered late — keeping the window's worth of raw means the grid can be
    /// rebuilt from source after such a fault. A deployment that wants to go lower is choosing to
    /// trust its rollup.
    /// </remarks>
    public static readonly TimeSpan HeatmapWindow = TimeSpan.FromDays(56);

    /// <summary>How long raw <c>presence_sample</c> rows are kept. <c>null</c> is for ever.</summary>
    /// <remarks>
    /// Enforced by dropping whole monthly partitions, never by deleting rows, and never ahead of the
    /// rollup — a partition drops only once every hour in it has been aggregated. A month is
    /// therefore kept for up to a month longer than this asks.
    /// </remarks>
    public TimeSpan? RawSamples { get; init; }

    /// <summary>How long hourly rollups are kept. <c>null</c> is for ever.</summary>
    public TimeSpan? HourlyRollups { get; init; }

    /// <summary>
    /// How long daily rollups are kept. <c>null</c> (for ever) is what §5.2 designs for — it's the
    /// copy everything else may be dropped in favour of. Setting this departs from the design; the
    /// floor below it is a year.
    /// </summary>
    public TimeSpan? DailyRollups { get; init; }

    /// <summary>
    /// How many months of raw partitions to keep ahead of the current one.
    /// </summary>
    /// <remarks>
    /// The raw table has no <c>DEFAULT</c> partition, so a month without one is an insert error.
    /// This exists so a calendar rollover is never the first thing to discover a maintenance pass
    /// that couldn't reach the database.
    /// </remarks>
    public int MonthsOfPartitionsAhead { get; init; } = 2;

    /// <summary>
    /// How far back before the watermark each pass re-aggregates.
    /// </summary>
    /// <remarks>
    /// A sample can land after its own hour was rolled up (a slow probe, a lagging clock). The
    /// rollup is an upsert, so re-aggregating an hour is idempotent — a late row is folded in rather
    /// than lost.
    /// </remarks>
    public TimeSpan RollupOverlap { get; init; } = TimeSpan.FromHours(3);

    /// <summary>
    /// How long §11's redacted probe shapes are kept. <c>null</c> is for ever, and nothing should
    /// set it.
    /// </summary>
    /// <remarks>
    /// The one default here that deletes rather than keeps: a probe shape is not a measurement, it's
    /// the evidence a measurement was read out of, and its value ("can the new parser still read
    /// last fortnight's replies") is gone within a release cycle.
    /// </remarks>
    public TimeSpan? ProbePayloads { get; init; } = TimeSpan.FromDays(14);

    /// <summary>
    /// How long the crawl loop's record of its own cycles is kept (migration 0017). <c>null</c> is
    /// for ever.
    /// </summary>
    /// <remarks>
    /// Deletes for the same reason <see cref="ProbePayloads"/> does: a cycle report is a fact about
    /// our crawler, never a measurement of anybody's game. Thirty days because "has this instrument
    /// been running" is a question asked about last month.
    /// </remarks>
    public TimeSpan? CrawlCycles { get; init; } = TimeSpan.FromDays(30);

    /// <summary>The retention §5.2 designed: raw ninety days, hourly two years, daily for ever.</summary>
    /// <remarks>
    /// A preset, not the default: §15.4 is open, and writing the shape down isn't the same as
    /// validating it against a real deployment's storage. Ship conservative and tune.
    /// </remarks>
    public static PresenceRetentionOptions AsDesigned { get; } = new()
    {
        RawSamples = TimeSpan.FromDays(90),
        HourlyRollups = TimeSpan.FromDays(730),
        DailyRollups = null,
    };

    /// <summary>Throws on a setting that could only have come from a typo or a hand-edited file.</summary>
    public void Validate()
    {
        if (MonthsOfPartitionsAhead < 1)
        {
            throw new ArgumentException(
                "At least one month of raw partitions must be created ahead: the table has no DEFAULT "
                + "partition, so a month without one loses every measurement taken in it.");
        }

        if (RollupOverlap < TimeSpan.Zero)
        {
            throw new ArgumentException("The rollup overlap reaches backwards and cannot be negative.");
        }

        // A negative TTL puts the sweep's cutoff in the future, deleting the shapes taken moments
        // ago and leaving the old ones.
        if (ProbePayloads < TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The probe-shape TTL cannot be negative: a cutoff in the future sweeps what was just "
                + "recorded and keeps what should have gone.");
        }

        Floor(RawSamples, HeatmapWindow, nameof(RawSamples));
        Floor(HourlyRollups, HeatmapWindow, nameof(HourlyRollups));
        Floor(DailyRollups, TimeSpan.FromDays(365), nameof(DailyRollups));
    }

    private static void Floor(TimeSpan? value, TimeSpan floor, string name)
    {
        if (value is { } keep && keep < floor)
        {
            throw new ArgumentException(
                $"{name} of {keep} is shorter than the {floor.TotalDays:0} days below which a surface "
                + "this site already renders would start showing gaps it could not explain. Retention "
                + "is configurable (spec §15.4); deleting what a page is about to draw is not.");
        }
    }
}

/// <summary>What one maintenance pass did.</summary>
/// <remarks>
/// A log line answers "is the rollup keeping up" and "did retention drop anything" without a query.
/// </remarks>
public sealed record PresenceMaintenanceReport(
    int PartitionsCreated,
    int HoursRolled,
    int DaysRolled,
    int PartitionsDropped,
    int HourRollupsDeleted,
    int DayRollupsDeleted)
{
    public static readonly PresenceMaintenanceReport Nothing = new(0, 0, 0, 0, 0, 0);

    public PresenceMaintenanceReport Plus(PresenceMaintenanceReport other) => new(
        PartitionsCreated + other.PartitionsCreated,
        HoursRolled + other.HoursRolled,
        DaysRolled + other.DaysRolled,
        PartitionsDropped + other.PartitionsDropped,
        HourRollupsDeleted + other.HourRollupsDeleted,
        DayRollupsDeleted + other.DayRollupsDeleted);

    public bool DidSomething =>
        PartitionsCreated + HoursRolled + DaysRolled
        + PartitionsDropped + HourRollupsDeleted + DayRollupsDeleted > 0;
}
