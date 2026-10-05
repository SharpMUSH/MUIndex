using Dapper;

using MUI.Catalog.Persistence;
using MUI.Crawl;
using MUI.Crawler.Persistence;
using MUI.Crawler.Tests.Support;
using MUI.Discovery;

using Npgsql;

namespace MUI.Crawler.Tests;

/// <summary>
/// A lead from staff's announcement routine, from <c>crawl_lead_add</c> to the listing, against a
/// real PostgreSQL.
/// </summary>
/// <remarks>
/// In this class rather than its own because a lead is a submission with a different author: it rides
/// the same marker through the same cycle, and the point of these tests is that the marker survives
/// the trip from the lead door exactly as it does from the form.
/// </remarks>
public partial class SubmissionPostgresTests
{
    private static readonly LeadEvidence Announcement = new(
        new Uri("https://www.reddit.com/r/MUD/comments/abc123/tidewater_nights_is_open/"),
        PostUrl: null,
        Channel: "reddit");

    private static LeadService Leads(
        NpgsqlDataSource source,
        IHostResolver resolver,
        LeadOptions? options = null) =>
        new(
            new NpgsqlCrawlTargetRepository(source),
            new CatalogueEndpointDirectory(new NpgsqlEndpointStore(source)),
            new HostScopeGuard(resolver),
            new OptOutGate(new NpgsqlCrawlOptOutRepository(source), new ScriptedDns(), TimeProvider.System),
            new NpgsqlLeadLog(source),
            options ?? new LeadOptions(),
            TimeProvider.System);

    /// <summary>
    /// The case this whole design exists for: a model misreads a port and hands in a web server.
    /// It answers, says nothing of its own, and no game is made of it.
    /// </summary>
    [Test]
    public async Task ALeadThatTurnsOutNotToBeAGameNeverReachesAPage()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;
        var resolver = new FakeHostResolver().Resolving("www.example.org", "203.0.113.10");

        var receipt = await Leads(source, resolver).SubmitAsync("www.example.org", 80, Announcement, ct: None);

        await Assert.That(receipt.Outcome).IsEqualTo(SubmissionOutcome.Accepted);

        var probe = new ScriptedProbe(target => Probes.Answered(
            target.Host, target.Port, banner: "HTTP/1.1 400 Bad Request"));

        var report = await Cycle(source, probe, resolver).RunAsync();

