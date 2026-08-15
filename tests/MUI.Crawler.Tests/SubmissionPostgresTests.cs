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
/// A submitted address, from the form to a hidden listing, against a real PostgreSQL.
/// </summary>
/// <remarks>
/// The three halves of this feature meet here and nowhere else: the submission writes a crawl target
/// with a marker on it, the crawl cycle dials it and mints a game, and the game carries the marker
/// that keeps it off the listing until somebody claims it. Each half has its own tests upstream, and
/// none of them would have caught the marker being dropped in between.
/// </remarks>
public class SubmissionPostgresTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static SubmissionService Submissions(
        NpgsqlDataSource source,
        IHostResolver resolver,
        SubmissionOptions? options = null,
        IDnsTxtResolver? txt = null) =>
        new(
            new NpgsqlCrawlTargetRepository(source),
            new CatalogueEndpointDirectory(new NpgsqlEndpointStore(source)),
            new HostScopeGuard(resolver),
            new OptOutGate(
                new NpgsqlCrawlOptOutRepository(source),
                txt ?? new ScriptedDns(),
                TimeProvider.System),
            new NpgsqlSubmissionLog(source),
            options ?? new SubmissionOptions(),
            TimeProvider.System);

    private static CrawlCycle Cycle(
        NpgsqlDataSource source,
        IProbe probe,
        IHostResolver resolver,
        ClaimService? claims = null)
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
        var time = TimeProvider.System;

        return new CrawlCycle(
            targets,
            probe,
            // §11's gate, against the real register: a submitted address is still an address whose
            // operator may have asked us to stop, and the form does not get to override that.
            new OptOutGate(new NpgsqlCrawlOptOutRepository(source), new ScriptedDns(), time),
            new HostScopeGuard(resolver),
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
            time,
            claims);
    }

    /// <summary>
    /// The whole path: submit, crawl, and the game is real and hidden.
    /// </summary>
    [Test]
    public async Task ASubmittedAddressIsCrawledAndTheGameItBecomesIsHiddenUntilClaimed()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;
        var resolver = new FakeHostResolver().Resolving("mud.example.org", "203.0.113.10");

        var receipt = await Submissions(source, resolver)
            .SubmitAsync("mud.example.org", "4201", Source(1), None);

        await Assert.That(receipt.Outcome).IsEqualTo(SubmissionOutcome.Accepted);

        var probe = new ScriptedProbe(target => Probes.Answered(
            host: target.Host,
            port: target.Port,
            mssp: Probes.Mssp(("NAME", "Tidewater Nights"), ("CODEBASE", "PennMUSH 1.8.8p0")),
            banner: "Welcome to Tidewater Nights",
            who: new WhoReading(WhoConfidence.Count, 4)));

        var report = await Cycle(source, probe, resolver).RunAsync();

        await Assert.That(report.Answered).IsEqualTo(1);
        await Assert.That(report.Listed).IsEqualTo(1);

        await using var connection = await source.OpenConnectionAsync();

        // The game exists, is measured, and carries the marker the target was holding for it.
        var game = await connection.QuerySingleAsync<(Guid Id, string Slug, DateTimeOffset? SubmittedAt)>(
            "SELECT id, slug, submitted_at FROM game");

        await Assert.That(game.Slug).IsEqualTo("tidewater-nights");
        await Assert.That(game.SubmittedAt).IsNotNull();

        // And is on none of the site's reads, which is the thing the marker is for.
        var queries = new NpgsqlGameQueries(source);

        await Assert.That(await queries.FindAsync("tidewater-nights")).IsNull();
        await Assert.That((await queries.ListAsync(new GameFilter { IncludeArchived = true })).Count)
            .IsEqualTo(0);

        // Until somebody proves they run it — through the real claiming path, which is the whole of
        // the exit and the thing the first version of this test faked.
        //
        // THE FAKE WAS THE BUG. Flipping is_claimed with SetClaimedAsync proved the query filter
        // opens, and proved nothing about whether anybody could ever make it open: FindAsync is what
        // the claim page looks a game up with, and it had just been taught to hide this game. So the
        // page said "no such game", IssueAsync was unreachable, and hidden-until-claimed was a state
        // with no exit — under a green test.
        var user = await AccountAsync(source);
        var claims = new ClaimService(
            new NpgsqlClaimStore(source), new NpgsqlGameStore(source), TimeProvider.System);

        var claim = await claims.IssueAsync(
            (await new NpgsqlGameStore(source).BySlugAsync("tidewater-nights"))!.Id, user);

        // The operator publishes the token where an anonymous connection reads it, and the next
        // ordinary crawl settles it. Nothing here writes is_claimed.
        var published = new ScriptedProbe(target => Probes.Answered(
            host: target.Host,
            port: target.Port,
            mssp: Probes.Mssp(
                ("NAME", "Tidewater Nights"),
                (ClaimTokenBeacon.MsspVariable, claim.Token)),
            banner: "Welcome to Tidewater Nights"));

        // Waiting out the schedule, without waiting. The first cycle pushed next_probe_at forward,
        // which is §7.7 working; a claimant in production either waits for it or presses "check now".
        await connection.ExecuteAsync("UPDATE crawl_target SET next_probe_at = now()");

        await Cycle(source, published, resolver, claims).RunAsync();

        await Assert.That(await queries.FindAsync("tidewater-nights")).IsNotNull();
        await Assert.That((await queries.ListAsync(new GameFilter())).Count).IsEqualTo(1);
    }

    /// <summary>An account with nothing on it, which is all a claim needs to bind to (§8.1).</summary>
    private static async Task<Guid> AccountAsync(NpgsqlDataSource source)
    {
        var id = Guid.CreateVersion7();

        await using var connection = await source.OpenConnectionAsync();

        await connection.ExecuteAsync(
            """
            INSERT INTO app_user (id, display_name, normalised_name, security_stamp,
                                  concurrency_stamp, created_at)
            VALUES (@id, 'operator', 'OPERATOR', @stamp, @stamp, now())
            """,
            new { id, stamp = Guid.NewGuid().ToString() });

        return id;
    }

    /// <summary>
    /// A submitted address that publishes no name of its own is not listed at all (spec §7.2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The gate is on "a stranger proposed this", and the form is the second way that happens.</b>
    /// Reading it as "referrals only" left submissions outside it, and what that let through was not
    /// a hidden listing — it was <see cref="IdentityMatcher"/>, which runs afterwards. A VPS
    /// answering <c>NAME "Aardwolf"</c> is then scored against the whole catalogue, and short of a
    /// merge it mints a game and takes the <c>aardwolf</c> slug, because slug uniqueness asks the
    /// store and the store does not know a submitted game is hidden. The real one arrives later and
    /// is listed at <c>aardwolf-2</c>, for ever.
    /// </para>
    /// <para>
    /// The cost is §7.2's own and is accepted there: a real game whose operator never edited one
    /// line of MSSP stays unlisted. The target is kept and re-probed for ever, so it lists itself
    /// the moment a name is published.
    /// </para>
    /// </remarks>
    [Test]
    public async Task ASubmittedAddressThatPublishesNoNameOfItsOwnIsNotListed()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;
        var resolver = new FakeHostResolver().Resolving("squatter.example.org", "203.0.113.10");

        await Submissions(source, resolver).SubmitAsync("squatter.example.org", "4201", Source(7), None);

        // Answers, and says nothing about itself that is its own.
        var probe = new ScriptedProbe(target => Probes.Answered(target.Host, target.Port));

        var report = await Cycle(source, probe, resolver).RunAsync();

        await Assert.That(report.Answered).IsEqualTo(1);
        await Assert.That(report.Listed).IsEqualTo(0);

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<int>("SELECT count(*)::int FROM game"))
            .IsEqualTo(0);

        // And is still a target, so it lists itself the moment it publishes a name.
        await Assert.That(await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM crawl_target")).IsEqualTo(1);
    }

    /// <summary>
    /// A submitted address publishing a codebase's own name does not get to be that codebase.
    /// </summary>
    /// <remarks>
    /// §7.3's placeholder rule, reached through the form: <c>NAME "PennMUSH"</c> is what every
    /// unedited PennMUSH on the internet publishes, so it identifies nobody. Admitting it would let
    /// a submitter mint a listing per default install they can point at, and take the slug.
    /// </remarks>
    [Test]
    public async Task ASubmittedAddressPublishingItsCodebasesNameIsNotListed()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;
        var resolver = new FakeHostResolver().Resolving("default.example.org", "203.0.113.10");

        await Submissions(source, resolver).SubmitAsync("default.example.org", "4201", Source(8), None);

        var probe = new ScriptedProbe(target => Probes.Answered(
            target.Host, target.Port, mssp: Probes.Mssp(("NAME", "PennMUSH"))));

        await Cycle(source, probe, resolver).RunAsync();

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<int>("SELECT count(*)::int FROM game"))
            .IsEqualTo(0);
    }

    /// <summary>
    /// A game the crawler found for itself is listed on sight, exactly as §7.1 says.
    /// </summary>
    /// <remarks>
    /// The control for the test above, through the same pipeline. If the marker were being written
    /// for every game rather than only for submitted ones, the whole catalogue would vanish and the
    /// test above would still be green.
    /// </remarks>
    [Test]
    public async Task AGameTheCrawlerFoundForItselfIsListedOnSight()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;
        var resolver = new FakeHostResolver();

        await CrawlSeeds.PlantAsync(
            new NpgsqlCrawlTargetRepository(source),
            [new CrawlSeed("mush.example.org", 4201)],
            TimeProvider.System);

        var probe = new ScriptedProbe(target => Probes.Answered(
            host: target.Host, port: target.Port, mssp: Probes.Mssp(("NAME", "Gaslight Row"))));

        await Cycle(source, probe, resolver).RunAsync();

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<DateTimeOffset?>(
            "SELECT submitted_at FROM game")).IsNull();

        await Assert.That(await new NpgsqlGameQueries(source).FindAsync("gaslight-row")).IsNotNull();
    }

    /// <summary>
    /// Submitting a second port of a game we already list attaches to it and leaves it public.
    /// </summary>
    /// <remarks>
    /// The takeover this feature could otherwise have shipped. The address is not one we hold an
    /// endpoint for, so the submission is accepted and a target is written with the marker on it —
    /// and then §7.3's matcher says the probe <em>is</em> that game, the binder attaches rather than
    /// creating, and no marker is ever written to a game row. A version of this that copied the
    /// marker after binding would let anybody hide any listed game by naming one of its ports.
    /// </remarks>
    [Test]
    public async Task SubmittingASecondPortOfAListedGameDoesNotHideIt()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;
        var resolver = new FakeHostResolver().Resolving("mush.example.org", "203.0.113.10");

        await CrawlSeeds.PlantAsync(
            new NpgsqlCrawlTargetRepository(source),
            [new CrawlSeed("mush.example.org", 4201)],
            TimeProvider.System);

        var probe = new ScriptedProbe(target => Probes.Answered(
            host: target.Host,
            port: target.Port,
            mssp: Probes.Mssp(("NAME", "Tidewater Nights"), ("CREATED", "2004")),
            banner: "Welcome to Tidewater Nights"));

        await Cycle(source, probe, resolver).RunAsync();

        var queries = new NpgsqlGameQueries(source);

        await Assert.That(await queries.FindAsync("tidewater-nights")).IsNotNull();

        // Somebody submits the same game's other port.
        var receipt = await Submissions(source, resolver)
            .SubmitAsync("mush.example.org", "4202", Source(2), None);

        await Assert.That(receipt.Outcome).IsEqualTo(SubmissionOutcome.Accepted);

        await Cycle(source, probe, resolver).RunAsync();

        await using var connection = await source.OpenConnectionAsync();

        // One game, still public, and no marker on it.
        await Assert.That(await connection.ExecuteScalarAsync<int>("SELECT count(*)::int FROM game"))
            .IsEqualTo(1);
        await Assert.That(await connection.ExecuteScalarAsync<DateTimeOffset?>(
            "SELECT submitted_at FROM game")).IsNull();
        await Assert.That(await queries.FindAsync("tidewater-nights")).IsNotNull();
    }

    /// <summary>
    /// An address whose operator asked us to stop is refused at the door (spec §11).
    /// </summary>
    /// <remarks>
    /// Against the register the crawl loop itself reads, so the form and the loop cannot disagree
    /// about who has asked. Both channels are covered: a recorded request, which is one indexed read
    /// and no DNS at all, and a TXT record, which is the route an operator can use without an
    /// account here or MSSP support in their codebase.
    /// </remarks>
    [Test]
    public async Task AnAddressThatAskedUsToStopIsRefusedAtTheDoor()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;
        var resolver = new FakeHostResolver();
        var txt = new ScriptedDns();

        var gate = new OptOutGate(
            new NpgsqlCrawlOptOutRepository(source), txt, TimeProvider.System);

        await gate.RecordRequestAsync("quiet.example.org", null, "asked by mail on 2026-08-14");

        var recorded = await Submissions(source, resolver, txt: txt)
            .SubmitAsync("quiet.example.org", "4201", Source(9), None);

        await Assert.That(recorded.Outcome).IsEqualTo(SubmissionOutcome.RefusedOptOut);

        // The other channel, on a different host, read out of DNS.
        txt.Publishing("txt.example.org", OptOutVocabulary.DnsValue);

        var published = await Submissions(source, resolver, txt: txt)
            .SubmitAsync("txt.example.org", "4201", Source(10), None);

        await Assert.That(published.Outcome).IsEqualTo(SubmissionOutcome.RefusedOptOut);

        await using var connection = await source.OpenConnectionAsync();

        // Nothing to dial, ever: no target, no game, and no measurement of anybody.
        await Assert.That(await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM crawl_target")).IsEqualTo(0);
        await Assert.That(await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM game")).IsEqualTo(0);
        await Assert.That(await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM availability_interval")).IsEqualTo(0);

        // What was written is two submissions saying what we decided, which is where a decision of
        // ours belongs.
        await Assert.That(await connection.QueryAsync<string>(
            "SELECT outcome FROM game_submission ORDER BY submitted_at"))
            .IsEquivalentTo(new[] { "refused_opt_out", "refused_opt_out" });
    }

    /// <summary>
    /// A port-qualified TXT opt-out answers for that port and not for its neighbour.
    /// </summary>
    /// <remarks>
    /// §11 scopes a DNS opt-out to a port when the record names one, because MU* hosting routinely
    /// runs unrelated games on one domain separated only by a port. A form that read
    /// <c>opt-out=4201</c> as covering the host would refuse an address nobody objected to.
    /// </remarks>
    [Test]
    public async Task APortQualifiedOptOutLeavesTheNeighbouringPortSubmittable()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;
        var resolver = new FakeHostResolver();
        var txt = new ScriptedDns().Publishing("shared.example.org", "v=muindex1; opt-out=4201");

        await Assert.That((await Submissions(source, resolver, txt: txt)
            .SubmitAsync("shared.example.org", "4201", Source(11), None)).Outcome)
            .IsEqualTo(SubmissionOutcome.RefusedOptOut);

        await Assert.That((await Submissions(source, resolver, txt: txt)
            .SubmitAsync("shared.example.org", "4000", Source(12), None)).Outcome)
            .IsEqualTo(SubmissionOutcome.Accepted);

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM crawl_target WHERE port = 4000")).IsEqualTo(1);
        await Assert.That(await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM crawl_target WHERE port = 4201")).IsEqualTo(0);
    }

    /// <summary>
    /// A refusal writes a submission row and touches nothing else in the schema.
    /// </summary>
    [Test]
    public async Task ARefusalWritesNoTargetNoGameAndNoAvailability()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;
        var resolver = new FakeHostResolver().Resolving("internal.example.org", "169.254.169.254");

        var receipt = await Submissions(source, resolver)
            .SubmitAsync("internal.example.org", "4201", Source(3), None);

        await Assert.That(receipt.Outcome).IsEqualTo(SubmissionOutcome.RefusedNotRoutable);

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM crawl_target")).IsEqualTo(0);
        await Assert.That(await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM game")).IsEqualTo(0);
        await Assert.That(await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM availability_interval")).IsEqualTo(0);
        await Assert.That(await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM presence_sample")).IsEqualTo(0);

        // The one row it did write says what we decided, and has nowhere to name a game.
        await Assert.That(await connection.ExecuteScalarAsync<string>(
            "SELECT outcome FROM game_submission")).IsEqualTo("refused_not_routable");
    }

    /// <summary>The bound is read out of the table, so several web replicas share one count.</summary>
    [Test]
    public async Task TheRateLimitCountsRowsInTheTable()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;
        var resolver = new FakeHostResolver();
        var options = new SubmissionOptions { PerSource = 2, Window = TimeSpan.FromHours(1) };
        var mine = Source(4);

        // Two different services against one database, which is the deployment this is written for.
        await Submissions(source, resolver, options).SubmitAsync("a.example.org", "4201", mine, None);
        await Submissions(source, resolver, options).SubmitAsync("b.example.org", "4201", mine, None);

        var third = await Submissions(source, resolver, options)
            .SubmitAsync("c.example.org", "4201", mine, None);

        await Assert.That(third.Outcome).IsEqualTo(SubmissionOutcome.TooMany);

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM game_submission")).IsEqualTo(2);
    }

    /// <summary>
    /// Every outcome the service records is one the table's vocabulary accepts.
    /// </summary>
    /// <remarks>
    /// A CHECK constraint and a C# enum are two spellings of one vocabulary, and the day they
    /// disagree is the day a submission throws inside a request handler. Asserted by writing one row
    /// of each rather than by reading the constraint, because reading it would test the reader.
    /// </remarks>
    [Test]
    public async Task EveryRecordedOutcomeIsAValueTheTableAccepts()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var log = new NpgsqlSubmissionLog(database.DataSource);
        var recordable = Enum.GetValues<SubmissionOutcome>()
            .Where(o => o is not SubmissionOutcome.TooMany)
            .ToList();

        foreach (var outcome in recordable)
        {
            var id = Guid.CreateVersion7();

            await Assert.That(await log.TryBeginAsync(
                id, Source(5), DateTimeOffset.UtcNow, recordable.Count, DateTimeOffset.UtcNow.AddHours(-1), None))
                .IsTrue();

            await log.CompleteAsync(
                id,
                outcome is SubmissionOutcome.Malformed ? null : new SubmittedAddress("mud.example.org", 4201),
                outcome,
                // The table refuses an accepted row that names no target, and refuses a target on
                // any other outcome — so this is not decoration, it is the constraint.
                outcome is SubmissionOutcome.Accepted ? await TargetAsync(database.DataSource) : null,
                None);
        }

        await using var connection = await database.DataSource.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM game_submission WHERE outcome <> 'pending'")).IsEqualTo(7);
    }

    /// <summary>
    /// A concurrent burst from one source does not walk through the bound.
    /// </summary>
    /// <remarks>
    /// Against the real database, because this is where it matters: <c>READ COMMITTED</c> lets two
    /// transactions both read a count neither has written to, so counting and then inserting is
    /// check-then-act and a burst passes it entirely. The advisory lock on the source is what makes
    /// the count and the insert one step, and nothing but a real Postgres can be asked whether it
    /// worked.
    /// </remarks>
    [Test]
    public async Task AConcurrentBurstDoesNotWalkThroughTheBound()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;
        var resolver = new FakeHostResolver();
        var options = new SubmissionOptions { PerSource = 3, Window = TimeSpan.FromHours(1) };
        var mine = Source(6);

        // Forty requests at once, each with its own service, as forty concurrent requests across a
        // replica set would be.
        var attempts = await Task.WhenAll(Enumerable.Range(0, 40).Select(i =>
            Submissions(source, resolver, options)
                .SubmitAsync($"burst{i}.example.org", "4201", mine, None)));

        await Assert.That(attempts.Count(r => r.Outcome is not SubmissionOutcome.TooMany)).IsEqualTo(3);

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM game_submission")).IsEqualTo(3);
    }

    /// <summary>
    /// The salt is one row every replica reads, not a value each process invented.
    /// </summary>
    /// <remarks>
    /// A per-process salt reads as the stronger privacy property and removes the bound: two replicas
    /// derive two digests for one address, so five per hour becomes five per replica per hour and a
    /// restart clears it. What §11 asks for is a <em>rotating</em> salt, and a salt that rotates is
    /// one that is stored for the length of an epoch.
    /// </remarks>
    [Test]
    public async Task TheSaltIsSharedAndRotates()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var options = new SubmissionOptions { SaltEpoch = TimeSpan.FromDays(7) };
        var clock = new SettableClock(new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero));

        var replicaA = new NpgsqlSubmissionSalt(database.DataSource, options, clock);
        var replicaB = new NpgsqlSubmissionSalt(database.DataSource, options, clock);

        var a = await replicaA.CurrentAsync(None);
        var b = await replicaB.CurrentAsync(None);

        await Assert.That(a).IsEquivalentTo(b);
        await Assert.That(a.Length).IsGreaterThanOrEqualTo(32);

        // One row, however many replicas asked for it.
        await using var connection = await database.DataSource.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM submission_salt")).IsEqualTo(1);

        // A new epoch is new bytes, so nothing written under the old one can be lined up against
        // anything written under this one.
        clock.Advance(TimeSpan.FromDays(8));

        var rotated = await replicaA.CurrentAsync(None);

        await Assert.That(rotated).IsNotEquivalentTo(a);
        await Assert.That(await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM submission_salt")).IsEqualTo(2);
    }

    private static async Task<Guid> TargetAsync(NpgsqlDataSource source) =>
        await new NpgsqlCrawlTargetRepository(source).AddAsync(
            new CrawlTarget
            {
                Id = Guid.CreateVersion7(),
                Host = "mud.example.org",
                Port = 4201,
                NextProbeAt = DateTimeOffset.UtcNow,
                FirstSeenAt = DateTimeOffset.UtcNow,
                SubmittedAt = DateTimeOffset.UtcNow,
            },
            None);

    /// <summary>A digest-shaped source, which the table's own CHECK requires.</summary>
    private static string Source(int seed) => new string((char)('a' + seed % 6), 64);
}
