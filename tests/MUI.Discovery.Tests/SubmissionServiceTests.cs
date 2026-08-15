using System.Net;

using MUI.Crawl;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// The public submission form's whole decision (spec §7.2, §7.6, §9).
/// </summary>
/// <remarks>
/// The form is unauthenticated and takes an address, so the interesting assertions are all about
/// what it refuses to do with one: dial into our own network, mint a second listing for a game we
/// already have, hide a game somebody names, or let one source use it as a scanner.
/// </remarks>
public class SubmissionServiceTests
{
    private const string Source = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static readonly CancellationToken None = CancellationToken.None;

    /// <summary>Everything the service touches, so a test can look at any of it afterwards.</summary>
    private sealed record World(
        SubmissionService Service,
        InMemoryCrawlTargetRepository Targets,
        InMemoryEndpointDirectory Endpoints,
        InMemorySubmissionLog Log,
        FakeHostResolver Dns,
        FakeDnsTxtResolver Txt,
        InMemoryCrawlOptOutRepository OptOuts,
        ManualTimeProvider Clock);

    private static World Build(SubmissionOptions? options = null)
    {
        var targets = new InMemoryCrawlTargetRepository();
        var endpoints = new InMemoryEndpointDirectory();
        var log = new InMemorySubmissionLog();
        // Every name a test submits and expects to be taken has an answer, so that "accepted" is
        // asserted for the reason the test is about rather than falling out of an unscripted lookup.
        var dns = new FakeHostResolver()
            .Resolving("mud.example.org", "203.0.113.10")
            .Resolving("a.example.org", "203.0.113.11")
            .Resolving("b.example.org", "203.0.113.12")
            .Resolving("c.example.org", "203.0.113.13");
        var clock = new ManualTimeProvider();
        var txt = new FakeDnsTxtResolver();
        var optOuts = new InMemoryCrawlOptOutRepository();

        return new World(
            new SubmissionService(
                targets,
                endpoints,
                new HostScopeGuard(dns),
                new OptOutGate(optOuts, txt, clock),
                log,
                options ?? new SubmissionOptions(),
                clock),
            targets,
            endpoints,
            log,
            dns,
            txt,
            optOuts,
            clock);
    }

    [Test]
    public async Task AnAddressThatResolvesIntoPublicSpaceBecomesACrawlTarget()
    {
        var world = Build();

        var receipt = await world.Service.SubmitAsync("mud.example.org", "4201", Source, None);

        await Assert.That(receipt.Outcome).IsEqualTo(SubmissionOutcome.Accepted);
        await Assert.That(receipt.Address!.Host).IsEqualTo("mud.example.org");
        await Assert.That(receipt.Address.Port).IsEqualTo(4201);

        var target = await world.Targets.ByAddressAsync("mud.example.org", 4201, None);

        await Assert.That(target).IsNotNull();

        // Due now: there is a person behind this address in a way there is not behind a referral.
        await Assert.That(target!.NextProbeAt).IsEqualTo(world.Clock.GetUtcNow());

        // §7.2's exemption is never inferred, and a stranger with a browser is not an operator.
        await Assert.That(target.IsOperatorSeed).IsFalse();

        // The marker that keeps it off every public surface until somebody claims it (§8).
        await Assert.That(target.SubmittedAt).IsEqualTo(world.Clock.GetUtcNow());
    }

