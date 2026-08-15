using Dapper;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawl;
using MUI.Crawler.Persistence;
using MUI.Crawler.Tests.Support;
using MUI.Discovery;

using Npgsql;

namespace MUI.Crawler.Tests;

/// <summary>
/// Spec §11's opt-out through the whole loop and into a real database.
/// </summary>
/// <remarks>
/// <b>The assertions that matter here are the absences.</b> "No availability transition was written",
/// "no presence row", "no field" and "no game" are claims about storage, and an in-memory fake asked
/// what it thinks it wrote could not make them. A refusal happens before a
/// <see cref="ProbeResult"/> exists, so if any of these tables ever gains a row from an opt-out, our
/// own politeness has been recorded as a measurement of somebody's server — the same class of defect
/// as recording an unparseable <c>WHO</c> as zero players.
/// </remarks>
public class OptOutPostgresTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static CrawlCycle Build(
        NpgsqlDataSource source,
        IProbe probe,
        TimeProvider time,
        IDnsTxtResolver? dns = null)
    {
        var discovery = new DiscoveryOptions
        {
            GlobalInterval = TimeSpan.Zero,
            PerHostInterval = TimeSpan.Zero,
        };

        var games = new NpgsqlGameStore(source);
        var endpoints = new NpgsqlEndpointStore(source);
        var fields = new NpgsqlGameFieldStore(source);
        var availability = new NpgsqlAvailabilityStore(source);
        var targets = new NpgsqlCrawlTargetRepository(source);

        return new CrawlCycle(
            targets,
            probe,
            new OptOutGate(new NpgsqlCrawlOptOutRepository(source), dns ?? new ScriptedDns(), time),
            new HostScopeGuard(new FakeHostResolver()),
            new ProbeIngestor(
                new PresenceWriter(new NpgsqlPresenceStore(source)),
                new AvailabilityWriter(availability),
                new FieldReconciler(fields),
                games,
                new ArchiveSweeper(games, availability, availability)),
            new CatalogueBinder(
                games,
                endpoints,
                fields,
                new IdentityMatcher(
                    new CatalogueGameDirectory(games),
                    new CatalogueEndpointDirectory(endpoints),
                    fields,
                    new NpgsqlGameFieldIndex(source),
                    discovery),
                new NpgsqlDuplicateReviewRepository(source),
                time),
            new ReferralGraphWriter(new NpgsqlReferralRepository(source), targets, discovery, time),
            new CrawlRateLimiter(discovery, time),
            new HostGate(),
            discovery,
            time);
    }

    private static async Task SeedAsync(NpgsqlDataSource source, params CrawlSeed[] seeds) =>
        await CrawlSeeds.PlantAsync(new NpgsqlCrawlTargetRepository(source), seeds, TimeProvider.System);

    private static async Task MakeDueAsync(NpgsqlDataSource source)
    {
        await using var connection = await source.OpenConnectionAsync();
        await connection.ExecuteAsync("UPDATE crawl_target SET next_probe_at = now() - interval '1 minute'");
    }

    [Test]
    public async Task AGameThatPublishesTheMsspFieldIsNotDialledAgainAndKeepsWhatWeMeasured()
    {
        // §11 in one test: honoured within one cycle, recorded, and nothing that was already measured
        // is touched. The probe that carried the field is the last one; what it read is stored,
        // because nothing here is ever deleted and the about page says exactly that.
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await SeedAsync(source, new CrawlSeed("corvid.example.org", 4201));

        var probe = new ScriptedProbe(target => Probes.Answered(
            host: target.Host,
            port: target.Port,
            mssp: Probes.Mssp(
                ("NAME", "Corvid Hall"),
                (OptOutVocabulary.MsspVariable, "1")),
            who: new WhoReading(WhoConfidence.Count, 3)));

        var cycle = Build(source, probe, TimeProvider.System);

        var first = await cycle.RunAsync();

        await Assert.That(first.Answered).IsEqualTo(1);
        await Assert.That(first.Listed).IsEqualTo(1);

        await MakeDueAsync(source);

        var second = await cycle.RunAsync();

        await Assert.That(second.OptedOut).IsEqualTo(1);
        await Assert.That(second.Probed).IsEqualTo(0);
        await Assert.That(probe.Asked).Count().IsEqualTo(1);

        await using var connection = await source.OpenConnectionAsync();

        // What the one probe measured is still there, and the refusal added nothing to any of it.
        await Assert.That(await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM presence_sample"))
            .IsEqualTo(1L);
        await Assert.That(await connection.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM availability_interval"))
            .IsEqualTo(1L);
        await Assert.That(await connection.ExecuteScalarAsync<string>(
                "SELECT state FROM availability_interval"))
            .IsEqualTo("reachable");
        await Assert.That(await connection.ExecuteScalarAsync<string>("SELECT state FROM game"))
            .IsEqualTo("active");

        // And the ask itself is recorded where decisions of ours are recorded, with what we read.
        var recorded = await connection.QuerySingleAsync<(string Host, int? Port, string Source, string Detail)>(
            "SELECT host, port, source, detail FROM crawl_opt_out");

        await Assert.That(recorded.Host).IsEqualTo("corvid.example.org");
        await Assert.That(recorded.Port).IsEqualTo(4201);
        await Assert.That(recorded.Source).IsEqualTo("mssp");
        await Assert.That(recorded.Detail).Contains(OptOutVocabulary.MsspVariable);

        // The target keeps its row and its schedule. Nothing is retired: the day they ask us back it
        // is one timestamp in crawl_opt_out and the address is due again.
        await Assert.That(await connection.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM crawl_target WHERE host = 'corvid.example.org'"))
            .IsEqualTo(1L);
        await Assert.That(await connection.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM crawl_target WHERE next_probe_at > now()"))
            .IsEqualTo(1L);
    }

    [Test]
    public async Task AHostThatOptedOutInDnsIsNeverDialledAndGetsNoRecordAtAll()
    {
        // The route for an address we have never listed, which is most of them. It must work with no
        // game, no claim and no account — and it must leave the catalogue exactly as empty as it was.
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await SeedAsync(source, new CrawlSeed("quiet.example.org", 4201));

        var probe = new ScriptedProbe(_ =>
            throw new InvalidOperationException("A host that opted out must not be dialled."));

        var dns = new ScriptedDns().Publishing("quiet.example.org", OptOutVocabulary.DnsValue);

        var report = await Build(source, probe, TimeProvider.System, dns).RunAsync();

        await Assert.That(report.OptedOut).IsEqualTo(1);
        await Assert.That(report.Refused).IsEqualTo(0);
        await Assert.That(report.Probed).IsEqualTo(0);
        await Assert.That(probe.Asked).IsEmpty();

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM game")).IsEqualTo(0L);
        await Assert.That(await connection.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM availability_interval"))
            .IsEqualTo(0L);
        await Assert.That(await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM presence_sample"))
            .IsEqualTo(0L);
        await Assert.That(await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM game_field"))
            .IsEqualTo(0L);

        // Host-wide, as an unqualified record is, and rescheduled rather than left due for ever.
        await Assert.That(await connection.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM crawl_opt_out WHERE port IS NULL AND source = 'dns_txt'"))
            .IsEqualTo(1L);
        await Assert.That(await connection.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM crawl_target WHERE next_probe_at > now()"))
            .IsEqualTo(1L);
    }

    [Test]
    public async Task AnOptOutIsCountedApartFromASecurityRefusal()
    {
        // Two decisions of ours, and a single "refused" figure would hide somebody's stated wishes
        // inside our own network policy.
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await SeedAsync(
            source,
            new CrawlSeed("quiet.example.org", 4201),
            new CrawlSeed("internal.example.org", 4201));

        var probe = new ScriptedProbe(_ => throw new InvalidOperationException("Nothing here is dialled."));
        var dns = new ScriptedDns().Publishing("quiet.example.org", OptOutVocabulary.DnsValue);

        var discovery = new HostScopeGuard(
            new FakeHostResolver().Resolving("internal.example.org", "169.254.169.254"));

        var games = new NpgsqlGameStore(source);
        var availability = new NpgsqlAvailabilityStore(source);
        var targets = new NpgsqlCrawlTargetRepository(source);
        var fields = new NpgsqlGameFieldStore(source);
        var endpoints = new NpgsqlEndpointStore(source);
        var options = new DiscoveryOptions { GlobalInterval = TimeSpan.Zero, PerHostInterval = TimeSpan.Zero };
        var time = TimeProvider.System;

        var cycle = new CrawlCycle(
            targets,
            probe,
            new OptOutGate(new NpgsqlCrawlOptOutRepository(source), dns, time),
            discovery,
            new ProbeIngestor(
                new PresenceWriter(new NpgsqlPresenceStore(source)),
                new AvailabilityWriter(availability),
                new FieldReconciler(fields),
                games,
                new ArchiveSweeper(games, availability, availability)),
            new CatalogueBinder(
                games,
                endpoints,
                fields,
                new IdentityMatcher(
                    new CatalogueGameDirectory(games),
                    new CatalogueEndpointDirectory(endpoints),
                    fields,
                    new NpgsqlGameFieldIndex(source),
                    options),
                new NpgsqlDuplicateReviewRepository(source),
                time),
            new ReferralGraphWriter(new NpgsqlReferralRepository(source), targets, options, time),
            new CrawlRateLimiter(options, time),
            new HostGate(),
            options,
            time);

        var report = await cycle.RunAsync();

        await Assert.That(report.OptedOut).IsEqualTo(1);
        await Assert.That(report.Refused).IsEqualTo(1);
        await Assert.That(report.ToString()).Contains("1 opted out");
    }

    [Test]
    public async Task ARequestedOptOutStopsTheNextCycleAndSaysWhoAsked()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await SeedAsync(source, new CrawlSeed("mush.example.org", 4201));

        var gate = new OptOutGate(
            new NpgsqlCrawlOptOutRepository(source), new ScriptedDns(), TimeProvider.System);

        await gate.RecordRequestAsync(
            "MUSH.Example.ORG.", null, "mail from rowan@example.org, 2026-08-14", None);

        var probe = new ScriptedProbe(_ =>
            throw new InvalidOperationException("A recorded request must stop the dial."));

        var report = await Build(source, probe, TimeProvider.System).RunAsync();

        await Assert.That(report.OptedOut).IsEqualTo(1);

        await using var connection = await source.OpenConnectionAsync();

        // Canonicalised on the way in, or the crawl loop would look up a spelling that is not there.
        var row = await connection.QuerySingleAsync<(string Host, string Source, string Detail)>(
            "SELECT host, source, detail FROM crawl_opt_out");

        await Assert.That(row.Host).IsEqualTo("mush.example.org");
        await Assert.That(row.Source).IsEqualTo("request");
        await Assert.That(row.Detail).Contains("rowan@example.org");
    }

    [Test]
    public async Task TakingBackATxtRecordPutsTheAddressBackOnTheSchedule()
    {
        // The half of an opt-out that has to work or the whole thing is a trap: the DNS route is
        // undoable by the operator alone, and within one cycle, because we ask every time.
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await SeedAsync(source, new CrawlSeed("returning.example.org", 4201));

        var probe = new ScriptedProbe(target => Probes.Answered(
            host: target.Host, port: target.Port, mssp: Probes.Mssp(("NAME", "Back Again"))));

        var dns = new ScriptedDns().Publishing("returning.example.org", OptOutVocabulary.DnsValue);
        var cycle = Build(source, probe, TimeProvider.System, dns);

        await Assert.That((await cycle.RunAsync()).OptedOut).IsEqualTo(1);

        // The record comes down.
        dns.Publishing("returning.example.org");

        await MakeDueAsync(source);

        var second = await cycle.RunAsync();

        await Assert.That(second.OptedOut).IsEqualTo(0);
        await Assert.That(second.Answered).IsEqualTo(1);
        await Assert.That(second.Listed).IsEqualTo(1);

        await using var connection = await source.OpenConnectionAsync();

        // Withdrawn, and still on the record. Nothing is ever deleted, including our own decisions.
        var row = await connection.QuerySingleAsync<(DateTimeOffset RecordedAt, DateTimeOffset? WithdrawnAt)>(
            "SELECT recorded_at, withdrawn_at FROM crawl_opt_out");

        await Assert.That(row.WithdrawnAt).IsNotNull();
        await Assert.That(row.WithdrawnAt!.Value).IsGreaterThanOrEqualTo(row.RecordedAt);
    }

    [Test]
    public async Task TheRegisterKeepsTheFirstAskAndMovesOnlyTheConfirmation()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        var register = new NpgsqlCrawlOptOutRepository(source);
        var first = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await register.RecordAsync(
            new CrawlOptOut
            {
                Host = "corvid.example.org",
                Port = null,
                Source = OptOutSource.DnsTxt,
                RecordedAt = first,
                LastConfirmedAt = first,
                Detail = "TXT opt-out",
            },
            None);

        var confirmed = await register.RecordAsync(
            new CrawlOptOut
            {
                Host = "corvid.example.org",
                Port = null,
                Source = OptOutSource.DnsTxt,
                RecordedAt = first.AddDays(30),
                LastConfirmedAt = first.AddDays(30),
                Detail = "TXT opt-out",
            },
            None);

        await Assert.That(confirmed.IsFirstAsk).IsFalse();
        await Assert.That(confirmed.OptOut.RecordedAt).IsEqualTo(first);
        await Assert.That(confirmed.OptOut.LastConfirmedAt).IsEqualTo(first.AddDays(30));
        await Assert.That((await register.AllAsync(None))).Count().IsEqualTo(1);

        // One route withdrawing never speaks for another, and neither leaves the table.
        await register.RecordAsync(
            new CrawlOptOut
            {
                Host = "corvid.example.org",
                Port = 4201,
                Source = OptOutSource.Request,
                RecordedAt = first,
                LastConfirmedAt = first,
                Detail = "mail from rowan@example.org",
            },
            None);

        await register.WithdrawAsync("corvid.example.org", null, OptOutSource.DnsTxt, first.AddDays(31), None);

        var standing = await register.StandingAsync("corvid.example.org", 4201, None);

        await Assert.That(standing).IsNotNull();
        await Assert.That(standing!.Source).IsEqualTo(OptOutSource.Request);
        await Assert.That(await register.StandingAsync("corvid.example.org", 4202, None)).IsNull();
        await Assert.That(await register.AllAsync(None)).Count().IsEqualTo(2);
    }

    [Test]
    public async Task TheRegisterSaysWhetherItHeardThisBeforeRatherThanLeavingItToTheClock()
    {
        // timestamptz keeps microseconds and DateTimeOffset counts 100ns ticks, so a date written
        // here comes back a different value and "is this the first ask" cannot be a comparison
        // against the clock. The register knows, because it is the thing that inserted or did not.
        await using var database = await PostgresFixture.MigratedAsync();
        var register = new NpgsqlCrawlOptOutRepository(database.DataSource);

        var asked = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(1234567);

        var first = await register.RecordAsync(
            new CrawlOptOut
            {
                Host = "corvid.example.org",
                Source = OptOutSource.DnsTxt,
                RecordedAt = asked,
                LastConfirmedAt = asked,
                Detail = "TXT opt-out",
            },
            None);

        await Assert.That(first.IsFirstAsk).IsTrue();

        // The round trip that made the old check unreliable, asserted rather than assumed.
        await Assert.That(first.OptOut.RecordedAt).IsNotEqualTo(asked);

        var again = await register.RecordAsync(
            new CrawlOptOut
            {
                Host = "corvid.example.org",
                Source = OptOutSource.DnsTxt,
                RecordedAt = asked.AddDays(1).AddTicks(4321),
                LastConfirmedAt = asked.AddDays(1).AddTicks(4321),
                Detail = "TXT opt-out",
            },
            None);

        await Assert.That(again.IsFirstAsk).IsFalse();
        await Assert.That(again.OptOut.RecordedAt).IsEqualTo(first.OptOut.RecordedAt);
    }

    [Test]
    public async Task AHostDnsCannotBeAskedAboutIsProbedAndRescheduledRatherThanStuck()
    {
        // A REFERRAL naming a 64-byte label passes ReferralCandidate's plausibility check, and the
        // opt-out lookup is now the first thing that touches it. If the lookup throws, the crawl
        // loop's catch-all counts an error and never records the attempt — so the target stays due
        // and burns a batch slot every cycle, for ever, on a name a stranger chose.
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        var host = new string('a', 64) + ".example.org";

        await SeedAsync(source, new CrawlSeed(host, 4201));

        var probe = new ScriptedProbe(target => Probes.Answered(
            host: target.Host, port: target.Port, mssp: Probes.Mssp(("NAME", "Long Name Hall"))));

        // The real resolver, because the point is what DnsClient does with a name it will not send.
        var report = await Build(source, probe, TimeProvider.System, new DnsTxtResolver()).RunAsync();

        await Assert.That(report.Errored).IsEqualTo(0);
        await Assert.That(report.OptedOut).IsEqualTo(0);
        await Assert.That(report.Answered).IsEqualTo(1);

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM crawl_target WHERE next_probe_at > now()"))
            .IsEqualTo(1L);
    }

    [Test]
    public async Task ATargetThatThrewIsBackedOffRatherThanLeftDueForEver()
    {
        // Defence in depth for the same failure: whatever throws, a target that is not rescheduled is
        // re-selected by DueAsync every cycle and crowds out the ones that would have been probed.
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await SeedAsync(source, new CrawlSeed("explosive.example.org", 4201));

        var probe = new ScriptedProbe(_ => throw new InvalidOperationException("the probe exploded"));

        var report = await Build(source, probe, TimeProvider.System).RunAsync();

        await Assert.That(report.Errored).IsEqualTo(1);

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM crawl_target WHERE next_probe_at > now()"))
            .IsEqualTo(1L);

        // Backed off as a failure, because that is what an attempt that did not complete is.
        await Assert.That(await connection.ExecuteScalarAsync<int>(
                "SELECT consecutive_failures FROM crawl_target"))
            .IsEqualTo(1);
    }

    [Test]
    public async Task TheTableRefusesAnOptOutNobodyCouldExplainLater()
    {
        // Half this design lives in CHECK constraints, and this one is the ContactedMaintainer rule
        // in the schema: a claim about somebody else's wishes with no account of who made it.
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(async () => await connection.ExecuteAsync(
                """
                INSERT INTO crawl_opt_out (id, host, port, source, recorded_at, last_confirmed_at, detail)
                VALUES (@id, 'corvid.example.org', NULL, 'request', now(), now(), '   ')
                """,
                new { id = Guid.CreateVersion7() }))
            .Throws<PostgresException>();

        await Assert.That(async () => await connection.ExecuteAsync(
                """
                INSERT INTO crawl_opt_out (id, host, port, source, recorded_at, last_confirmed_at, detail)
                VALUES (@id, 'Corvid.Example.ORG.', NULL, 'request', now(), now(), 'shouty')
                """,
                new { id = Guid.CreateVersion7() }))
            .Throws<PostgresException>();

        await Assert.That(async () => await connection.ExecuteAsync(
                """
                INSERT INTO crawl_opt_out (id, host, port, source, recorded_at, last_confirmed_at, detail)
                VALUES (@id, 'corvid.example.org', NULL, 'because-we-felt-like-it', now(), now(), 'why')
                """,
                new { id = Guid.CreateVersion7() }))
            .Throws<PostgresException>();
    }
}
