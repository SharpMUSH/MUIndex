using MUI.Catalog;
using MUI.Crawl;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// An address a public announcement named, handed in by staff's lead routine.
/// </summary>
/// <remarks>
/// The routine is a model reading forum posts, so the interesting assertions are the ones that keep
/// its mistakes off the site: a lead is held to the submission form's standard rather than an
/// operator seed's, carries nothing but an address, and cannot be used to touch an address somebody
/// asked us to leave alone. The checks it shares with the form are <see cref="AddressIntake"/>, and
/// <see cref="SubmissionServiceTests"/> covers them in depth; these pin that the lead door uses them.
/// </remarks>
public class LeadServiceTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static readonly LeadEvidence Post = new(
        new Uri("https://www.reddit.com/r/MUD/comments/abc123/tidewater_nights_is_open/"),
        PostUrl: null,
        Channel: "reddit");

    private sealed record World(
        LeadService Service,
        InMemoryCrawlTargetRepository Targets,
        InMemoryEndpointDirectory Endpoints,
        InMemoryLeadLog Log,
        FakeHostResolver Dns,
        InMemoryCrawlOptOutRepository OptOuts,
        ManualTimeProvider Clock);

    private static World Build(LeadOptions? options = null)
    {
        var targets = new InMemoryCrawlTargetRepository();
        var endpoints = new InMemoryEndpointDirectory();
        var log = new InMemoryLeadLog();
        var dns = new FakeHostResolver()
            .Resolving("mud.example.org", "203.0.113.10")
            .Resolving("a.example.org", "203.0.113.11")
            .Resolving("b.example.org", "203.0.113.12");
        var clock = new ManualTimeProvider();
        var optOuts = new InMemoryCrawlOptOutRepository();

        return new World(
            new LeadService(
                targets,
                endpoints,
                new HostScopeGuard(dns),
                new OptOutGate(optOuts, new FakeDnsTxtResolver(), clock),
                log,
                options ?? new LeadOptions(),
                clock),
            targets,
            endpoints,
            log,
            dns,
            optOuts,
            clock);
    }

    /// <summary>
    /// The target is marked as proposed, not chosen, so it must prove it is a game (§7.2, §7.8).
    /// </summary>
    [Test]
    public async Task ALeadBecomesASubmittedTargetAndNotAnOperatorSeed()
    {
        var world = Build();

        var receipt = await world.Service.SubmitAsync("mud.example.org", 4201, Post, ct: None);

        await Assert.That(receipt.Outcome).IsEqualTo(SubmissionOutcome.Accepted);

        var target = await world.Targets.ByAddressAsync("mud.example.org", 4201, None);

        await Assert.That(target).IsNotNull();

        // What keeps a misread address off every public surface until a probe vouches for it.
        await Assert.That(target!.SubmittedAt).IsEqualTo(world.Clock.GetUtcNow());

        // Named for what it is, and never for the site it was read on.
        await Assert.That(target.DiscoveredVia).IsEqualTo(DiscoverySource.Announcement);

        // §7.2's exemption is never inferred, least of all from a model's reading of a forum post.
        await Assert.That(target.IsOperatorSeed).IsFalse();
        await Assert.That(target.NextProbeAt).IsEqualTo(world.Clock.GetUtcNow());
    }

    /// <summary>The page the address came from is kept in our own log, beside what became of it.</summary>
    [Test]
    public async Task TheEvidenceIsLoggedWithTheOutcomeAndTheTarget()
    {
        var world = Build();
        var onTheirSite = new LeadEvidence(
            new Uri("https://tidewater.example.org/connect"),
            new Uri("https://www.reddit.com/r/MUD/comments/abc123/tidewater_nights_is_open/"),
            "reddit");

        await world.Service.SubmitAsync("mud.example.org", 4201, onTheirSite, ct: None);

        var row = world.Log.Rows.Single();
        var target = await world.Targets.ByAddressAsync("mud.example.org", 4201, None);

        await Assert.That(row.Outcome).IsEqualTo(SubmissionOutcome.Accepted);
        await Assert.That(row.CrawlTargetId).IsEqualTo(target!.Id);
        await Assert.That(row.Host).IsEqualTo("mud.example.org");
        await Assert.That(row.Evidence).IsEqualTo(onTheirSite);
    }

    /// <summary>
    /// A game we already list collapses onto that game: a lead can never re-surface or re-mark one,
    /// unlisted and excluded games included.
    /// </summary>
    [Test]
    public async Task ALeadForAGameWeAlreadyHaveWritesNoTarget()
    {
        var world = Build();
        var game = Guid.CreateVersion7();

        await world.Endpoints.UpsertAsync(
            new KnownEndpoint(game, "mud.example.org", 4201, world.Clock.GetUtcNow(), world.Clock.GetUtcNow()),
            None);

        var receipt = await world.Service.SubmitAsync("mud.example.org", 4201, Post, ct: None);

        await Assert.That(receipt.Outcome).IsEqualTo(SubmissionOutcome.AlreadyListed);
        await Assert.That(receipt.GameId).IsEqualTo(game);
        await Assert.That(world.Targets.All).IsEmpty();
        await Assert.That(world.Dns.Asked).IsEmpty();
    }

    /// <summary>
    /// A host that asked us to stop is refused at the door, however many announcements name it (§11).
    /// </summary>
    [Test]
    public async Task ALeadForAnOptedOutAddressIsRefused()
    {
        var world = Build();
        var now = world.Clock.GetUtcNow();

        await world.OptOuts.RecordAsync(
            new CrawlOptOut
            {
                Host = "mud.example.org",
                Port = null,
                Source = OptOutSource.Request,
                RecordedAt = now,
                LastConfirmedAt = now,
                Detail = "asked by mail",
            },
            None);

        var receipt = await world.Service.SubmitAsync("mud.example.org", 4201, Post, ct: None);

        await Assert.That(receipt.Outcome).IsEqualTo(SubmissionOutcome.RefusedOptOut);
        await Assert.That(world.Targets.All).IsEmpty();
        await Assert.That(world.Log.Rows.Single().Outcome).IsEqualTo(SubmissionOutcome.RefusedOptOut);
    }

    /// <summary>§7.2's gate runs on the resolved address, for a lead as for anything else.</summary>
    [Test]
    public async Task ALeadResolvingIntoOurOwnNetworkIsRefused()
    {
        var world = Build();
        world.Dns.Resolving("internal.example.org", "169.254.169.254");

        var receipt = await world.Service.SubmitAsync("internal.example.org", 4201, Post, ct: None);

        await Assert.That(receipt.Outcome).IsEqualTo(SubmissionOutcome.RefusedNotRoutable);
        await Assert.That(world.Targets.All).IsEmpty();
    }

    /// <summary>
    /// A dry run says what would happen and leaves no trace — not a target, not a log row, not a slot.
    /// </summary>
    [Test]
    public async Task ADryRunWritesNothingAndTakesNoSlot()
    {
        var world = Build(new LeadOptions { PerWindow = 1 });

        var dry = await world.Service.SubmitAsync("mud.example.org", 4201, Post, dryRun: true, ct: None);

        await Assert.That(dry.Outcome).IsEqualTo(SubmissionOutcome.Accepted);
        await Assert.That(world.Targets.All).IsEmpty();
        await Assert.That(world.Log.Rows).IsEmpty();

        // The one real slot is still there to take.
        var real = await world.Service.SubmitAsync("mud.example.org", 4201, Post, ct: None);

        await Assert.That(real.Outcome).IsEqualTo(SubmissionOutcome.Accepted);
    }

    /// <summary>
    /// The bound is a ceiling on a routine that misreads a page, and spending it costs no lookup.
    /// </summary>
    [Test]
    public async Task TheWindowTakesOnlySoManyLeadsAndThenReopens()
    {
        var world = Build(new LeadOptions { PerWindow = 2, Window = TimeSpan.FromDays(1) });

        await world.Service.SubmitAsync("a.example.org", 4201, Post, ct: None);
        await world.Service.SubmitAsync("b.example.org", 4201, Post, ct: None);

        var third = await world.Service.SubmitAsync("mud.example.org", 4201, Post, ct: None);

        await Assert.That(third.Outcome).IsEqualTo(SubmissionOutcome.TooMany);
        await Assert.That(world.Log.Rows.Count).IsEqualTo(2);
        await Assert.That(world.Dns.Asked).DoesNotContain("mud.example.org");

        world.Clock.Advance(TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1));

        var tomorrow = await world.Service.SubmitAsync("mud.example.org", 4201, Post, ct: None);

        await Assert.That(tomorrow.Outcome).IsEqualTo(SubmissionOutcome.Accepted);
    }

    /// <summary>An address that cannot be read is recorded as that, and fabricates no default port.</summary>
    [Test]
    [Arguments("", 4201)]
    [Arguments("mud.example.org", 0)]
    [Arguments("mud.example.org", 70000)]
    public async Task AnUnreadableAddressIsMalformed(string host, int port)
    {
        var world = Build();

        var receipt = await world.Service.SubmitAsync(host, port, Post, ct: None);

        await Assert.That(receipt.Outcome).IsEqualTo(SubmissionOutcome.Malformed);
        await Assert.That(world.Targets.All).IsEmpty();
        await Assert.That(world.Log.Rows.Single().Outcome).IsEqualTo(SubmissionOutcome.Malformed);
    }

    /// <summary>
    /// A lead with no web page behind it is the routine's mistake, refused before it takes a slot.
    /// </summary>
    [Test]
    [Arguments("ftp://example.org/post", "reddit")]
    [Arguments("https://www.reddit.com/r/MUD/", "Reddit")]
    [Arguments("https://www.reddit.com/r/MUD/", "")]
    [Arguments("https://www.reddit.com/r/MUD/", "a-label-that-is-far-too-long-to-be-a-label")]
    public async Task EvidenceThatIsNotAWebPageIsRefusedBeforeAnythingHappens(string url, string channel)
    {
        var world = Build();
        var evidence = new LeadEvidence(new Uri(url), null, channel);

        await Assert.That(async () => await world.Service.SubmitAsync("mud.example.org", 4201, evidence, ct: None))
            .Throws<ArgumentException>();

        await Assert.That(world.Log.Rows).IsEmpty();
        await Assert.That(world.Targets.All).IsEmpty();
    }
}