    /// <summary>
    /// §7.2, on the resolved address, before anything is written.
    /// </summary>
    /// <remarks>
    /// This is the whole reason the gate cannot be a check on the string. Anybody may type
    /// <c>internal.example.org</c> into a public form, and publishing an A record pointing it at
    /// <c>169.254.169.254</c> — the cloud metadata address, which hands out credentials — costs an
    /// attacker nothing. The name passes every check that can be made before DNS answers.
    /// </remarks>
    [Test]
    [Arguments("127.0.0.1")]
    [Arguments("10.0.0.5")]
    [Arguments("192.168.1.1")]
    [Arguments("169.254.169.254")]
    [Arguments("::1")]
    [Arguments("fd00::1")]
    public async Task ANameResolvingIntoOurOwnNetworkIsRefusedAndNothingIsWritten(string address)
    {
        var world = Build();
        world.Dns.Resolving("internal.example.org", address);

        var receipt = await world.Service.SubmitAsync("internal.example.org", "4201", Source, None);

        await Assert.That(receipt.Outcome).IsEqualTo(SubmissionOutcome.RefusedNotRoutable);

        // Nothing may be dialled later either: a refused name never reaches the registry.
        await Assert.That(await world.Targets.ByAddressAsync("internal.example.org", 4201, None)).IsNull();
        await Assert.That(world.Targets.All).IsEmpty();
    }

    /// <summary>
    /// A mixed answer refuses the whole target, and does not proceed on the address we liked.
    /// </summary>
    [Test]
    public async Task OnePrivateAddressRefusesTheWholeName()
    {
        var world = Build();
        world.Dns.Resolving("both.example.org", "203.0.113.10", "10.0.0.5");

        var receipt = await world.Service.SubmitAsync("both.example.org", "4201", Source, None);

        await Assert.That(receipt.Outcome).IsEqualTo(SubmissionOutcome.RefusedNotRoutable);
        await Assert.That(world.Targets.All).IsEmpty();
    }

    /// <summary>
    /// A refusal is recorded as a submission and never as anything about a game.
    /// </summary>
    /// <remarks>
    /// The distinction CLAUDE.md is emphatic about, asserted where it can be: there is no probe
    /// result, no availability sample and no game id anywhere near this — <see cref="SubmissionRecord"/>
    /// has no field one could be put in. What a refusal produces is a row saying we declined, which
    /// is where a decision of ours belongs.
    /// </remarks>
    [Test]
    public async Task ARefusalIsRecordedAgainstTheSubmissionAndNotAgainstAGame()
    {
        var world = Build();
        world.Dns.Resolving("internal.example.org", "10.0.0.5");

        await world.Service.SubmitAsync("internal.example.org", "4201", Source, None);

        var recorded = world.Log.Rows.Single();

        await Assert.That(recorded.Outcome).IsEqualTo(SubmissionOutcome.RefusedNotRoutable);
        await Assert.That(recorded.CrawlTargetId).IsNull();

        // Held by reflection, because the argument is about the shape rather than about this row: a
        // log that *could* name a game is a log somebody will eventually attach one to. Neither
        // method on the interface takes a game id, and the table has no column for one.
        var parameters = typeof(ISubmissionLog).GetMethods()
            .SelectMany(m => m.GetParameters())
            .Select(p => p.Name!)
            .ToList();

        await Assert.That(parameters).DoesNotContain("gameId");
    }

    /// <summary>
    /// "Could not resolve" and "resolved somewhere we won't go" are different facts (§7.2).
    /// </summary>
    [Test]
    public async Task ANameThatDoesNotResolveIsNotARefusal()
    {
        var world = Build();
        world.Dns.Failing("gone.example.org");

        var receipt = await world.Service.SubmitAsync("gone.example.org", "4201", Source, None);

        await Assert.That(receipt.Outcome).IsEqualTo(SubmissionOutcome.Unresolvable);
        await Assert.That(world.Targets.All).IsEmpty();
    }

    /// <summary>
    /// A game already answering there collapses onto that game rather than making a second one.
    /// </summary>
    [Test]
    public async Task AnAddressWeAlreadyHaveCollapsesOntoTheGame()
    {
        var world = Build();
        var game = Guid.CreateVersion7();

        await world.Endpoints.UpsertAsync(
            new KnownEndpoint(game, "mud.example.org", 4201, world.Clock.GetUtcNow(), world.Clock.GetUtcNow()),
            None);

        var receipt = await world.Service.SubmitAsync("mud.example.org", "4201", Source, None);

        await Assert.That(receipt.Outcome).IsEqualTo(SubmissionOutcome.AlreadyListed);
        await Assert.That(receipt.GameId).IsEqualTo(game);
        await Assert.That(world.Targets.All).IsEmpty();

        // And no lookup was made, which is the point of doing this before DNS: the form is not a
        // resolver anybody may drive.
        await Assert.That(world.Dns.Asked).IsEmpty();
    }

