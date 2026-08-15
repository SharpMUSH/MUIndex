using Microsoft.Extensions.Logging;

namespace MUI.Catalog.Persistence;

/// <summary>
/// The three things that have to happen to <c>presence_sample</c> on a schedule (spec §5.2): make the
/// partitions before they are needed, aggregate what has been measured, and then — only if a
/// deployment has answered §15.4 — let the oldest raw months go.
/// </summary>
/// <remarks>
/// <para>
/// <b>The order is the safety property.</b> Retention runs last and never past what the rollups have
/// consumed, so a pass whose rollup step failed cannot go on to drop the raw rows the rollup was
/// supposed to read. §5.2 permits raw samples to be dropped "only after [they have] been aggregated
/// into something that outlives [them]", and that word <em>after</em> is enforced here by a watermark
/// rather than by the order the statements happen to be written in.
/// </para>
/// <para>
/// <b>Nothing in here decides how long anything is kept.</b> That is <see cref="PresenceRetentionOptions"/>,
/// whose defaults keep everything, because §15.4 is an open question and a deletion is the wrong way
/// to be wrong about one.
/// </para>
/// </remarks>
public sealed class PresenceMaintenance(
    NpgsqlPresenceStore samples,
    NpgsqlPresenceRollupStore rollups,
    PresenceRetentionOptions retention,
    ILogger? logger = null)
{
    /// <summary>
    /// Whether the schema this pass works on has been applied yet.
    /// </summary>
    /// <remarks>
    /// A hosted service that starts beside the migration run asks this rather than discovering the
    /// answer as an exception. Cheap enough to ask every pass, so nothing has to remember it.
    /// </remarks>
    public Task<bool> SchemaReadyAsync(CancellationToken cancellationToken = default) =>
        rollups.IsInstalledAsync(cancellationToken);

    /// <summary>One whole pass: partitions, then rollups, then retention.</summary>
    public async Task<PresenceMaintenanceReport> RunAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        retention.Validate();

        var created = await samples.EnsurePartitionsThroughAsync(
            now, now.AddMonths(retention.MonthsOfPartitionsAhead), cancellationToken);

        var rolled = await RollUpAsync(now, cancellationToken);
        var swept = await SweepRetentionAsync(now, cancellationToken);

        var report = new PresenceMaintenanceReport(created.Count, 0, 0, 0, 0, 0)
            .Plus(rolled)
            .Plus(swept);

        if (report.DidSomething)
        {
            logger?.LogInformation("Presence maintenance: {Report}", report);
        }

        return report;
    }

    /// <summary>
    /// Aggregates everything measured up to the last hour that is over, at both grains.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The hour still running is left alone: a min and a max published halfway through an hour would
    /// be contradicted by the rest of it, and the raw rows are still there to be read next pass.
    /// </para>
    /// <para>
    /// <b>Each grain resumes from its own watermark, and this is the part that deletes data if it is
    /// wrong.</b> The two aggregations are separate statements, so a restart, a cancellation or a
    /// transient error between them leaves the hours rolled and the days not. A daily pass that then
    /// resumed from the <em>hourly</em> watermark would skip everything older than the overlap, write
    /// its own watermark as though it had read it, and let retention drop the raw months behind it —
    /// and the grain §5.2 keeps for ever would be permanently missing, with the only other copy gone.
    /// Measured, by <c>EachGrainResumesFromItsOwnWatermarkAndNotFromTheHourly</c>, which produced six
    /// hours of history and one day of it before this read its own mark.
    /// </para>
    /// </remarks>
    public async Task<PresenceMaintenanceReport> RollUpAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var toExclusive = FloorHour(now);

        // Only consulted for a grain that has never run. Read once, because it is a scan of the
        // oldest partition and both grains would otherwise ask for the same answer.
        var earliest = new Lazy<Task<DateTimeOffset?>>(() => rollups.EarliestSampleAtAsync(cancellationToken));

        var hours = await RollGrainAsync(PresenceGrain.Hour, toExclusive, earliest, cancellationToken);
        var days = await RollGrainAsync(PresenceGrain.Day, toExclusive, earliest, cancellationToken);

        return new PresenceMaintenanceReport(0, hours, days, 0, 0, 0);
    }

    /// <summary>
    /// Rolls one grain from where that grain got to, and moves that grain's watermark after — never
    /// before, so an interrupted pass resumes rather than skips.
    /// </summary>
    private async Task<int> RollGrainAsync(
        PresenceGrain grain,
        DateTimeOffset toExclusive,
        Lazy<Task<DateTimeOffset?>> earliest,
        CancellationToken cancellationToken)
    {
        var resume = await rollups.WatermarkAsync(grain, cancellationToken) ?? await earliest.Value;

        if (resume is not { } start)
        {
            // Nothing has ever been measured, so there is nothing to aggregate and — importantly —
            // no watermark to write, which would otherwise let retention believe an unread past had
            // been consumed.
            return 0;
        }

        // Floored to the grain's own boundary, so a bucket is never rewritten from the fragment of
        // itself that happened to fall after the watermark.
        var from = Floor(grain, start - retention.RollupOverlap);

        if (from >= toExclusive)
        {
            return 0;
        }

        var rolled = await rollups.RollUpAsync(grain, from, toExclusive, cancellationToken);
        await rollups.SetWatermarkAsync(grain, toExclusive, cancellationToken);

        return rolled;
    }

    /// <summary>
    /// Applies whatever retention a deployment has configured, and by default applies none.
    /// </summary>
    /// <remarks>
    /// Every cutoff is clamped to what has actually been rolled up. A deployment that asks for ninety
    /// days and whose rollup has been failing for a fortnight keeps a fortnight more than it asked
    /// for, which is the only direction this may err in.
    /// </remarks>
    public async Task<PresenceMaintenanceReport> SweepRetentionAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var hourly = await rollups.WatermarkAsync(PresenceGrain.Hour, cancellationToken);
        var daily = await rollups.WatermarkAsync(PresenceGrain.Day, cancellationToken);

        // What both grains have consumed. Null means one of them never has, and nothing may go.
        var consumed = hourly is { } h && daily is { } d ? Min(h, d) : (DateTimeOffset?)null;

        var dropped = 0;

        if (retention.RawSamples is { } keepRaw && consumed is { } rolledThrough)
        {
            var boundary = Min(now - keepRaw, rolledThrough);

            var partitions = await samples.DropPartitionsEndingAtOrBeforeAsync(boundary, cancellationToken);
            dropped = partitions.Count;

            if (dropped > 0)
            {
                logger?.LogInformation(
                    "Dropped {Count} rolled-up raw presence partitions: {Partitions}",
                    dropped, string.Join(", ", partitions));
            }
        }

        var hoursDeleted = 0;

        if (retention.HourlyRollups is { } keepHours && daily is { } dailyThrough)
        {
            // Clamped to the daily watermark: an hourly bucket the daily grain has not read is, again,
            // the only copy there is.
            hoursDeleted = await rollups.DeleteBeforeAsync(
                PresenceGrain.Hour, Min(now - keepHours, dailyThrough), cancellationToken);
        }

        var daysDeleted = 0;

        if (retention.DailyRollups is { } keepDays)
        {
            // §5.2 keeps this grain for ever and the default here is null, so reaching this line means
            // a deployment said so in as many words. Nothing outlives it.
            daysDeleted = await rollups.DeleteBeforeAsync(
                PresenceGrain.Day, now - keepDays, cancellationToken);
        }

        return new PresenceMaintenanceReport(0, 0, 0, dropped, hoursDeleted, daysDeleted);
    }

    private static DateTimeOffset Floor(PresenceGrain grain, DateTimeOffset at) => grain switch
    {
        PresenceGrain.Hour => FloorHour(at),
        PresenceGrain.Day => FloorDay(at),
        _ => throw new ArgumentOutOfRangeException(nameof(grain), grain, "No bucket has that grain."),
    };

    private static DateTimeOffset FloorHour(DateTimeOffset at)
    {
        var utc = at.UtcDateTime;

        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
    }

    private static DateTimeOffset FloorDay(DateTimeOffset at) =>
        new(at.UtcDateTime.Date, TimeSpan.Zero);

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) =>
        left < right ? left : right;
}
