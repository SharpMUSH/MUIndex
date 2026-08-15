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
/// <para>
/// <b>The tally is kept and the conclusion is not.</b> <see cref="CountedSamples"/> and
/// <see cref="UnmeasurableSamples"/> are counts of probes, not of players, and they are separate
/// because an hour that was probed twice and could not be counted either time is a different fact
/// from an hour in which nobody was logged in. <see cref="MinCount"/>, <see cref="MaxCount"/> and
/// <see cref="MeanCount"/> are over the counted samples alone and are <c>null</c> when there were
/// none — a rollup never answers "how many players" with a zero it inferred.
/// </para>
/// <para>
/// The third state has no representation here on purpose: an hour nobody measured is the
/// <b>absence</b> of one of these, exactly as it is the absence of a <see cref="PresenceSample"/>.
/// </para>
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

    /// <summary>
    /// The salt epoch every aggregate in this bucket was computed under (spec §11), or <c>null</c>
    /// where the bucket spanned a rotation and the two sets cannot be compared.
    /// </summary>
    public string? SaltEpoch { get; init; }

    /// <summary>
    /// The largest single-sample unique-player estimate inside <see cref="SaltEpoch"/>, and
    /// deliberately not a union: a union needs the hashes, and the hashes never leave the probe.
    /// </summary>
    public int? PeakDistinctEstimate { get; init; }

    /// <summary>A filled cell — something in this bucket was counted, a measured zero included.</summary>
    public bool IsCounted => CountedSamples > 0;

    /// <summary>A hatched cell — probed, and not countable, for every probe in the bucket.</summary>
    public bool IsUncountable => CountedSamples == 0 && UnmeasurableSamples > 0;
}

/// <summary>
/// How long measured presence is kept, at each of the three grains (spec §5.2, §15.4).
/// </summary>
/// <remarks>
/// <para>
/// Configuration and not a literal, for the same reason <c>DatasetLicenceOptions</c> is: §15.4
/// leaves the retention policy — and the salt rotation period beside it — explicitly open, and a
/// constant in the source would settle by accident a question the design deliberately did not. §5.2
/// does state a shape (raw ninety days, hourly two years, daily for ever), and
/// <see cref="AsDesigned"/> is exactly that shape, one line of configuration away.
/// </para>
/// <para>
/// <b>The default keeps everything.</b> §5.2 authorises dropping raw samples once they have been
/// aggregated, and §15.4 says the period is unsettled, and §15.3 — the cost envelope that would bound
/// it — is unsettled too. Between an unsettled number and a deletion, the conservative default is to
/// delete nothing and let a deployment that has measured its own storage say when to start. Turning
/// retention on later costs one setting; turning it on too early costs data that cannot be measured
/// again.
/// </para>
/// </remarks>
public sealed record PresenceRetentionOptions
{
    /// <summary>
    /// §5.2's heatmap window, which is the floor under any raw retention: the site still reads raw
    /// samples for the day × hour graphic, so a retention shorter than the window it draws would blank
    /// the left-hand end of every heatmap on the site.
    /// </summary>
    public static readonly TimeSpan HeatmapWindow = TimeSpan.FromDays(56);

    /// <summary>How long raw <c>presence_sample</c> rows are kept. <c>null</c> is for ever.</summary>
    /// <remarks>
    /// Enforced by dropping whole monthly partitions, never by deleting rows, and never ahead of the
    /// rollup: a partition is dropped only once every hour in it has been aggregated into something
    /// that outlives it. A month is therefore kept for up to a month longer than this asks.
    /// </remarks>
    public TimeSpan? RawSamples { get; init; }

    /// <summary>How long hourly rollups are kept. <c>null</c> is for ever.</summary>
    public TimeSpan? HourlyRollups { get; init; }

    /// <summary>
    /// How long daily rollups are kept. <c>null</c> is for ever, and for ever is what §5.2 says: the
    /// daily rollup is the copy everything else is allowed to be dropped in favour of. A deployment
    /// that sets this is departing from the design, which is why the floor below it is a year.
    /// </summary>
    public TimeSpan? DailyRollups { get; init; }

    /// <summary>
    /// How many months of raw partitions to keep ahead of the current one.
    /// </summary>
    /// <remarks>
    /// The raw table has no <c>DEFAULT</c> partition (migration 0003), so a month without one is an
    /// insert error. <see cref="Persistence.NpgsqlPresenceStore"/> makes the month before every append
    /// as well; this exists so that a calendar rollover is never the first thing to discover a
    /// database the maintenance pass could not reach.
    /// </remarks>
    public int MonthsOfPartitionsAhead { get; init; } = 2;

    /// <summary>
    /// How far back before the watermark each pass re-aggregates.
    /// </summary>
    /// <remarks>
    /// A sample can land after its own hour has been rolled up — a probe that finished slowly, a
    /// replica whose clock is behind. Re-reading a few hours costs a bounded query over the newest
    /// partition and means a late row is folded in rather than lost, because the rollup is an upsert
    /// and re-aggregating an hour produces the same row twice.
    /// </remarks>
    public TimeSpan RollupOverlap { get; init; } = TimeSpan.FromHours(3);

    /// <summary>
    /// How long §11's redacted probe shapes are kept. <c>null</c> is for ever, and nothing should
    /// set it.
    /// </summary>
    /// <remarks>
    /// <b>The one default here that deletes rather than keeps.</b> Every other retention on this
    /// record defaults to keeping everything, because a measurement not taken cannot be taken again.
    /// A probe shape is not a measurement — it is the evidence a measurement was read out of, its
    /// value is entirely "can the new parser still read last fortnight's replies", and that value is
    /// gone within a release cycle. Between an unsettled number and a deletion the conservative
    /// choice for a measurement is to keep and for this is to drop.
    /// </remarks>
    public TimeSpan? ProbePayloads { get; init; } = TimeSpan.FromDays(14);

    /// <summary>The retention §5.2 designed: raw ninety days, hourly two years, daily for ever.</summary>
    /// <remarks>
    /// Available as a preset rather than as the default, because §15.4 is open and the shape being
    /// written down is not the same as the number having been validated against a real deployment's
    /// storage. Ship conservative and tune.
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

        // A negative TTL puts the sweep's cutoff in the future, which would delete the shapes taken
        // moments ago and leave the old ones. The failure of every other setting here is keeping too
        // much; this one's is deleting the newest evidence there is.
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
/// Every number here is a count of things that happened, so a log line from a deployment answers "is
/// the rollup keeping up" and "did retention drop anything" without a query.
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