    /// <summary>
    /// Submitting an address already in the registry changes nothing about it.
    /// </summary>
    /// <remarks>
    /// <b>Including the submission marker.</b> If a second submission could set it, the form would
    /// be a way to hide any listed game on the site by typing its address — the one thing a
    /// hidden-until-claimed rule makes possible if it is written the wrong way round.
    /// </remarks>
    [Test]
    public async Task SubmittingATargetWeAlreadyCrawlChangesNothingAboutIt()
    {
        var world = Build();

        var found = new CrawlTarget
        {
            Id = Guid.CreateVersion7(),
            Host = "mud.example.org",
            Port = 4201,
            NextProbeAt = world.Clock.GetUtcNow().AddDays(3),
            FirstSeenAt = world.Clock.GetUtcNow().AddYears(-1),
        };

        await world.Targets.AddAsync(found, None);

        var receipt = await world.Service.SubmitAsync("mud.example.org", "4201", Source, None);

        await Assert.That(receipt.Outcome).IsEqualTo(SubmissionOutcome.AlreadyQueued);

        var after = await world.Targets.ByAddressAsync("mud.example.org", 4201, None);

        await Assert.That(after!.SubmittedAt).IsNull();
        await Assert.That(after.NextProbeAt).IsEqualTo(found.NextProbeAt);
        await Assert.That(after.Id).IsEqualTo(found.Id);
    }

    /// <summary>
    /// An address whose operator has asked us to stop is refused at the door (spec §11).
    /// </summary>
    /// <remarks>
    /// <b>The crawl loop's own gate already held, and that is what made this worth fixing rather
    /// than urgent.</b> A target with a standing opt-out is never dialled, so nothing leaked and no
    /// game was ever minted — the form simply told somebody "accepted" for an address we had already
    /// promised never to touch, and then did nothing about it for ever. A refusal we know at the
    /// door belongs at the door.
    /// </remarks>
    [Test]
    [Arguments(OptOutSource.Request)]
    [Arguments(OptOutSource.Mssp)]
    public async Task AnAddressWithARecordedOptOutIsRefusedAndWritesNothing(OptOutSource source)
    {
        var world = Build();
        var now = world.Clock.GetUtcNow();

        await world.OptOuts.RecordAsync(
            new CrawlOptOut
            {
                Host = "mud.example.org",
                Port = 4201,
                Source = source,
                RecordedAt = now,
                LastConfirmedAt = now,
                Detail = "asked by mail",
            },
            None);

        var receipt = await world.Service.SubmitAsync("mud.example.org", "4201", Source, None);

        await Assert.That(receipt.Outcome).IsEqualTo(SubmissionOutcome.RefusedOptOut);
        await Assert.That(world.Targets.All).IsEmpty();

        // The cheap register answered, so no TXT lookup was spent — and neither route may be
        // re-read without doing the thing they asked us to stop doing, so no answer could act on it.
        await Assert.That(world.Txt.Asked).IsEmpty();
    }

    /// <summary>A TXT record at the host asks for us just as well, and is read here.</summary>
    [Test]
    public async Task AnAddressWhoseHostPublishesADnsOptOutIsRefused()
    {
        var world = Build();
        world.Txt.Publishing(OptOutVocabulary.DnsNameFor("mud.example.org"), OptOutVocabulary.DnsValue);

        var receipt = await world.Service.SubmitAsync("mud.example.org", "4201", Source, None);

        await Assert.That(receipt.Outcome).IsEqualTo(SubmissionOutcome.RefusedOptOut);
        await Assert.That(world.Targets.All).IsEmpty();
        await Assert.That(world.Txt.Asked).Contains("_muindex.mud.example.org");
    }

