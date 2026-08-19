using Dapper;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawler.Persistence;
using MUI.Crawler.Tests.Support;

using Npgsql;

namespace MUI.Crawler.Tests;

/// <summary>
/// The residue §7.8 leaves, and the one lever an operator has over it.
/// </summary>
/// <remarks>
/// Exists because the rule it backs stops short on purpose: the rubric publishes what a probe can
/// prove and refuses the rest, so some real games no measurement of ours reaches are stuck unless a
/// person can look and release them by hand, recorded as <c>staff</c> beside the signals a probe would
/// have written.
/// </remarks>
public class SubmissionQueuePostgresTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Test]
    public async Task TheQueueHoldsSubmissionsThatAnsweredAndWereNotPublished()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;
        var games = new NpgsqlGameStore(source);
        var queue = new NpgsqlSubmissionQueue(source);

        var waiting = await SubmittedAsync(source, games, "quiet-hall", "Quiet Hall", "quiet.example.org");
        var published = await SubmittedAsync(source, games, "loud-hall", "Loud Hall", "loud.example.org");
        await SelfFoundAsync(source, games, "found-hall", "Found Hall", "found.example.org");

        await games.CorroborateAsync(published, DateTimeOffset.UtcNow, ["mssp"], None);

        var pending = await queue.AwaitingAsync(None);

        // The published submission has left, and the game nobody submitted was never in it.
        await Assert.That(pending.Select(row => row.Slug).ToArray()).IsEquivalentTo(["quiet-hall"]);

        var row = pending[0];

        await Assert.That(row.GameId).IsEqualTo(waiting);
        await Assert.That(row.Name).IsEqualTo("Quiet Hall");
        await Assert.That(row.Address).IsEqualTo("quiet.example.org:4201");
    }

    [Test]
    public async Task AnArchivedSubmissionIsNotSomethingToDecideAbout()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;
        var games = new NpgsqlGameStore(source);

        var gone = await SubmittedAsync(source, games, "gone-hall", "Gone Hall", "gone.example.org");

        await games.SetStateAsync(gone, LifecycleState.Archived, DateTimeOffset.UtcNow, None);

        await Assert.That(await new NpgsqlSubmissionQueue(source).AwaitingAsync(None)).IsEmpty();
    }

    [Test]
    public async Task ReleasingOneRecordsThatAPersonDidItAndPublishesTheGame()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;
        var games = new NpgsqlGameStore(source);
        var queue = new NpgsqlSubmissionQueue(source);

        await SubmittedAsync(source, games, "quiet-hall", "Quiet Hall", "quiet.example.org");

        await Assert.That(await new NpgsqlGameQueries(source).FindAsync("quiet-hall")).IsNull();

        var released = await queue.ReleaseAsync("quiet-hall", DateTimeOffset.UtcNow, None);

        await Assert.That(released).IsTrue();
        await Assert.That(await new NpgsqlGameQueries(source).FindAsync("quiet-hall")).IsNotNull();

        await using var connection = await source.OpenConnectionAsync();

        // 'staff' rather than a signal name, because the rubric did not publish this one and a
        // record that said it had would be our judgement dressed as a measurement.
        await Assert.That(await connection.ExecuteScalarAsync<string[]>(
            "SELECT corroborated_by FROM game WHERE slug = 'quiet-hall'")).IsEquivalentTo(["staff"]);
    }

    [Test]
    public async Task ReleasingAGameThatIsNotWaitingChangesNothing()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;
        var games = new NpgsqlGameStore(source);
        var queue = new NpgsqlSubmissionQueue(source);

        var published = await SubmittedAsync(source, games, "loud-hall", "Loud Hall", "loud.example.org");
        var at = DateTimeOffset.UtcNow.AddDays(-1);

        await games.CorroborateAsync(published, at, ["mssp"], None);

        await Assert.That(await queue.ReleaseAsync("loud-hall", DateTimeOffset.UtcNow, None)).IsFalse();
        await Assert.That(await queue.ReleaseAsync("no-such-game", DateTimeOffset.UtcNow, None)).IsFalse();

        await using var connection = await source.OpenConnectionAsync();

        // Its own reasons survive: a release is not a way to overwrite why a game was published.
        await Assert.That(await connection.ExecuteScalarAsync<string[]>(
            "SELECT corroborated_by FROM game WHERE slug = 'loud-hall'")).IsEquivalentTo(["mssp"]);
    }

    private static async Task<Guid> SubmittedAsync(
        NpgsqlDataSource source,
        IGameStore games,
        string slug,
        string name,
        string host) =>
        await InsertAsync(source, games, slug, name, host, DateTimeOffset.UtcNow.AddHours(-6));

    private static async Task<Guid> SelfFoundAsync(
        NpgsqlDataSource source,
        IGameStore games,
        string slug,
        string name,
        string host) =>
        await InsertAsync(source, games, slug, name, host, submittedAt: null);

    private static async Task<Guid> InsertAsync(
        NpgsqlDataSource source,
        IGameStore games,
        string slug,
        string name,
        string host,
        DateTimeOffset? submittedAt)
    {
        var id = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        await games.InsertAsync(
            new GameRecord(
                id,
                slug,
                name,
                Tagline: null,
                LifecycleState.Active,
                IsClaimed: false,
                FirstSeenAt: now.AddHours(-6),
                LastReachableAt: now,
                ArchivedAt: null,
                SubmittedAt: submittedAt),
            None);

        await new NpgsqlEndpointStore(source).UpsertAsync(
            new GameEndpoint(id, host, 4201, EndpointKind.Telnet, now.AddHours(-6), now, EndpointState.Active),
            None);

        return id;
    }
}
