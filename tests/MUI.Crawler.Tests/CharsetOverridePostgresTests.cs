using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawler.Persistence;
using MUI.Crawler.Tests.Support;
using MUI.Discovery;

namespace MUI.Crawler.Tests;

/// <summary>
/// The one seam that carries an operator's <c>CHARSET</c> override to the probe.
/// </summary>
/// <remarks>
/// <para>
/// The probe has no database and must not grow one, so the override is loaded with the target it
/// applies to and travels on <c>ProbeTarget.Charset</c>. Everything else about the feature is
/// testable without Postgres; <b>this half is only true if the SQL is</b>, and the SQL is a
/// correlated subquery against a table the target row does not join to.
/// </para>
/// <para>
/// Why it matters that this be pinned: with the subquery silently returning null, every probe still
/// succeeds, every test still passes, and the only symptom is that thirteen connect screens stay
/// unreadable for ever. A feature that fails by doing nothing needs a test that asserts it did
/// something.
/// </para>
/// </remarks>
public class CharsetOverridePostgresTests
{
    private static GameRecord Game(Guid id) => new(
        id,
        Slug: "pkuxkx",
        Name: "北大侠客行",
        Tagline: null,
        State: LifecycleState.Active,
        IsClaimed: false,
        FirstSeenAt: DateTimeOffset.UtcNow);

    private static CrawlTarget Target(Guid? gameId) => new()
    {
        Id = Guid.CreateVersion7(),
        GameId = gameId,
        Host = "mud.pkuxkx.net",
        Port = 8080,
        NextProbeAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        FirstSeenAt = DateTimeOffset.UtcNow,
    };

    [Test]
    public async Task AStaffOverrideReachesTheTargetTheProbeWillDial()
    {
        await using var database = await PostgresFixture.MigratedAsync();

        var games = new NpgsqlGameStore(database.DataSource);
        var fields = new NpgsqlGameFieldStore(database.DataSource);
        var targets = new NpgsqlCrawlTargetRepository(database.DataSource);

        var gameId = Guid.CreateVersion7();
        await games.InsertAsync(Game(gameId));
        await targets.AddAsync(Target(gameId), default);

        await Assert.That((await targets.DueAsync(DateTimeOffset.UtcNow, 10, default))[0].Charset)
            .IsNull();

        var now = DateTimeOffset.UtcNow;
        await fields.UpsertAsync(new GameField(gameId, "CHARSET", FieldSource.Staff, "gbk", now, now));

        var due = await targets.DueAsync(DateTimeOffset.UtcNow, 10, default);

        await Assert.That(due[0].Charset).IsEqualTo("gbk");
        await Assert.That((await targets.ByAddressAsync("mud.pkuxkx.net", 8080, default))!.Charset)
            .IsEqualTo("gbk");
    }

    /// <summary>
    /// Only <c>staff</c>, never the precedence winner.
    /// </summary>
    /// <remarks>
    /// The other rungs on this field are the game's own MSSP claim and what CHARSET negotiated, and
    /// neither is a statement about how these bytes should be read — pkuxkx negotiates UTF-8 and
    /// sends GBK, so taking the handshake's answer here would reinstate exactly the bug the override
    /// exists to fix, and taking MSSP's would let a server choose how we decode it.
    /// </remarks>
    [Test]
    public async Task WhatTheGameItselfSaysIsNotAnOverride()
    {
        await using var database = await PostgresFixture.MigratedAsync();

        var games = new NpgsqlGameStore(database.DataSource);
        var fields = new NpgsqlGameFieldStore(database.DataSource);
        var targets = new NpgsqlCrawlTargetRepository(database.DataSource);

        var gameId = Guid.CreateVersion7();
        await games.InsertAsync(Game(gameId));
        await targets.AddAsync(Target(gameId), default);

        var now = DateTimeOffset.UtcNow;
        await fields.UpsertAsync(new GameField(gameId, "CHARSET", FieldSource.Handshake, "utf-8", now, now));
        await fields.UpsertAsync(new GameField(gameId, "CHARSET", FieldSource.Mssp, "UTF-8", now, now));

        var due = await targets.DueAsync(DateTimeOffset.UtcNow, 10, default);

        await Assert.That(due[0].Charset).IsNull();
    }

    /// <summary>
    /// A target not yet attributed to a game has no override and must still load.
    /// </summary>
    /// <remarks>
    /// 269 of production's 710 targets have a null <c>game_id</c>. A subquery that turned those into
    /// an error, or worse into somebody else's value, would take the crawl down rather than one
    /// game's screen.
    /// </remarks>
    [Test]
    public async Task ATargetWithNoGameYetIsFine()
    {
        await using var database = await PostgresFixture.MigratedAsync();

        var targets = new NpgsqlCrawlTargetRepository(database.DataSource);
        await targets.AddAsync(Target(gameId: null), default);

        var due = await targets.DueAsync(DateTimeOffset.UtcNow, 10, default);

        await Assert.That(due).Count().IsEqualTo(1);
        await Assert.That(due[0].Charset).IsNull();
    }
}