    /// <summary>
    /// A TXT opt-out naming a port speaks about that port and not about its neighbours.
    /// </summary>
    /// <remarks>
    /// §11 scopes a DNS opt-out to a port when the record names one, because MU* hosting routinely
    /// runs unrelated games on one domain separated only by a port. <c>opt-out=4201</c> and a bare
    /// <c>opt-out</c> are therefore different answers for a submission naming 4000, and a form that
    /// read them alike would either refuse an address nobody objected to or take one somebody did.
    /// </remarks>
    [Test]
    public async Task APortQualifiedOptOutOnlyAnswersForThatPort()
    {
        var world = Build();
        world.Dns.Resolving("shared.example.org", "203.0.113.20");
        world.Txt.Publishing(OptOutVocabulary.DnsNameFor("shared.example.org"), "opt-out=4201");

        await Assert.That((await world.Service.SubmitAsync("shared.example.org", "4201", Source, None)).Outcome)
            .IsEqualTo(SubmissionOutcome.RefusedOptOut);

        // The neighbour on 4000 said nothing, and is taken.
        await Assert.That((await world.Service.SubmitAsync("shared.example.org", "4000", Source, None)).Outcome)
            .IsEqualTo(SubmissionOutcome.Accepted);
    }

    /// <summary>And an unqualified record covers the host, neighbours included.</summary>
    [Test]
    public async Task AHostWideOptOutAnswersForEveryPort()
    {
        var world = Build();
        world.Dns.Resolving("shared.example.org", "203.0.113.20");
        world.Txt.Publishing(OptOutVocabulary.DnsNameFor("shared.example.org"), OptOutVocabulary.DnsValue);

        await Assert.That((await world.Service.SubmitAsync("shared.example.org", "4201", Source, None)).Outcome)
            .IsEqualTo(SubmissionOutcome.RefusedOptOut);
        await Assert.That((await world.Service.SubmitAsync("shared.example.org", "4000", Source, None)).Outcome)
            .IsEqualTo(SubmissionOutcome.RefusedOptOut);
    }

    /// <summary>
    /// Nothing is spent on an address §7.2 has already refused.
    /// </summary>
    /// <remarks>
    /// A form that resolved, refused, and then went on to ask DNS a second question about the same
    /// name would be paying twice to reach the answer it already had.
    /// </remarks>
    [Test]
    public async Task AScopeRefusalCostsNoOptOutLookup()
    {
        var world = Build();
        world.Dns.Resolving("internal.example.org", "10.0.0.5");

        var receipt = await world.Service.SubmitAsync("internal.example.org", "4201", Source, None);

        await Assert.That(receipt.Outcome).IsEqualTo(SubmissionOutcome.RefusedNotRoutable);
        await Assert.That(world.Txt.Asked).IsEmpty();
    }

    /// <summary>
    /// The rate-limit reservation is what bounds the TXT lookups this form can cause.
    /// </summary>
    /// <remarks>
    /// An unauthenticated form that triggers a DNS query is a request amplifier, and the bound has
    /// to be the one that is taken <em>before</em> any of the work — which is why the reservation is
    /// first. The resolver behind it is the crawler's own, un-retried and caching failures, so the
    /// worst case is one bounded lookup per slot.
    /// </remarks>
    [Test]
    public async Task ASourceAtItsBoundCausesNoLookupsAtAll()
    {
        var world = Build(new SubmissionOptions { PerSource = 2, Window = TimeSpan.FromHours(1) });

        await world.Service.SubmitAsync("a.example.org", "4201", Source, None);
        await world.Service.SubmitAsync("b.example.org", "4201", Source, None);

        var spentByNow = world.Txt.Asked.Count;

        for (var i = 0; i < 20; i++)
        {
            await Assert.That((await world.Service.SubmitAsync($"c{i}.example.org", "4201", Source, None)).Outcome)
                .IsEqualTo(SubmissionOutcome.TooMany);
        }

        await Assert.That(world.Txt.Asked.Count).IsEqualTo(spentByNow);
        await Assert.That(spentByNow).IsLessThanOrEqualTo(2);
    }

