using Dapper;

using MUI.Crawl;
using MUI.Crawler.Persistence;
using MUI.Crawler.Tests.Support;
using MUI.Discovery;

namespace MUI.Crawler.Tests;

/// <summary>
/// What is left behind when the crawler declines to dial an address (issue #185).
/// </summary>
/// <remarks>
/// <para>
/// A refusal used to leave nothing. <see cref="CrawlCycle"/> records it through
/// <c>RecordAttemptAsync(succeeded: true)</c> — correct, since the host did not fail — so the target
/// reads as flawless: no failures, a recent attempt, no availability row, and a log line with about
/// thirty minutes of retention. Two of the 1,677 targets in production were in that state on
/// 2026-09-17 and neither was findable except by noticing that <c>next_probe_at</c> happened to be
/// exactly seven days after <c>last_probed_at</c>.
/// </para>
/// <para>
/// <b>None of this reaches a game's record.</b> It is our note about our own decision — the
/// <c>icon_attempt</c> argument — and rule 5 is what keeps it out of <c>availability_interval</c>,
/// where <c>FailureCause.Refused</c> already means something else entirely: an RST from a real host.
/// </para>
/// </remarks>
public class RefusalRecordPostgresTests
{
    /// <summary>The ask itself, in the shape an operator's message arrives in.</summary>
    private static CrawlOptOut Asked() => new()
    {
        Host = "quiet.example.org",
        Port = 4201,
        Source = OptOutSource.Request,
        RecordedAt = DateTimeOffset.UtcNow,
        LastConfirmedAt = DateTimeOffset.UtcNow,
        Detail = "the admin asked in a chat message on 2026-08-16",
    };

    private static ScriptedProbe Answering() => new(target => Probes.Answered(
        host: target.Host,
        port: target.Port,
        mssp: Probes.Mssp(("NAME", "Tidewater Nights")),
        banner: "Welcome to Tidewater Nights"));

    [Test]
    public async Task AnOptedOutAddressLeavesARecordOfWhyItWasNotDialled()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await CrawlCycles.SeedAsync(source, new CrawlSeed("quiet.example.org", 4201));

        await new NpgsqlCrawlOptOutRepository(source).RecordAsync(
            Asked(),
            default);

        await CrawlCycles.Build(source, Answering(), TimeProvider.System).RunAsync();

        await using var connection = await source.OpenConnectionAsync();

        var refusal = await connection.QueryFirstOrDefaultAsync<(string Reason, string Detail, int Times)>(
            """
            SELECT reason AS Reason, detail AS Detail, times AS Times
              FROM crawl_refusal
             WHERE host = 'quiet.example.org' AND port = 4201
            """);

        await Assert.That(refusal.Reason).IsEqualTo("opted_out");
        await Assert.That(refusal.Detail).IsNotNull().And.IsNotEmpty();
        await Assert.That(refusal.Times).IsEqualTo(1);

        // The line rule 5 draws. A refusal is a decision of ours and may not appear anywhere a
        // reader would take it for a measurement of them.
        await Assert.That(await connection.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM availability_interval"))
            .IsEqualTo(0L);
    }

    [Test]
    public async Task ASecondRefusalOfTheSameAddressCountsRatherThanRepeats()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await CrawlCycles.SeedAsync(source, new CrawlSeed("quiet.example.org", 4201));

        await new NpgsqlCrawlOptOutRepository(source).RecordAsync(
            Asked(),
            default);

        await CrawlCycles.Build(source, Answering(), TimeProvider.System).RunAsync();

        await using var connection = await source.OpenConnectionAsync();
        await connection.ExecuteAsync("UPDATE crawl_target SET next_probe_at = now() - interval '1 hour'");

        await CrawlCycles.Build(source, Answering(), TimeProvider.System).RunAsync();

        var row = await connection.QueryFirstAsync<(int Times, DateTimeOffset First, DateTimeOffset Last)>(
            """
            SELECT times AS Times, first_refused_at AS First, last_refused_at AS Last
              FROM crawl_refusal
             WHERE host = 'quiet.example.org' AND port = 4201
            """);

        // Two facts, not one: when we first declined and when we last did. A record that only kept
        // the latest would say nothing about how long this has been standing.
        await Assert.That(row.Times).IsEqualTo(2);
        await Assert.That(row.Last).IsGreaterThanOrEqualTo(row.First);
    }

    /// <summary>
    /// A refusal that no longer stands leaves no row.
    /// </summary>
    /// <remarks>
    /// The <c>icon_attempt</c> lesson, applied before it can bite: a table that only ever grows
    /// becomes a queue nobody reads. An opt-out withdrawn, or a DNS answer that stops carrying a
    /// private address, means there is nothing left to declare — so the row goes and the table means
    /// <em>standing refusals</em> rather than everything that ever happened. The history of the ask
    /// itself lives in <c>crawl_opt_out</c>, which never deletes.
    /// </remarks>
    [Test]
    public async Task AnAddressWeDialAgainStopsBeingListedAsRefused()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await CrawlCycles.SeedAsync(source, new CrawlSeed("quiet.example.org", 4201));

        var optOuts = new NpgsqlCrawlOptOutRepository(source);

        await optOuts.RecordAsync(
            Asked(),
            default);

        await CrawlCycles.Build(source, Answering(), TimeProvider.System).RunAsync();

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM crawl_refusal"))
            .IsEqualTo(1L);

        await optOuts.WithdrawAsync("quiet.example.org", 4201, OptOutSource.Request, DateTimeOffset.UtcNow, default);
        await connection.ExecuteAsync("UPDATE crawl_target SET next_probe_at = now() - interval '1 hour'");

        await CrawlCycles.Build(source, Answering(), TimeProvider.System).RunAsync();

        await Assert.That(await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM crawl_refusal"))
            .IsEqualTo(0L);
    }
}