        await Assert.That(report.Answered).IsEqualTo(1);
        await Assert.That(report.Listed).IsEqualTo(0);

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<int>("SELECT count(*)::int FROM game"))
            .IsEqualTo(0);
    }

    /// <summary>
    /// A lead that answers as a game is published by the probe, says only "announcement" about how we
    /// found it, and keeps the page it was read from in our own note and nowhere else.
    /// </summary>
    [Test]
    public async Task ALeadThatAnswersAsAGameIsPublishedAndNamesNoSite()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;
        var resolver = new FakeHostResolver().Resolving("game.example.org", "203.0.113.10");

        await Leads(source, resolver).SubmitAsync("game.example.org", 4201, Announcement, ct: None);

        var probe = new ScriptedProbe(target => Probes.Answered(
            target.Host,
            target.Port,
            who: new WhoReading(WhoConfidence.Count, 12),
            info: """
                ### Begin INFO 1
                Name: Tidewater Nights
                Connected: 12
                Version: PennMUSH 1.8.8
                ### End INFO
                """));

        await Cycle(source, probe, resolver).RunAsync();

        await Assert.That(await new NpgsqlGameQueries(source).FindAsync("tidewater-nights")).IsNotNull();

        await using var connection = await source.OpenConnectionAsync();

        var game = await connection.QuerySingleAsync<(string DiscoveredVia, DateTime? SubmittedAt, DateTime? CorroboratedAt)>(
            "SELECT discovered_via, submitted_at, corroborated_at FROM game");

        await Assert.That(game.DiscoveredVia).IsEqualTo("announcement");
        await Assert.That(game.SubmittedAt).IsNotNull();
        await Assert.That(game.CorroboratedAt).IsNotNull();

        var lead = await connection.QuerySingleAsync<(string Outcome, string EvidenceUrl, Guid? CrawlTargetId)>(
            "SELECT outcome, evidence_url, crawl_target_id FROM crawl_lead");

        await Assert.That(lead.Outcome).IsEqualTo("accepted");
        await Assert.That(lead.EvidenceUrl).IsEqualTo(Announcement.EvidenceUrl.AbsoluteUri);
        await Assert.That(lead.CrawlTargetId).IsNotNull();
    }

    /// <summary>
    /// A lead that only says its own name is minted and kept off the site until §7.8 vouches for it,
    /// the same residue a form submission leaves.
    /// </summary>
    [Test]
    public async Task ALeadThatShowsNothingButItsNameStaysHidden()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;
        var resolver = new FakeHostResolver().Resolving("quiet.example.org", "203.0.113.10");

        await Leads(source, resolver).SubmitAsync("quiet.example.org", 4201, Announcement, ct: None);

        var probe = new ScriptedProbe(target => Probes.Answered(
            target.Host, target.Port, info: "### Begin INFO 1\nName: Quiet Hall\n### End INFO"));

        var report = await Cycle(source, probe, resolver).RunAsync();

        await Assert.That(report.Listed).IsEqualTo(1);
        await Assert.That(await new NpgsqlGameQueries(source).FindAsync("quiet-hall")).IsNull();
    }

    /// <summary>
    /// The C# outcomes and <c>crawl_lead</c>'s CHECK are one vocabulary, and the evidence reads back
    /// as it was written — the post beside the page it led to.
    /// </summary>
    [Test]
    public async Task EveryLeadOutcomeIsAValueTheTableAcceptsAndReadsBack()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var log = new NpgsqlLeadLog(database.DataSource);
        var onTheirSite = new LeadEvidence(
            new Uri("https://tidewater.example.org/connect"),
            new Uri("https://www.reddit.com/r/MUD/comments/abc123/tidewater_nights_is_open/"),
            "reddit");
        var recordable = Enum.GetValues<SubmissionOutcome>()
            .Where(o => o is not SubmissionOutcome.TooMany)
            .ToList();
        var start = DateTimeOffset.UtcNow;

        for (var i = 0; i < recordable.Count; i++)
        {
            var outcome = recordable[i];
            var id = Guid.CreateVersion7();

            await Assert.That(await log.TryBeginAsync(
                id, onTheirSite, start.AddSeconds(i), recordable.Count, start.AddDays(-1), None)).IsTrue();

            await log.CompleteAsync(
                id,
                outcome is SubmissionOutcome.Malformed ? null : new SubmittedAddress("mud.example.org", 4201),
                outcome,
                outcome is SubmissionOutcome.Accepted ? await TargetAsync(database.DataSource) : null,
                None);
        }

        var recent = await log.RecentAsync(100, None);

        await Assert.That(recent.Count).IsEqualTo(recordable.Count);
        await Assert.That(recent.Select(r => r.Outcome)).IsEquivalentTo(
            recordable.Select(o => (SubmissionOutcome?)o).Reverse());
        await Assert.That(recent[0].Evidence).IsEqualTo(onTheirSite);

        // Spent: the bound counts the table, and refusing writes no row.
        await Assert.That(await log.TryBeginAsync(
            Guid.CreateVersion7(), onTheirSite, start.AddMinutes(1), recordable.Count, start.AddDays(-1), None))
            .IsFalse();
    }

    /// <summary>
    /// Abandoning gives back a bare reservation and never touches a lead that reached an outcome.
    /// </summary>
    [Test]
    public async Task AbandoningRemovesOnlyAReservationThatWentNowhere()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var log = new NpgsqlLeadLog(database.DataSource);
        var now = DateTimeOffset.UtcNow;
        var bare = Guid.CreateVersion7();
        var finished = Guid.CreateVersion7();

        await log.TryBeginAsync(bare, Announcement, now, 10, now.AddDays(-1), None);
        await log.TryBeginAsync(finished, Announcement, now, 10, now.AddDays(-1), None);
        await log.CompleteAsync(
            finished, new SubmittedAddress("mud.example.org", 4201), SubmissionOutcome.AlreadyQueued, null, None);

        await log.AbandonAsync(bare, None);
        await log.AbandonAsync(finished, None);

        var left = await log.RecentAsync(10, None);

        await Assert.That(left.Select(r => r.Id)).IsEquivalentTo([finished]);
    }

    /// <summary>The bound holds under a concurrent burst, as the form's does.</summary>
    [Test]
    public async Task AConcurrentBurstOfLeadsDoesNotWalkThroughTheBound()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;
        var options = new LeadOptions { PerWindow = 3 };

        var attempts = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            Leads(source, new FakeHostResolver(), options)
                .SubmitAsync($"burst{i}.example.org", 4201, Announcement, ct: None)));

        await Assert.That(attempts.Count(r => r.Outcome is not SubmissionOutcome.TooMany)).IsEqualTo(3);

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<int>("SELECT count(*)::int FROM crawl_lead"))
            .IsEqualTo(3);
    }
}
