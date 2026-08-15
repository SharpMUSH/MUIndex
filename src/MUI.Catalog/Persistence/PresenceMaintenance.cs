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
    /// Aggregates everything measured up to the last hour that is over.
    /// </summary>
    /// <remarks>
    /// The hour still running is left alone: a min and a max published halfway through an hour would
    /// be contradicted by the rest of it, and the raw rows are still there to be read next pass.
    /// </remarks>
    public async Task<PresenceMaintenanceReport> RollUpAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var toExclusive = FloorHour(now);

        var resume = await rollups.WatermarkAsync(PresenceGrain.Hour, cancellationToken)
            ?? await rollups.EarliestSampleAtAsync(cancellationToken);

        if (resume is not { } start)
        {
            // Nothing has ever been measured, so there is nothing to aggregate and — importantly —
            // no watermark to write, which would otherwise let retention believe an unread past had
            // been consumed.
            return PresenceMaintenanceReport.Nothing;
        }

        var from = FloorHour(start) - retention.RollupOverlap;

        if (from >= toExclusive)
        {
            return PresenceMaintenanceReport.Nothing;
        }

        var hours = await rollups.RollUpAsync(PresenceGrain.Hour, from, toExclusive, cancellationToken);
        await rollups.SetWatermarkAsync(PresenceGrain.Hour, toExclusive, cancellationToken);

        // From the start of the first day the hourly pass touched, so a day is never rewritten from
        // the fragment of itself that happened to fall after the watermark.
        var days = await rollups.RollUpAsync(
            PresenceGrain.Day, FloorDay(from), toExclusive, cancellationToken);
        await rollups.SetWatermarkAsync(PresenceGrain.Day, toExclusive, cancellationToken);

        return new PresenceMaintenanceReport(0, hours, days, 0, 0, 0);
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
