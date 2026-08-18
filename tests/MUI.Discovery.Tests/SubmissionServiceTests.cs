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
        // Every name a test expects to be taken has an answer, so "accepted" is never an unscripted lookup.
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
    /// The gate can't be a string check — see <see cref="HostScopeGuard"/> — since a name passes
    /// every check that can be made before DNS answers.
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
    /// No probe result, availability sample or game id anywhere near this —
    /// <see cref="SubmissionRecord"/> has no field one could be put in.
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

        // Reflection, not just this row: a log that *could* name a game is one somebody will
        // eventually attach one to.
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

        // No lookup was made: the form is not a resolver anybody may drive.
        await Assert.That(world.Dns.Asked).IsEmpty();
    }

    /// <summary>
    /// Submitting an address already in the registry changes nothing about it.
    /// </summary>
    /// <remarks>
    /// <b>Including the submission marker.</b> If a second submission could set it, the form would
    /// be a way to hide any listed game by typing its address.
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
    /// The crawl loop's gate already holds this, but the form would otherwise tell somebody
    /// "accepted" for an address we'd already promised never to touch.
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

        // The cheap register answered first, so no TXT lookup was spent.
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
    /// §11 scopes a DNS opt-out to a port when the record names one — MU* hosting routinely runs
    /// unrelated games on one domain, separated only by a port.
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

    /// <summary>Nothing is spent on an address §7.2 has already refused.</summary>
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
    /// An unauthenticated form that triggers a DNS query is a request amplifier, so the reservation
    /// is taken before any of the work.
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
    /// §11's own shape: "no such record" is an answer, "the resolver did not reply" is not. A
    /// nameserver having a bad minute must not become a refusal a submitter cannot understand.
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
        // "intranet 80" is the one worth naming: accepting a single-label host would aim the
        // crawler at whatever our own search domain resolves that to.
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

        // Nothing was parsed, resolved or written — a source at its bound must not make us do work.
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
        // window`, so a row of exactly that age is still inside it.
        world.Clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));

        await Assert.That((await world.Service.SubmitAsync("mud.example.org", "4201", Source, None)).Outcome)
            .IsEqualTo(SubmissionOutcome.Accepted);
    }

    /// <summary>A refusal never lands in the log as an outcome the table would take.</summary>
    [Test]
    public async Task ASourceAtItsBoundIsNotLogged()
    {
        // Otherwise the window would slide forward for as long as somebody keeps knocking.
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

        // A retired epoch's rows can't be lined up against a current one's — the salt is what makes
        // "we do not store the address" true rather than nearly true.
        await Assert.That(one).IsNotEqualTo(await next.OfAsync(IPAddress.Parse("203.0.113.10")));

        // A request with no address at all still falls into one bucket, so it is still bounded.
        await Assert.That(await epoch.OfAsync(null)).IsEqualTo(
            SubmissionSource.Digest(Enumerable.Repeat((byte)1, 32).ToArray(), SubmissionSource.Unknown));
    }

    /// <summary>
    /// Every replica derives the same digest for one address, which is what makes the bound a bound.
    /// </summary>
    /// <remarks>
    /// A random per-process salt reads as stronger privacy but silently removes the limit: two
    /// replicas would hash one address two ways.
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
    /// The smallest block anybody is assigned is a /64. Keyed on the full address, one attacker
    /// holds eighteen quintillion buckets and the limit is decorative.
    /// </remarks>
    [Test]
    public async Task EveryAddressInOneIPv6PrefixIsOneSource()
    {
        await Assert.That(SubmissionSource.Bucket(IPAddress.Parse("2001:db8:1:2::1")))
            .IsEqualTo(SubmissionSource.Bucket(IPAddress.Parse("2001:db8:1:2:ffff:ffff:ffff:ffff")));

        // A neighbouring /64 is somebody else, and had to be obtained.
        await Assert.That(SubmissionSource.Bucket(IPAddress.Parse("2001:db8:1:2::1")))
            .IsNotEqualTo(SubmissionSource.Bucket(IPAddress.Parse("2001:db8:1:3::1")));

        // IPv4 is kept whole, including the mapped form, which would otherwise be a free second
        // bucket for every IPv4 client.
        await Assert.That(SubmissionSource.Bucket(IPAddress.Parse("203.0.113.10"))).IsEqualTo("203.0.113.10");
        await Assert.That(SubmissionSource.Bucket(IPAddress.Parse("::ffff:203.0.113.10")))
            .IsEqualTo("203.0.113.10");
    }

    /// <summary>A concurrent burst does not walk through the bound.</summary>
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
        // TooMany is the one never written, asserted above. This is the other half: nothing else may
        // be added to the enum without a migration.
        var recordable = Enum.GetValues<SubmissionOutcome>()
            .Where(o => o is not SubmissionOutcome.TooMany)
            .ToList();

        await Assert.That(recordable.Count).IsEqualTo(7);
    }
}
