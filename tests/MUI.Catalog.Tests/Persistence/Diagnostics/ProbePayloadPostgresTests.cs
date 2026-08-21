using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>§11's replay window: what is kept, for how long, and what is never kept.</summary>
public class ProbePayloadPostgresTests
{
    private static readonly DateTimeOffset Now = Seed.Now;

    [Test]
    public async Task AShapeIsStoredAndReadBackForItsGame()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var payloads = new NpgsqlProbePayloads(db.DataSource);

        await payloads.RecordAsync([Shape(game, Now, "Aaaaaa Aaaa   On For  Idle")]);

        var stored = (await payloads.ForGameAsync(game, Now.AddDays(-1))).Single();

        await Assert.That(stored.Kind).IsEqualTo(ProbePayloadKind.Who);
        await Assert.That(stored.Shape).IsEqualTo("Aaaaaa Aaaa   On For  Idle");
        await Assert.That(stored.Port).IsEqualTo(4201);
    }

    /// <summary>
    /// One probe writes one row, however many times the writer is asked.
    /// </summary>
    /// <remarks>
    /// A retried cycle re-recording the same probe would otherwise double the replay window's size
    /// and tell whoever reads it that a game was probed twice in one instant.
    /// </remarks>
    [Test]
    public async Task TheSameProbeRecordedTwiceIsOneRow()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var payloads = new NpgsqlProbePayloads(db.DataSource);

        await payloads.RecordAsync([Shape(game, Now, "first")]);
        await payloads.RecordAsync([Shape(game, Now, "second")]);

        var stored = await payloads.ForGameAsync(game, Now.AddDays(-1));

        await Assert.That(stored).Count().IsEqualTo(1);
        await Assert.That(stored[0].Shape).IsEqualTo("first");
    }

    /// <summary>
    /// The sweep drops what is past its TTL and keeps what is not.
    /// </summary>
    /// <remarks>
    /// Unclamped, unlike presence retention: nothing is derived from a shape, so there is no
    /// watermark to wait on, and a TTL that waited on one would keep a payload alive because a
    /// rollup was late.
    /// </remarks>
    [Test]
    public async Task TheSweepDropsWhatIsPastItsTtl()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var payloads = new NpgsqlProbePayloads(db.DataSource);

        await payloads.RecordAsync([
            Shape(game, Now.AddDays(-40), "old"),
            Shape(game, Now.AddDays(-2), "recent"),
        ]);

        var gone = await payloads.SweepAsync(Now.AddDays(-14));

        await Assert.That(gone).IsEqualTo(1);

        var left = (await payloads.ForGameAsync(game, Now.AddYears(-1))).Single();

        await Assert.That(left.Shape).IsEqualTo("recent");
    }

    /// <summary>
    /// An address nobody has identified still gets a row.
    /// </summary>
    /// <remarks>
    /// Those are the most interesting probes to replay — an unidentified address is often one we
    /// could not read — so a null game is a nullable column rather than a skipped write.
    /// </remarks>
    [Test]
    public async Task AnUnidentifiedAddressIsStillRecorded()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var payloads = new NpgsqlProbePayloads(db.DataSource);

        var written = await payloads.RecordAsync([
            new ProbePayload(null, "stranger.example", 4000, Now, ProbePayloadKind.Who, "aaaa"),
        ]);

        await Assert.That(written).IsEqualTo(1);
    }

    private static ProbePayload Shape(Guid game, DateTimeOffset at, string shape) =>
        new(game, "corvid.example", 4201, at, ProbePayloadKind.Who, shape);
}
