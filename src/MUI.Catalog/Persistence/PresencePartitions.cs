using System.Globalization;

namespace MUI.Catalog.Persistence;

/// <summary>
/// The naming and bounds of <c>presence_sample</c>'s monthly partitions, in one place.
/// </summary>
/// <remarks>
/// <para>
/// Two callers need this and they must agree exactly: the writer, which makes the month's partition
/// before every append, and the maintenance pass, which makes them ahead of need and drops them whole
/// when a deployment's retention says so. A drop that read the name differently from the create would
/// delete a month that was not the month it meant.
/// </para>
/// <para>
/// <b>The name is the record of the bounds.</b> Retention reads a partition's month back out of its
/// name rather than parsing <c>pg_get_expr(relpartbound)</c>, and anything not named the way we name
/// them is not ours and is never dropped — an operator who attached a partition by hand keeps it.
/// </para>
/// </remarks>
internal static class PresencePartitions
{
    public const string Table = "presence_sample";

    public const string Prefix = $"{Table}_";

    private const string MonthFormat = "yyyyMM";

    /// <summary>The first instant of the UTC month <paramref name="at"/> falls in.</summary>
    public static DateTimeOffset MonthOf(DateTimeOffset at)
    {
        var utc = at.UtcDateTime;

        return new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero);
    }

    public static string NameFor(DateTimeOffset month) =>
        Prefix + MonthOf(month).ToString(MonthFormat, CultureInfo.InvariantCulture);

    /// <summary>The month a partition name means, or null when the name is not one of ours.</summary>
    public static DateTimeOffset? MonthFromName(string name)
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
    /// parameters in PostgreSQL. Every value in it is derived from a <see cref="DateTimeOffset"/>, so
    /// there is no caller-controlled text anywhere in the statement.
    /// </remarks>
    public static string CreateDdl(DateTimeOffset month)
    {
        var start = MonthOf(month);
        var end = start.AddMonths(1);

        return $"""
            CREATE TABLE IF NOT EXISTS {NameFor(start)}
            PARTITION OF {Table}
            FOR VALUES FROM ('{start:yyyy-MM-dd HH:mm:sszzz}') TO ('{end:yyyy-MM-dd HH:mm:sszzz}')
            """;
    }
}