    /// <summary>An ordinary address is still taken, and the register is asked about it.</summary>
    [Test]
    public async Task AnAddressNobodyObjectedToIsStillAccepted()
    {
        var world = Build();

        var receipt = await world.Service.SubmitAsync("mud.example.org", "4201", Source, None);

        await Assert.That(receipt.Outcome).IsEqualTo(SubmissionOutcome.Accepted);
        await Assert.That(world.Txt.Asked).Contains("_muindex.mud.example.org");
        await Assert.That(world.Targets.All.Count).IsEqualTo(1);
    }

    /// <summary>
    /// A resolver that did not answer concludes nothing, and the address is taken.
    /// </summary>
    /// <remarks>
    /// §11's own shape: "no such record" is an answer and "the resolver did not reply" is not. A
    /// nameserver having a bad minute must not become a refusal a submitter cannot understand — and
    /// the crawl loop asks again before every dial, so nothing is lost by proceeding.
    /// </remarks>
    [Test]
    public async Task ASilentResolverDoesNotRefuseTheSubmission()
    {
        var world = Build();
        world.Txt.Silent(OptOutVocabulary.DnsNameFor("mud.example.org"));

        await Assert.That((await world.Service.SubmitAsync("mud.example.org", "4201", Source, None)).Outcome)
            .IsEqualTo(SubmissionOutcome.Accepted);
    }

    [Test]
    [Arguments("intranet", "80")]
    [Arguments("mud.example.org", "0")]
    [Arguments("mud.example.org", "70000")]
    [Arguments("mud.example.org", "four thousand")]
    [Arguments("", "4201")]
    [Arguments("mud example org", "4201")]
    [Arguments("-example.org", "4201")]
    public async Task NothingThatIsNotAnAddressBecomesOne(string host, string port)
    {
        // A single-label name is the one worth naming: accepting "intranet 80" would aim the crawler
        // at whatever our own search domain resolves that to, which is the §7.2 hole arriving
        // without anybody having to own a domain.
        var world = Build();

        var receipt = await world.Service.SubmitAsync(host, port, Source, None);

        await Assert.That(receipt.Outcome).IsEqualTo(SubmissionOutcome.Malformed);
        await Assert.That(world.Targets.All).IsEmpty();
        await Assert.That(world.Dns.Asked).IsEmpty();
    }

    /// <summary>
    /// A whole address pasted into the first box is read rather than refused on a technicality.
    /// </summary>
    [Test]
    [Arguments("mud.example.org:4201")]
    [Arguments("mud.example.org 4201")]
    [Arguments("  MUD.Example.ORG.:4201  ")]
    public async Task AWholeAddressInOneBoxIsRead(string typed)
    {
        var world = Build();

        var receipt = await world.Service.SubmitAsync(typed, null, Source, None);

        await Assert.That(receipt.Outcome).IsEqualTo(SubmissionOutcome.Accepted);
        await Assert.That(receipt.Address!.Host).IsEqualTo("mud.example.org");
        await Assert.That(receipt.Address.Port).IsEqualTo(4201);
    }

    /// <summary>One spelling per machine, or the registry holds two rows for one server.</summary>
    [Test]
    public async Task TheHostIsCanonicalisedBeforeAnythingIsDoneWithIt()
    {
        var world = Build();

        await world.Service.SubmitAsync("MUD.Example.ORG.", "4201", Source, None);

        await Assert.That(world.Targets.All.Single().Host).IsEqualTo("mud.example.org");
        await Assert.That(world.Log.Rows.Single().Host).IsEqualTo("mud.example.org");
    }

