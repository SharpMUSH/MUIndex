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
        var slugs = new NpgsqlSlugHistoryStore(source);
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
                new ArchiveSweeper(games, availability, availability),
                new SlugMinter(games, fields, slugs)),
            new CatalogueBinder(
                games,
                endpoints,
                fields,
                slugs,
                new IdentityMatcher(
                    new CatalogueGameDirectory(games),
                    new CatalogueEndpointDirectory(endpoints),
                    fields,
                    new NpgsqlGameFieldIndex(source),
                    discovery),
                new NpgsqlDuplicateReviewRepository(source),
                new NpgsqlMergeLog(source),
                time),
            new ReferralGraphWriter(new NpgsqlReferralRepository(source), targets, discovery, time),
            new CrawlRateLimiter(discovery, time),
            new HostConcurrencyGate(),
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

        // Named over INFO and nothing else. §7.2 lists it; §7.8 does not publish it, which is what
        // leaves a claim as the only way out and keeps this test about claiming. A probe carrying
        // MSSP or a readable WHO would now publish the game before the claim was ever issued.
        var probe = new ScriptedProbe(target => Probes.Answered(
            host: target.Host,
            port: target.Port,
            banner: "Welcome to Tidewater Nights",
            info: """
                ### Begin INFO 1
                Name: Tidewater Nights
                ### End INFO
                """));

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

        // Until somebody proves they run it, through the real claiming path. An earlier version of
        // this test faked it by flipping is_claimed directly, which proved the query filter opens but
        // nothing about whether anybody could ever reach that flip — IssueAsync was unreachable and
        // hidden-until-claimed had no real exit, under a green test.
        var user = await AccountAsync(source);
        var claims = new ClaimService(
            new NpgsqlClaimStore(source), new NpgsqlGameStore(source), TimeProvider.System);

        var claim = await claims.IssueAsync(
            (await new NpgsqlGameStore(source).BySlugAsync("tidewater-nights"))!.Id, user);

        // Published on the connect screen rather than in MSSP (§8.3's second channel), so the probe
        // that settles the claim still carries no §7.8 signal — the game becomes visible because it
        // was claimed, not corroborated on the way past.
        var published = new ScriptedProbe(target => Probes.Answered(
            host: target.Host,
            port: target.Port,
            banner: $"Welcome to Tidewater Nights\n{ClaimTokenBeacon.ConnectScreenPrefix} {claim.Token}",
            info: """
                ### Begin INFO 1
                Name: Tidewater Nights
                ### End INFO
                """));

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
    /// The form is the second way "a stranger proposed this" happens, alongside a referral (see
    /// <c>CatalogueBinder.MayBeListed</c>). The target is kept and re-probed forever, so it lists
    /// itself the moment a name is published.
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
    /// A submitted game that names itself over <c>INFO</c> is listed, MSSP or no MSSP.
    /// </summary>
    /// <remarks>
    /// The real case that motivated this: a RhostMUSH game with no MSSP was refused a listing purely
    /// for lacking telnet option 70, though its <c>INFO</c> block named it plainly. Listed under the
    /// name it gave, not its address or its engine.
    /// </remarks>
    [Test]
    public async Task ASubmittedGameThatNamesItselfOverInfoIsListed()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;
        var resolver = new FakeHostResolver().Resolving("game.example.org", "203.0.113.10");

        await Submissions(source, resolver).SubmitAsync("game.example.org", "10000", Source(11), None);

        var probe = new ScriptedProbe(target => Probes.Answered(
            target.Host,
            target.Port,
            who: new WhoReading(WhoConfidence.Count, 64),
            info: """
                ### Begin INFO 1
                Name: Convergence MUSH
                Connected: 64
                Version: RhostMUSH 4.27.3
                ### End INFO
                """));

        var report = await Cycle(source, probe, resolver).RunAsync();

        await Assert.That(report.Listed).IsEqualTo(1);

        await using var connection = await source.OpenConnectionAsync();

        var game = await connection.QuerySingleAsync<(string Slug, string Name)>(
            "SELECT slug, name FROM game");

        await Assert.That(game.Name).IsEqualTo("Convergence MUSH");
        await Assert.That(game.Slug).IsEqualTo("convergence-mush");
    }

    /// <summary>
    /// A submitted game the probe shows to be a game is public on sight (spec §7.8).
    /// </summary>
    /// <remarks>
    /// The rule this feature exists for: a game that answered every probe with a clear player count
    /// and an <c>INFO</c> block naming itself still sat on no page of the site, waiting on a claim
    /// from an operator with no reason to know the site existed. Nothing about it was uncertain except
    /// who had typed its address.
    /// </remarks>
    [Test]
    public async Task ASubmittedGameThatLooksLikeOneIsPublicWithoutWaitingForAClaim()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;
        var resolver = new FakeHostResolver().Resolving("game.example.org", "203.0.113.10");

        await Submissions(source, resolver).SubmitAsync("game.example.org", "10000", Source(21), None);

        var probe = new ScriptedProbe(target => Probes.Answered(
            target.Host,
            target.Port,
            who: new WhoReading(WhoConfidence.Count, 67),
            info: """
                ### Begin INFO 1
                Name: Convergence MUSH
                Connected: 67
                Version: RhostMUSH 4.27.3
                ### End INFO
                """));

        await Cycle(source, probe, resolver).RunAsync();

        var queries = new NpgsqlGameQueries(source);

        // On the site, with no claim anywhere in the story.
        await Assert.That(await queries.FindAsync("convergence-mush")).IsNotNull();
        await Assert.That((await queries.ListAsync(new GameFilter())).Count).IsEqualTo(1);

        await using var connection = await source.OpenConnectionAsync();

        // And why, kept: an audit that cannot see which signals published a game cannot review the
        // rule that published it.
        var record = await connection.QuerySingleAsync<(DateTime? At, string[]? By)>(
            "SELECT corroborated_at, corroborated_by FROM game");

        await Assert.That(record.At).IsNotNull();
        await Assert.That(record.By).IsEquivalentTo(["who", "codebase"]);

        // The marker itself is untouched. A stranger did hand us this address, and that stays true.
        await Assert.That(await connection.ExecuteScalarAsync<DateTime?>(
            "SELECT submitted_at FROM game")).IsNotNull();
    }

    /// <summary>
    /// A submitted game that says only its own name is listed, hidden, and left for the queue.
    /// </summary>
    /// <remarks>
    /// §7.2 admits it — the server named itself — and §7.8 does not publish it, because naming
    /// yourself is not evidence of being a game: a host with one line of text can do it. This is the
    /// residue <c>mui-crawl submissions</c> exists to show, and the only path out of it is a claim
    /// or an operator's judgement.
    /// </remarks>
    [Test]
    public async Task ASubmittedGameThatShowsNothingButItsNameStaysHidden()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;
        var resolver = new FakeHostResolver().Resolving("quiet.example.org", "203.0.113.10");

        await Submissions(source, resolver).SubmitAsync("quiet.example.org", "4201", Source(22), None);

        var probe = new ScriptedProbe(target => Probes.Answered(
            target.Host,
            target.Port,
            info: """
                ### Begin INFO 1
                Name: Quiet Hall
                ### End INFO
                """));

        var report = await Cycle(source, probe, resolver).RunAsync();

        await Assert.That(report.Listed).IsEqualTo(1);

        var queries = new NpgsqlGameQueries(source);

        await Assert.That(await queries.FindAsync("quiet-hall")).IsNull();
        await Assert.That((await queries.ListAsync(new GameFilter { IncludeArchived = true })).Count)
            .IsEqualTo(0);

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<DateTime?>(
            "SELECT corroborated_at FROM game")).IsNull();
    }

    /// <summary>
    /// A hidden submission publishes itself on the probe that shows what it is, with nobody involved.
    /// </summary>
    /// <remarks>
    /// The same self-healing §7.2 already claims for the name gate: the address is kept and re-probed
    /// for ever (§7.4), so the day an operator switches MSSP on is the day the game appears. Without
    /// this the rule would only ever run once, at creation, and a game that grew a signal afterwards
    /// would stay hidden on the strength of a probe taken before it had one.
    /// </remarks>
    [Test]
    public async Task AHiddenSubmissionPublishesItselfOnTheProbeThatCorroboratesIt()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;
        var resolver = new FakeHostResolver().Resolving("later.example.org", "203.0.113.10");

        await Submissions(source, resolver).SubmitAsync("later.example.org", "4201", Source(23), None);

        var quiet = new ScriptedProbe(target => Probes.Answered(
            target.Host, target.Port, info: "### Begin INFO 1\nName: Late Bloomer\n### End INFO"));

        await Cycle(source, quiet, resolver).RunAsync();

        var queries = new NpgsqlGameQueries(source);

        await Assert.That(await queries.FindAsync("late-bloomer")).IsNull();

        await using var connection = await source.OpenConnectionAsync();

        // Waiting out §7.7's schedule without waiting, as the claim path above does.
        await connection.ExecuteAsync("UPDATE crawl_target SET next_probe_at = now()");

        var announced = new ScriptedProbe(target => Probes.Answered(
            target.Host,
            target.Port,
            mssp: Probes.Mssp(("NAME", "Late Bloomer")),
            info: "### Begin INFO 1\nName: Late Bloomer\n### End INFO"));

        await Cycle(source, announced, resolver).RunAsync();

        await Assert.That(await queries.FindAsync("late-bloomer")).IsNotNull();
        await Assert.That(await connection.ExecuteScalarAsync<string[]>(
            "SELECT corroborated_by FROM game")).IsEquivalentTo(["mssp"]);
    }

    /// <summary>
    /// Corroboration is written once, and keeps the reasons it was written with.
    /// </summary>
    /// <remarks>
    /// The record is what we measured at the moment it published, not a live view of what the game
    /// currently does — rewriting it on every probe would turn an audit trail into a mirror of the
    /// last six hours, and there would be no way to ask why a game was published in the first place.
    /// Presence establishes and absence never revokes (§8.4), so nothing here un-publishes either.
    /// </remarks>
    [Test]
    public async Task CorroborationIsWrittenOnceAndKeepsItsFirstReasons()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;
        var resolver = new FakeHostResolver().Resolving("first.example.org", "203.0.113.10");

        await Submissions(source, resolver).SubmitAsync("first.example.org", "4201", Source(24), None);

        var first = new ScriptedProbe(target => Probes.Answered(
            target.Host,
            target.Port,
            who: new WhoReading(WhoConfidence.Count, 3),
            info: "### Begin INFO 1\nName: First Light\n### End INFO"));

        await Cycle(source, first, resolver).RunAsync();

        await using var connection = await source.OpenConnectionAsync();

        var written = await connection.ExecuteScalarAsync<DateTime>(
            "SELECT corroborated_at FROM game");

        await connection.ExecuteAsync("UPDATE crawl_target SET next_probe_at = now()");

        var richer = new ScriptedProbe(target => Probes.Answered(
            target.Host,
            target.Port,
            mssp: Probes.Mssp(("NAME", "First Light")),
            who: new WhoReading(WhoConfidence.Count, 4),
            info: "### Begin INFO 1\nName: First Light\n### End INFO"));

        await Cycle(source, richer, resolver).RunAsync();

        await Assert.That(await connection.ExecuteScalarAsync<DateTime>(
            "SELECT corroborated_at FROM game")).IsEqualTo(written);
        await Assert.That(await connection.ExecuteScalarAsync<string[]>(
            "SELECT corroborated_by FROM game")).IsEquivalentTo(["who"]);
    }

    /// <summary>
    /// A submitted address that only prints text at us is still not listed.
    /// </summary>
    /// <remarks>
    /// <b>The floor under the widened gate, and the reason a banner is not one of its signals.</b>
    /// Every TCP service on earth can print a paragraph at a stranger, and a submitter chooses the
    /// address — so admitting a banner would make the gate a formality and hand the identity matcher
    /// an unvouched target scored on whatever somebody's web server happened to say. The signals that
    /// do count are all the far end answering a MU\* command about itself.
    /// </remarks>
    [Test]
    public async Task ASubmittedAddressThatOnlyPrintsABannerIsStillNotListed()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;
        var resolver = new FakeHostResolver().Resolving("banner.example.org", "203.0.113.10");

        await Submissions(source, resolver).SubmitAsync("banner.example.org", "4201", Source(12), None);

        var probe = new ScriptedProbe(target => Probes.Answered(
            target.Host, target.Port, banner: "Welcome to Aardwolf! Please enter your name:"));

        var report = await Cycle(source, probe, resolver).RunAsync();

        await Assert.That(report.Listed).IsEqualTo(0);

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<int>("SELECT count(*)::int FROM game"))
            .IsEqualTo(0);
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
    /// The takeover this feature could otherwise have shipped: §7.3's matcher says the probe
    /// <em>is</em> that game, the binder attaches rather than creating, and no marker is ever written
    /// to the game row. Copying the marker after binding would let anybody hide a listed game by
    /// naming one of its ports.
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

        // A whole connect screen, not a line of it: a screen under
        // BannerFingerprint.MinimumIdentifyingLength isn't a signal, rightly — short login prompts
        // are shared across unrelated games.
        var probe = new ScriptedProbe(target => Probes.Answered(
            host: target.Host,
            port: target.Port,
            mssp: Probes.Mssp(("NAME", "Tidewater Nights"), ("CREATED", "2004")),
            banner: "Welcome to Tidewater Nights.\nA harbour town, and a long memory."));

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
    /// Against the register the crawl loop itself reads, so the form and the loop can't disagree about
    /// who has asked. Both channels are covered: a recorded request and a DNS TXT record.
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
    /// Against the real database, since this is where check-then-act races actually happen: under
    /// <c>READ COMMITTED</c>, counting then inserting lets a burst pass the bound entirely without
    /// the advisory lock making the two one step.
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
    /// A per-process salt reads as the stronger privacy property but removes the bound: two replicas
    /// would derive two digests for one address, turning five per hour into five per replica per hour.
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
