using Dapper;

using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// A field value too large for an index must not cost a game its listing.
/// </summary>
/// <remarks>
/// A connect screen is routinely thousands of characters, and the <c>(field, value)</c> index tried
/// to hold one whole — <c>54000: index row size … exceeds btree version 4 maximum 2704</c>. The
/// failure is total, not partial: the insert is refused, the probe's ingestion lost, and lost again
/// on every future probe, permanently unlistable with nothing saying so.
/// </remarks>
public class OversizedFieldValueTests
{
    private static readonly DateTimeOffset Now = Seed.Now;

    /// <summary>The real shape: a connect screen far past the btree limit, stored whole.</summary>
    [Test]
    public async Task AConnectScreenTooLargeToIndexIsStillStored()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var store = new NpgsqlGameFieldStore(db.DataSource);

        // Longer than the longest observed in the wild and several times the index limit, so this
        // fails against the old index and passes against the bounded one.
        var screen = string.Join('\n', Enumerable.Repeat(new string('=', 78), 160));

        await store.UpsertAsync(new GameField(
            game, InternalFields.ConnectScreen, FieldSource.Banner, screen, Now, Now));

        var stored = (await store.ForGameAsync(game))
            .Single(f => f.Field == InternalFields.ConnectScreen);

        // Stored whole — the index is bounded, never the fact. Truncating what a game sent to fit
        // our own index is the quiet lossiness this schema refuses.
        await Assert.That(stored.Value).IsEqualTo(screen);
        await Assert.That(stored.Value.Length).IsGreaterThan(2704);
    }

    /// <summary>
    /// Two long values that differ only past the indexed prefix are still two distinct rows.
    /// </summary>
    /// <remarks>
    /// The prefix is an index, not a key. Bounding it must not collapse rows that share their first
    /// 256 characters, which two connect screens from one codebase easily do.
    /// </remarks>
    [Test]
    public async Task TwoValuesSharingTheirFirstBytesRemainDistinct()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var one = await Seed.GameAsync(db, slug: "one", name: "One");
        var two = await Seed.GameAsync(db, slug: "two", name: "Two");
        var store = new NpgsqlGameFieldStore(db.DataSource);

        var shared = new string('#', 4000);

        await store.UpsertAsync(new GameField(
            one, InternalFields.ConnectScreen, FieldSource.Banner, shared + "ONE", Now, Now));
        await store.UpsertAsync(new GameField(
            two, InternalFields.ConnectScreen, FieldSource.Banner, shared + "TWO", Now, Now));

        var first = (await store.ForGameAsync(one)).Single(f => f.Field == InternalFields.ConnectScreen);
        var second = (await store.ForGameAsync(two)).Single(f => f.Field == InternalFields.ConnectScreen);

        await Assert.That(first.Value).EndsWith("ONE");
        await Assert.That(second.Value).EndsWith("TWO");
    }

    /// <summary>
    /// Both indexes over this table are bounded, because both could refuse a connect screen.
    /// </summary>
    /// <remarks>
    /// The first fix caught only one index and the next probe failed on the other, so this asserts
    /// the property over every index on <c>game_field</c>. An index on a raw or merely case-folded
    /// value is the shape of the bug — folding doesn't shorten anything.
    /// </remarks>
    [Test]
    public async Task NoIndexOnThisTableCanRefuseALongValue()
    {
        await using var db = await PostgresFixture.MigratedAsync();

        await using var connection = await db.DataSource.OpenConnectionAsync();

        var definitions = (await connection.QueryAsync<string>(
            "SELECT indexdef FROM pg_indexes WHERE tablename = 'game_field'")).ToList();

        foreach (var definition in definitions.Where(d => d.Contains("value", StringComparison.Ordinal)))
        {
            // Either the indexed expression is bounded, or the index only covers rows short enough.
            var bounded = definition.Contains("256", StringComparison.Ordinal);

            await Assert.That(bounded)
                .IsTrue()
                .Because($"an unbounded index over `value` refuses a connect screen: {definition}");
        }
    }

    /// <summary>The index still exists and still leads on the field, which is what it is for.</summary>
    [Test]
    public async Task TheFacetLookupIsStillIndexed()
    {
        await using var db = await PostgresFixture.MigratedAsync();

        await using var connection = await db.DataSource.OpenConnectionAsync();

        var definition = await connection.ExecuteScalarAsync<string>(
            "SELECT indexdef FROM pg_indexes WHERE indexname = 'game_field_field_value_idx'");

        await Assert.That(definition).IsNotNull();
        await Assert.That(definition!).Contains("field");

        // Asserted on the bound, not the spelling: PostgreSQL reports the expression as
        // "left"(value, 256), and matching source text would break on formatting while saying
        // nothing about overflow.
        await Assert.That(definition!).Contains("256");
        await Assert.That(definition!.Contains("(field, value)", StringComparison.Ordinal)).IsFalse();
    }
}
