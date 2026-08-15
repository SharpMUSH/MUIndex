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

        return new World(
            new SubmissionService(
                targets, endpoints, new HostScopeGuard(dns), log, options ?? new SubmissionOptions(), clock),
            targets,
            endpoints,
            log,
            dns,
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

        var recorded = world.Log.Records.Single();

        await Assert.That(recorded.Outcome).IsEqualTo(SubmissionOutcome.RefusedNotRoutable);
        await Assert.That(recorded.CrawlTargetId).IsNull();

        // Held by reflection, because the argument is about the shape rather than about this row: a
        // submission that could name a game is a submission somebody will eventually attach to one.
        var fields = typeof(SubmissionRecord).GetProperties().Select(p => p.Name).ToList();

        await Assert.That(fields).DoesNotContain("GameId");
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
        await Assert.That(world.Log.Records.Single().Host).IsEqualTo("mud.example.org");
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
        await Assert.That(world.Log.Records.Count).IsEqualTo(2);
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

        await Assert.That(world.Log.Records.Count).IsEqualTo(1);
    }

    /// <summary>
    /// §11 — the submitter's address is never stored, and the digest is not a hash of it alone.
    /// </summary>
    [Test]
    public async Task ASourceIsSaltedRatherThanHashed()
    {
        var first = new SubmissionSource();
        var second = new SubmissionSource();

        var one = first.Of(System.Net.IPAddress.Parse("203.0.113.10"));

        await Assert.That(one).IsEqualTo(first.Of(System.Net.IPAddress.Parse("203.0.113.10")));
        await Assert.That(one).IsNotEqualTo(first.Of(System.Net.IPAddress.Parse("203.0.113.11")));

        // A different process is a different salt, so a row written before a restart is not
        // comparable to one written after it. Four billion IPv4 guesses is an afternoon; this is
        // what makes "we do not store the address" true rather than nearly true.
        await Assert.That(one).IsNotEqualTo(second.Of(System.Net.IPAddress.Parse("203.0.113.10")));

        // A request with no address at all still falls into one bucket, so it is still bounded.
        await Assert.That(first.Of((System.Net.IPAddress?)null)).IsEqualTo(first.Of(SubmissionSource.Unknown));
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

        await Assert.That(recordable.Count).IsEqualTo(6);
    }
}
