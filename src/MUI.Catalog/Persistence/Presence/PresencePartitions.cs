using System.Globalization;

namespace MUI.Catalog.Persistence;

/// <summary>
/// The naming and bounds of a presence table's monthly partitions, in one place.
/// </summary>
/// <remarks>
/// <para>
/// The writer (creating ahead of an append) and the maintenance pass (dropping on retention) must
/// agree on the name exactly, or a drop could delete the wrong month. Retention reads a partition's
/// month back out of its name rather than parsing bounds, so anything not named the way we name them
/// is never dropped — an operator who attached a partition by hand keeps it.
/// </para>
/// <para>
/// Parameterised by table since migration 0037, which partitioned <c>presence_rollup_hour</c> the
/// same way, for the same reason, and needs the same guarantee. One implementation rather than two:
/// the naming rule is the safety property, and a second copy of it is a second thing to get wrong on
/// the day somebody changes the format.
/// </para>
/// </remarks>
internal sealed class PresencePartitions
{
    private const string MonthFormat = "yyyyMM";

    private PresencePartitions(string table, string column)
    {
        Table = table;
        Column = column;
        Prefix = table + "_";
    }

    /// <summary>Raw presence, partitioned on <c>at</c> since migration 0003.</summary>
    public static PresencePartitions Samples { get; } = new("presence_sample", "at");

    /// <summary>The hourly rollup, partitioned on <c>hour</c> since migration 0037.</summary>
    public static PresencePartitions HourlyRollups { get; } = new("presence_rollup_hour", "hour");

    public string Table { get; }

    /// <summary>The timestamp column the range is over — the partition key.</summary>
    public string Column { get; }

    public string Prefix { get; }

    /// <summary>The first instant of the UTC month <paramref name="at"/> falls in.</summary>
    public static DateTimeOffset MonthOf(DateTimeOffset at)
    {
        var utc = at.UtcDateTime;

        return new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero);
    }

    public string NameFor(DateTimeOffset month) =>
        Prefix + MonthOf(month).ToString(MonthFormat, CultureInfo.InvariantCulture);

    /// <summary>The month a partition name means, or null when the name is not one of ours.</summary>
    public DateTimeOffset? MonthFromName(string name)
    {
        if (!name.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        return DateTime.TryParseExact(
            name[Prefix.Length..],
            MonthFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? new DateTimeOffset(parsed.Year, parsed.Month, 1, 0, 0, 0, TimeSpan.Zero)
            : null;
    }

    /// <summary>
    /// The DDL for one month's partition.
    /// </summary>
    /// <remarks>
    /// Interpolated rather than parameterised because a table name and a partition bound cannot be
    /// parameters in PostgreSQL. Every value in it is derived from a <see cref="DateTimeOffset"/> or
    /// from one of the two instances above, so there is no caller-controlled text anywhere in the
    /// statement.
    /// </remarks>
    public string CreateDdl(DateTimeOffset month)
    {
        var start = MonthOf(month);
        var end = start.AddMonths(1);

        return $"""
            CREATE TABLE IF NOT EXISTS {NameFor(start)}
            PARTITION OF {Table}
            FOR VALUES FROM ('{start:yyyy-MM-dd HH:mm:sszzz}') TO ('{end:yyyy-MM-dd HH:mm:sszzz}')
            """;
    }

    /// <summary>Every partition currently attached to this table, oldest first.</summary>
    public const string PartitionsSql =
        """
        SELECT c.relname FROM pg_inherits
          JOIN pg_class c ON c.oid = pg_inherits.inhrelid
          JOIN pg_class p ON p.oid = pg_inherits.inhparent
         WHERE p.relname = @table
         ORDER BY c.relname
        """;
}
