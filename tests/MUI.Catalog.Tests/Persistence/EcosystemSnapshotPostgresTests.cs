using Dapper;

using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// §9's adoption curves, and the arithmetic that makes a point on one trustworthy.
/// </summary>
public class EcosystemSnapshotPostgresTests
{
    private static readonly DateTimeOffset Day = new(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The components are stored and the share is divided out of them.
    /// </summary>
    /// <remarks>
    /// A snapshot holding "41%" would be a number whose denominator had been thrown away —
    /// uncheckable afterwards and impossible to recompute if a share is ever defined differently.
    /// Keeping the four counts means a point on a curve is divided by the same record the live
    /// dashboard divides, so the two cannot come to mean different things.
    /// </remarks>
    [Test]
    public async Task APointKeepsTheCountsAndNotTheRatio()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var snapshots = new NpgsqlEcosystemSnapshots(db.DataSource);

        await snapshots.RecordAsync(Day, [new ProtocolAdoption("GMCP", 41, 3, 100, 12, 60)]);

        var point = (await snapshots.CurveAsync("GMCP", Day.AddDays(-1), Day.AddDays(1))).Single();

        await Assert.That(point.Adoption.Offered).IsEqualTo(41);
        await Assert.That(point.Adoption.Handshakes).IsEqualTo(100);
        await Assert.That(point.Adoption.Measured!.Fraction).IsEqualTo(0.41);

        // And the declared side stays a share of the reports, never of the handshakes.
        await Assert.That(point.Adoption.DeclaredShare!.Denominator).IsEqualTo(60);
    }

    /// <summary>
    /// A pass that runs twice leaves one point on the day, and the later reading wins.
    /// </summary>
    /// <remarks>
    /// The maintenance pass runs on an interval rather than at midnight, and a replica taking the
    /// lease mid-afternoon runs it again. Two points on one day would put a step in every curve that
    /// measured our scheduling.
    /// </remarks>
    [Test]
    public async Task RunningTwiceInADayLeavesOnePoint()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var snapshots = new NpgsqlEcosystemSnapshots(db.DataSource);

        await snapshots.RecordAsync(Day.AddHours(2), [new ProtocolAdoption("TLS", 10, 0, 100, 5, 60)]);
        await snapshots.RecordAsync(Day.AddHours(20), [new ProtocolAdoption("TLS", 14, 0, 110, 6, 61)]);

        var points = await snapshots.CurveAsync("TLS", Day.AddDays(-1), Day.AddDays(1));

        await Assert.That(points).Count().IsEqualTo(1);
        await Assert.That(points[0].Adoption.Offered).IsEqualTo(14);
    }

    /// <summary>
    /// Never measured stays null, and is not stored as nought games offering it.
    /// </summary>
    /// <remarks>
    /// §3.1's rule reaching the snapshot table. A protocol we have never observed either way is not
    /// a protocol nobody offers, and a curve drawn through a fabricated zero would show a rise that
    /// is entirely our own instrumentation arriving.
    /// </remarks>
    [Test]
    public async Task NeverMeasuredIsNullAndNotZero()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var snapshots = new NpgsqlEcosystemSnapshots(db.DataSource);

        await snapshots.RecordAsync(Day, [new ProtocolAdoption("MXP", null, 0, 100, 3, 60)]);

        var point = (await snapshots.CurveAsync("MXP", Day.AddDays(-1), Day.AddDays(1))).Single();

        await Assert.That(point.Adoption.Offered).IsNull();
        await Assert.That(point.Adoption.Measured).IsNull();
    }

    /// <summary>One point is not a curve, and the type says so rather than a surface guessing.</summary>
    [Test]
    public async Task OnePointIsNotACurve()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var snapshots = new NpgsqlEcosystemSnapshots(db.DataSource);

        await snapshots.RecordAsync(Day, [new ProtocolAdoption("UTF-8", 20, 0, 100, 9, 60)]);
        var one = await snapshots.CurveAsync("UTF-8", Day.AddDays(-30), Day.AddDays(1));

        await Assert.That(new AdoptionCurve("UTF-8", one).IsCurve).IsFalse();

        await snapshots.RecordAsync(Day.AddDays(1), [new ProtocolAdoption("UTF-8", 26, 0, 104, 9, 61)]);
        var two = await snapshots.CurveAsync("UTF-8", Day.AddDays(-30), Day.AddDays(2));

        var curve = new AdoptionCurve("UTF-8", two);

        await Assert.That(curve.IsCurve).IsTrue();
        await Assert.That(curve.Ends.First!.Fraction).IsEqualTo(0.2);
        await Assert.That(curve.Points[0].At).IsLessThan(curve.Points[1].At);
    }

    /// <summary>
    /// A share above one is refused by the schema rather than drawn.
    /// </summary>
    /// <remarks>
    /// A numerator larger than its denominator is an arithmetic mistake, not a surprising
    /// measurement, and the failure it produces on a public page is a bar past the end of its track.
    /// Caught where it cannot be forgotten.
    /// </remarks>
    [Test]
    public async Task ANumeratorLargerThanItsDenominatorIsRefused()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var snapshots = new NpgsqlEcosystemSnapshots(db.DataSource);

        await Assert.That(async () =>
                await snapshots.RecordAsync(Day, [new ProtocolAdoption("GMCP", 101, 0, 100, 0, 60)]))
            .Throws<Npgsql.PostgresException>();
    }
}