    [Test]
    public async Task OneSourceMaySubmitOnlySoMuchInsideTheWindow()
    {
        var world = Build(new SubmissionOptions { PerSource = 2, Window = TimeSpan.FromHours(1) });

        await world.Service.SubmitAsync("a.example.org", "4201", Source, None);
        await world.Service.SubmitAsync("b.example.org", "4201", Source, None);

        var third = await world.Service.SubmitAsync("mud.example.org", "4201", Source, None);

        await Assert.That(third.Outcome).IsEqualTo(SubmissionOutcome.TooMany);

        // Nothing was parsed, resolved or written — a source at its bound must not be able to make us
        // do work, which is why the check is first.
        await Assert.That(world.Log.Rows.Count).IsEqualTo(2);
        await Assert.That(world.Targets.All.Any(t => t.Host == "mud.example.org")).IsFalse();
    }

    [Test]
    public async Task TheBoundIsPerSourceAndExpiresWithTheWindow()
    {
        var world = Build(new SubmissionOptions { PerSource = 1, Window = TimeSpan.FromHours(1) });
        const string other = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";

        await world.Service.SubmitAsync("a.example.org", "4201", Source, None);

        await Assert.That((await world.Service.SubmitAsync("b.example.org", "4201", Source, None)).Outcome)
            .IsEqualTo(SubmissionOutcome.TooMany);

        // Somebody else is not throttled by our first submitter.
        await Assert.That((await world.Service.SubmitAsync("c.example.org", "4201", other, None)).Outcome)
            .IsEqualTo(SubmissionOutcome.Accepted);

        // Past the window rather than exactly onto its edge: the count is `submitted_at >= now -
        // window`, in the fake and in the index the real one reads, so a row of exactly that age is
        // still inside it.
        world.Clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));

        await Assert.That((await world.Service.SubmitAsync("mud.example.org", "4201", Source, None)).Outcome)
            .IsEqualTo(SubmissionOutcome.Accepted);
    }

    /// <summary>A refusal never lands in the log as an outcome the table would take.</summary>
    [Test]
    public async Task ASourceAtItsBoundIsNotLogged()
    {
        // Otherwise the window slides forward for as long as somebody keeps knocking, and a bound
        // that lengthens under load is not a bound.
        var world = Build(new SubmissionOptions { PerSource = 1, Window = TimeSpan.FromHours(1) });

        await world.Service.SubmitAsync("a.example.org", "4201", Source, None);
        await world.Service.SubmitAsync("b.example.org", "4201", Source, None);
        await world.Service.SubmitAsync("c.example.org", "4201", Source, None);

        await Assert.That(world.Log.Rows.Count).IsEqualTo(1);
    }

    /// <summary>
    /// §11 — the submitter's address is never stored, and the digest is not a hash of it alone.
    /// </summary>
    [Test]
    public async Task ASourceIsSaltedRatherThanHashed()
    {
        var epoch = new SubmissionSource(new FixedSubmissionSalt(1));
        var next = new SubmissionSource(new FixedSubmissionSalt(2));

        var one = await epoch.OfAsync(IPAddress.Parse("203.0.113.10"));

        await Assert.That(one).IsEqualTo(await epoch.OfAsync(IPAddress.Parse("203.0.113.10")));
        await Assert.That(one).IsNotEqualTo(await epoch.OfAsync(IPAddress.Parse("203.0.113.11")));

        // A retired epoch's rows cannot be lined up against a current one's, by anybody, including
        // us. Four billion IPv4 guesses is an afternoon, so the salt is what makes "we do not store
        // the address" true rather than nearly true, and the rotation is what bounds what one salt
        // can ever link together.
        await Assert.That(one).IsNotEqualTo(await next.OfAsync(IPAddress.Parse("203.0.113.10")));

        // A request with no address at all still falls into one bucket, so it is still bounded.
        await Assert.That(await epoch.OfAsync(null)).IsEqualTo(
            SubmissionSource.Digest(Enumerable.Repeat((byte)1, 32).ToArray(), SubmissionSource.Unknown));
    }

    /// <summary>
    /// Every replica derives the same digest for one address, which is what makes the bound a bound.
    /// </summary>
    /// <remarks>
    /// The first version generated a random salt per process. It read as the stronger privacy
    /// property and quietly removed the limit: two replicas hash one address two ways, so five per
    /// hour becomes five per replica per hour, and a restart clears it outright. The salt is shared
    /// and rotates on an epoch instead — which is what §11 actually describes.
    /// </remarks>
    [Test]
    public async Task TwoReplicasSharingASaltAgreeAboutOneAddress()
    {
        var salt = new FixedSubmissionSalt(9);
        var replicaA = new SubmissionSource(salt);
        var replicaB = new SubmissionSource(salt);

        await Assert.That(await replicaA.OfAsync(IPAddress.Parse("203.0.113.10")))
            .IsEqualTo(await replicaB.OfAsync(IPAddress.Parse("203.0.113.10")));
    }

    /// <summary>
    /// An IPv6 submitter is bounded by their /64, not by an address they have unlimited supply of.
    /// </summary>
    /// <remarks>
    /// The smallest block anybody is assigned is a /64, and home connections routinely get a /48 or
    /// a /56. Keyed on the full address, one attacker holds eighteen quintillion buckets and a limit
    /// of five per hour is decorative.
    /// </remarks>
    [Test]
    public async Task EveryAddressInOneIPv6PrefixIsOneSource()
    {
        await Assert.That(SubmissionSource.Bucket(IPAddress.Parse("2001:db8:1:2::1")))
            .IsEqualTo(SubmissionSource.Bucket(IPAddress.Parse("2001:db8:1:2:ffff:ffff:ffff:ffff")));

        // A neighbouring /64 is somebody else, and had to be obtained.
        await Assert.That(SubmissionSource.Bucket(IPAddress.Parse("2001:db8:1:2::1")))
            .IsNotEqualTo(SubmissionSource.Bucket(IPAddress.Parse("2001:db8:1:3::1")));

        // IPv4 is kept whole — there a single address is already scarce — including the mapped form,
        // which would otherwise be a free second bucket for every IPv4 client.
        await Assert.That(SubmissionSource.Bucket(IPAddress.Parse("203.0.113.10"))).IsEqualTo("203.0.113.10");
        await Assert.That(SubmissionSource.Bucket(IPAddress.Parse("::ffff:203.0.113.10")))
            .IsEqualTo("203.0.113.10");
    }

    /// <summary>
    /// A concurrent burst does not walk through the bound.
    /// </summary>
    /// <remarks>
    /// The reason the limit is a reservation rather than a count. Counting rows and then inserting
    /// one is check-then-act: every request in a burst reads a count none of them has written to,
    /// and every one of them passes. On an unauthenticated form that check is the whole limit.
    /// </remarks>
    [Test]
    public async Task ABurstFromOneSourceStillOnlyGetsItsShare()
    {
        var world = Build(new SubmissionOptions { PerSource = 3, Window = TimeSpan.FromHours(1) });

        var attempts = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ =>
            world.Service.SubmitAsync("mud.example.org", "4201", Source, None)));

        await Assert.That(attempts.Count(r => r.Outcome is not SubmissionOutcome.TooMany)).IsEqualTo(3);
        await Assert.That(world.Log.Rows.Count).IsEqualTo(3);
    }

    /// <summary>Every recordable outcome is one the storage vocabulary accepts.</summary>
    [Test]
    public async Task EveryOutcomeRecordedIsOneTheTableWillTake()
    {
        // The one that is not, TooMany, is the one never written — asserted above. This is the other
        // half: nothing else may be added to the enum without a migration, and the missing arm here
        // is what says so.
        var recordable = Enum.GetValues<SubmissionOutcome>()
            .Where(o => o is not SubmissionOutcome.TooMany)
            .ToList();

        await Assert.That(recordable.Count).IsEqualTo(7);
    }
}
