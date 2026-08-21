using Dapper;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawler.Tests.Support;
using MUI.Discovery;

using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

namespace MUI.Crawler.Tests;

/// <summary>
/// Spec §8.3's DNS channel end to end: a zone file, a real database, and what a claim becomes.
/// </summary>
/// <remarks>
/// The grammar has its own unit tests in <c>MUI.Discovery.Tests</c>. What is here is the part a
/// parser cannot answer — which name gets asked about, how many times, and what the claim row looks
/// like afterwards — plus the two absences that matter: no lookup for a game nobody is claiming, and
/// no revocation when a record disappears.
/// </remarks>
public class DnsClaimPostgresTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static readonly DateTimeOffset Then = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task APortQualifiedRecordVerifiesAPendingClaim()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        var game = await GameAsync(source, "corvid");
        await EndpointAsync(source, game, "corvid.example.org", 4201);

        var claims = Claims(source);
        var token = (await claims.IssueAsync(game, await AccountAsync(source))).Token;

        var dns = new ScriptedDns().Publishing("corvid.example.org", $"{token}=4201");

        await Assert.That(await Verifier(source, dns).CheckAsync(game, None))
            .IsEqualTo(ClaimVerdict.Verified);

        var settled = (await new NpgsqlClaimStore(source).ForGameAsync(game, None)).Single();

        await Assert.That(settled.IsVerified).IsTrue();
        await Assert.That(settled.VerifiedVia).IsEqualTo(ClaimChannel.DnsTxt);
        await Assert.That(await IsClaimedAsync(source, game)).IsTrue();
        await Assert.That(dns.Asked).Contains("_muindex.corvid.example.org");
    }

    /// <summary>
    /// A lookup costs a query to somebody else's resolver, so it is spent only where it could matter.
    /// </summary>
    /// <remarks>
    /// The gate is a claim, not a game: a sweep over the whole catalogue asking DNS about every host
    /// we list would be a standing load on strangers' nameservers, in aid of a channel that almost
    /// none of them use.
    /// </remarks>
    [Test]
    public async Task NoLookupHappensForAGameNobodyIsClaiming()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        var game = await GameAsync(source, "corvid");
        await EndpointAsync(source, game, "corvid.example.org", 4201);

        var dns = new ScriptedDns();

        await Assert.That(await Verifier(source, dns).CheckAsync(game, None))
            .IsEqualTo(ClaimVerdict.NothingToDo);
        await Assert.That(dns.Asked).IsEmpty();
    }

    /// <summary>One name per host, not one per port — a game on six ports is one zone.</summary>
    [Test]
    public async Task OneLookupCoversEveryPortAGameHasOnAHost()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        var game = await GameAsync(source, "corvid");
        await EndpointAsync(source, game, "corvid.example.org", 4201);
        await EndpointAsync(source, game, "corvid.example.org", 4202);

        var claims = Claims(source);
        var token = (await claims.IssueAsync(game, await AccountAsync(source))).Token;

        var dns = new ScriptedDns().Publishing("corvid.example.org", $"{token}=4202");

        await Assert.That(await Verifier(source, dns).CheckAsync(game, None))
            .IsEqualTo(ClaimVerdict.Verified);
        await Assert.That(dns.Asked).Count().IsEqualTo(1);
    }

    /// <summary>
    /// An address the game has moved off is not an address it may be claimed at (§5.5's <c>gone</c>).
    /// </summary>
    /// <remarks>
    /// A gone endpoint is kept for ever and still probed at the §7.4 floor, so it is exactly the sort
    /// of row that outlives whoever holds the domain now. Verifying against one would let the next
    /// tenant of an expired hostname claim a game that left it years ago.
    /// </remarks>
    [Test]
    public async Task ARecordOnAnAddressTheGameHasMovedOffVerifiesNothing()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        var game = await GameAsync(source, "corvid");
        await EndpointAsync(source, game, "old.example.org", 4201, EndpointState.Gone);

        var claims = Claims(source);
        var token = (await claims.IssueAsync(game, await AccountAsync(source))).Token;

        var dns = new ScriptedDns().Publishing("old.example.org", $"{token}=4201");

        await Assert.That(await Verifier(source, dns).CheckAsync(game, None))
            .IsEqualTo(ClaimVerdict.NothingToDo);
        await Assert.That(dns.Asked).IsEmpty();
    }

    /// <summary>A resolver that did not answer is not an absence, and settles nothing either way.</summary>
    [Test]
    public async Task AResolverThatDidNotAnswerConcludesNothing()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        var game = await GameAsync(source, "corvid");
        await EndpointAsync(source, game, "corvid.example.org", 4201);

        var claims = Claims(source);
        var claim = await claims.IssueAsync(game, await AccountAsync(source));

        var dns = new ScriptedDns().Silent("corvid.example.org");

        await Assert.That(await Verifier(source, dns).CheckAsync(game, None))
            .IsEqualTo(ClaimVerdict.NothingToDo);

        var still = (await new NpgsqlClaimStore(source).ForGameAsync(game, None)).Single();

        await Assert.That(still.IsPending(Then)).IsTrue();
        await Assert.That(still.Id).IsEqualTo(claim.Id);
    }

    /// <summary>
    /// §8.4 — the record going away never revokes what it proved.
    /// </summary>
    [Test]
    public async Task DeletingTheRecordLeavesTheClaimVerified()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        var game = await GameAsync(source, "corvid");
        await EndpointAsync(source, game, "corvid.example.org", 4201);

        var claims = Claims(source);
        var token = (await claims.IssueAsync(game, await AccountAsync(source))).Token;

        var dns = new ScriptedDns().Publishing("corvid.example.org", $"{token}=4201");
        await Verifier(source, dns).CheckAsync(game, None);

        // The zone is edited and the record is gone. DNS answers, and says there is nothing there.
        var quiet = new ScriptedDns();

        await Assert.That(await Verifier(source, quiet).CheckAsync(game, None))
            .IsEqualTo(ClaimVerdict.NothingToDo);

        var settled = (await new NpgsqlClaimStore(source).ForGameAsync(game, None)).Single();

        await Assert.That(settled.IsVerified).IsTrue();
        await Assert.That(await IsClaimedAsync(source, game)).IsTrue();
    }

    /// <summary>
    /// A shared host is the case §8.3 was worried about, and the token is what bounds it.
    /// </summary>
    /// <remarks>
    /// Two unrelated games behind one domain, separated only by port. The token is minted per
    /// (account, game), so publishing one game's token against another's port proves nothing about
    /// either — the qualifier keeps the record off the wrong listener, and the token keeps it off the
    /// wrong claim even if the qualifier is wrong.
    /// </remarks>
    [Test]
    public async Task ATokenIssuedForOneGameCannotVerifyItsNeighbourOnTheSameHost()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        var mine = await GameAsync(source, "corvid");
        var theirs = await GameAsync(source, "nightfall");
        await EndpointAsync(source, mine, "shared.example.org", 4201);
        await EndpointAsync(source, theirs, "shared.example.org", 4202);

        var claims = Claims(source);
        var account = await AccountAsync(source);
        var mineToken = (await claims.IssueAsync(mine, account)).Token;
        await claims.IssueAsync(theirs, account);

        // My token, qualified for my neighbour's port.
        var dns = new ScriptedDns().Publishing("shared.example.org", $"{mineToken}=4202");

        await Assert.That(await Verifier(source, dns).CheckAsync(mine, None))
            .IsEqualTo(ClaimVerdict.NothingToDo);
        await Assert.That(await Verifier(source, dns).CheckAsync(theirs, None))
            .IsEqualTo(ClaimVerdict.NothingToDo);

        var store = new NpgsqlClaimStore(source);

        await Assert.That((await store.ForGameAsync(mine, None)).Single().IsVerified).IsFalse();
        await Assert.That((await store.ForGameAsync(theirs, None)).Single().IsVerified).IsFalse();
    }

    /// <summary>
    /// Only claims a lookup could change are swept, and an expired or revoked one is not one of them.
    /// </summary>
    /// <remarks>
    /// A verified DNS claim stays in the set so <c>beacon_last_seen_at</c> keeps moving while the
    /// record is up — §8.4's second timestamp is worthless if nothing ever writes it. A claim
    /// verified on the wire is not swept: DNS did not prove it and re-reading a zone would say
    /// nothing about whether the listener still publishes anything.
    /// </remarks>
    [Test]
    public async Task TheSweepSetIsLiveClaimsAndTheOnesDnsItselfProved()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;
        var store = new NpgsqlClaimStore(source);
        var now = DateTimeOffset.UtcNow;

        var pending = await GameAsync(source, "pending");
        var byDns = await GameAsync(source, "by-dns");
        var byWire = await GameAsync(source, "by-wire");
        var expired = await GameAsync(source, "expired");
        var revoked = await GameAsync(source, "revoked");

        var claims = Claims(source, now);

        await claims.IssueAsync(pending, await AccountAsync(source));

        await EndpointAsync(source, byDns, "by-dns.example.org", 4201);
        var dnsToken = (await claims.IssueAsync(byDns, await AccountAsync(source))).Token;
        await new DnsClaimVerifier(
                store,
                new NpgsqlEndpointStore(source),
                new ScriptedDns().Publishing("by-dns.example.org", $"{dnsToken}=4201"),
                claims,
                new SettableClock(now))
            .CheckAsync(byDns, None);

        var wireClaim = await claims.IssueAsync(byWire, await AccountAsync(source));
        await claims.OfferBeaconAsync(byWire, wireClaim.Token, ClaimChannel.Mssp, None);

        // Issued long enough ago that its own thirty days ran out, rather than edited to look that
        // way: expires_at > issued_at is a CHECK, so a claim cannot be back-dated into expiry.
        await Claims(source, now - ClaimToken.PendingLifetime - TimeSpan.FromDays(1))
            .IssueAsync(expired, await AccountAsync(source));

        var withdrawn = await claims.IssueAsync(revoked, await AccountAsync(source));
        await claims.RevokeAsync(withdrawn.Id, "changed their mind", None);

        var swept = (await store.PendingOrDnsVerifiedAsync(now, None))
            .Select(claim => claim.GameId)
            .ToList();

        await Assert.That(swept).Contains(pending);
        await Assert.That(swept).Contains(byDns);
        await Assert.That(swept).DoesNotContain(byWire);
        await Assert.That(swept).DoesNotContain(expired);
        await Assert.That(swept).DoesNotContain(revoked);
    }

    /// <summary>
    /// §8.1's promise holds for this channel too: publish it and we notice, without being asked.
    /// </summary>
    /// <remarks>
    /// The claim page's check button is the fast path, not the only one. An operator who added the
    /// record and closed the tab has done everything we asked of them, and a channel that only ever
    /// verified on a button press would be a channel with a footnote.
    /// </remarks>
    [Test]
    public async Task TheSweepVerifiesAClaimNobodyPressedTheButtonFor()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        var game = await GameAsync(source, "corvid");
        await EndpointAsync(source, game, "corvid.example.org", 4201);

        var now = DateTimeOffset.UtcNow;
        var token = (await Claims(source, now).IssueAsync(game, await AccountAsync(source))).Token;

        var dns = new ScriptedDns().Publishing("corvid.example.org", $"{token}=4201");

        using var sweeper = Sweeper(database, dns, now);
        await sweeper.StartAsync(None);

        var store = new NpgsqlClaimStore(source);
        var verified = await UntilAsync(async () =>
            (await store.ForGameAsync(game, None)).Single().IsVerified);

        await sweeper.StopAsync(None);

        await Assert.That(verified).IsTrue();
    }

    /// <summary>The lease is its own, so a long crawl cycle cannot hold up a claim (§12).</summary>
    [Test]
    public async Task TheDnsClaimLeaseIsNotTheCrawlLease()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var other = database.SecondPool();

        await using var crawl = await AdvisoryLease.TryAcquireAsync(database.DataSource, AdvisoryLease.CrawlKey);
        await using var sweep = await AdvisoryLease.TryAcquireAsync(other, AdvisoryLease.DnsClaimKey);

        await Assert.That(crawl).IsNotNull();
        await Assert.That(sweep).IsNotNull();
    }

    private static DnsClaimSweeper Sweeper(TestDatabase database, IDnsTxtResolver dns, DateTimeOffset now)
    {
        var source = database.DataSource;
        var store = new NpgsqlClaimStore(source);

        return new DnsClaimSweeper(
            source,
            new DnsClaimVerifier(
                store, new NpgsqlEndpointStore(source), dns, Claims(source, now), new SettableClock(now)),
            store,
            new DnsClaimSweepOptions
            {
                Interval = TimeSpan.FromMilliseconds(200),
                LeaseRetryInterval = TimeSpan.FromMilliseconds(200),
                SchemaWait = TimeSpan.FromMilliseconds(200),
            },
            new SettableClock(now),
            NullLogger<DnsClaimSweeper>.Instance);
    }

    private static async Task<bool> UntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        return false;
    }

    private static DnsClaimVerifier Verifier(NpgsqlDataSource source, IDnsTxtResolver dns) => new(
        new NpgsqlClaimStore(source),
        new NpgsqlEndpointStore(source),
        dns,
        Claims(source),
        new SettableClock(Then));

    private static ClaimService Claims(NpgsqlDataSource source, DateTimeOffset? at = null) => new(
        new NpgsqlClaimStore(source),
        new NpgsqlGameStore(source),
        new SettableClock(at ?? Then));

    private static async Task<Guid> GameAsync(NpgsqlDataSource source, string slug)
    {
        var id = Guid.CreateVersion7();

        await new NpgsqlGameStore(source).InsertAsync(new GameRecord(
            id,
            slug,
            slug,
            Tagline: null,
            LifecycleState.Active,
            IsClaimed: false,
            FirstSeenAt: Then.AddYears(-1),
            LastReachableAt: null,
            ArchivedAt: null));

        return id;
    }

    private static Task EndpointAsync(
        NpgsqlDataSource source,
        Guid game,
        string host,
        int port,
        EndpointState state = EndpointState.Active) =>
        new NpgsqlEndpointStore(source).UpsertAsync(
            new GameEndpoint(game, host, port, EndpointKind.Telnet, Then, Then, state));

    private static async Task<Guid> AccountAsync(NpgsqlDataSource source)
    {
        var id = Guid.CreateVersion7();

        await using var connection = await source.OpenConnectionAsync();

        await connection.ExecuteAsync(
            """
            INSERT INTO app_user (id, display_name, normalised_name, security_stamp,
                                  concurrency_stamp, created_at)
            VALUES (@id, @name, @normalised, @stamp, @stamp, now())
            """,
            new
            {
                id,
                name = $"operator-{id:N}",
                normalised = $"OPERATOR-{id:N}".ToUpperInvariant(),
                stamp = Guid.NewGuid().ToString(),
            });

        return id;
    }

    private static async Task<bool> IsClaimedAsync(NpgsqlDataSource source, Guid game)
    {
        await using var connection = await source.OpenConnectionAsync();

        return await connection.ExecuteScalarAsync<bool>(
            "SELECT is_claimed FROM game WHERE id = @game", new { game });
    }
}
