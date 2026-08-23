using MUI.Ares;
using MUI.Catalog;
using MUI.Crawler.Tests.Support;
using MUI.Discovery;

namespace MUI.Crawler.Tests;

/// <summary>
/// What one AresCentral pass does, with the hub and every store replaced.
/// </summary>
/// <remarks>
/// The hub is faked at the client rather than at the socket: what is under test is what the pass is
/// allowed to write and in what order, not how a JSON body is parsed, which has its own tests.
/// </remarks>
public class AresCycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    private static AresListedGame Game(
        string name,
        string? host,
        int port,
        string? website = "https://example.org",
        string? status = "Open") =>
        new(name, "A **Markdown** blurb.", host, port, "Sci-Fi", website, "08/21/2026", status);

    /// <summary>
    /// An address nobody here has seen becomes a target and no more. No game exists yet, so there is
    /// nothing for the hub's values to attach to — §7.1's rule, which this pass does not get to bend.
    /// </summary>
    [Test]
    public async Task AnUnknownAddressBecomesATargetAndWritesNoFields()
    {
        var targets = new FakeTargets(Now);
        var fields = new FakeFields();

        var result = await Cycle(
                new StubHub([Game("Pacifica", "bsgpacifica.org", 4201)]), targets, fields)
            .RunAsync();

        await Assert.That(result.Seeded).IsEqualTo(1);

        var planted = targets.Added.Single();
        await Assert.That(planted.Host).IsEqualTo("bsgpacifica.org");
        await Assert.That(planted.Port).IsEqualTo(4201);

        // The security-relevant half: these are stranger-supplied addresses and HostScopeGuard has to
        // rule on every one of them, exactly as it does on a REFERRAL.
        await Assert.That(planted.IsOperatorSeed).IsFalse();
        await Assert.That(planted.DiscoveredVia).IsEqualTo(DiscoverySource.AresCentral);

        await Assert.That(fields.Written).IsEmpty();
    }

    /// <summary>
    /// An address already in the registry that has not answered for itself yet is still not a game.
    /// Nothing may be written against it, and the pass must not plant a second target for it.
    /// </summary>
    [Test]
    public async Task AKnownButUnpromotedAddressIsNeitherReseededNorDescribed()
    {
        var targets = new FakeTargets(Now);
        targets.Existing[("bsgpacifica.org", 4201)] = null;
        var fields = new FakeFields();

        var result = await Cycle(
                new StubHub([Game("Pacifica", "bsgpacifica.org", 4201)]), targets, fields)
            .RunAsync();

        await Assert.That(result.Seeded).IsEqualTo(0);
        await Assert.That(result.Bound).IsEqualTo(0);
        await Assert.That(targets.Added).IsEmpty();
        await Assert.That(fields.Written).IsEmpty();
    }

    /// <summary>
    /// Once the ordinary crawl has promoted the address, the hub's values land — every one of them
    /// under a rung that says a hub said so, never under one that claims we measured it.
    /// </summary>
    [Test]
    public async Task APromotedAddressTakesTheHubsValuesUnderItsOwnRung()
    {
        var game = Guid.CreateVersion7();
        var targets = new FakeTargets(Now);
        targets.Existing[("bsgpacifica.org", 4201)] = game;
        var fields = new FakeFields();

        var result = await Cycle(
                new StubHub([Game("Pacifica", "bsgpacifica.org", 4201)]), targets, fields)
            .RunAsync();

        await Assert.That(result.Bound).IsEqualTo(1);
        await Assert.That(fields.Written.All(f => f.Source == FieldSource.AresCentral)).IsTrue();
        await Assert.That(fields.Written.All(f => f.GameId == game)).IsTrue();
        await Assert.That(Value(fields, "NAME")).IsEqualTo("Pacifica");
        await Assert.That(Value(fields, "GENRE")).IsEqualTo("Sci-Fi");
        await Assert.That(Value(fields, "WEBSITE")).IsEqualTo("https://example.org");
        await Assert.That(Value(fields, "STATUS")).IsEqualTo("Open");
        await Assert.That(Value(fields, "DESCRIPTION")).IsEqualTo("A **Markdown** blurb.");
    }

    /// <summary>
    /// Everything on this list runs AresMUSH — that is what the list is. Inferred from the hub's own
    /// definition rather than read from a field, and recorded at the same weak rung as the rest,
    /// because it is still somebody else's say-so about somebody else's server.
    /// </summary>
    [Test]
    public async Task BeingOnTheListIsItselfTheCodebase()
    {
        var targets = new FakeTargets(Now);
        targets.Existing[("bsgpacifica.org", 4201)] = Guid.CreateVersion7();
        var fields = new FakeFields();

        await Cycle(new StubHub([Game("Pacifica", "bsgpacifica.org", 4201)]), targets, fields)
            .RunAsync();

        await Assert.That(Value(fields, "CODEBASE")).IsEqualTo("AresMUSH");
    }

    /// <summary>Parsers never fabricate: a field the hub left blank is a field we do not write.</summary>
    [Test]
    public async Task ABlankValueWritesNoFieldAtAll()
    {
        var targets = new FakeTargets(Now);
        targets.Existing[("bsgpacifica.org", 4201)] = Guid.CreateVersion7();
        var fields = new FakeFields();

        await Cycle(
                new StubHub([Game("Pacifica", "bsgpacifica.org", 4201, website: "   ", status: null)]),
                targets, fields)
            .RunAsync();

        await Assert.That(fields.Written.Any(f => f.Field == "WEBSITE")).IsFalse();
        await Assert.That(fields.Written.Any(f => f.Field == "STATUS")).IsFalse();
        await Assert.That(fields.Written.Any(f => f.Field == "NAME")).IsTrue();
    }

    /// <summary>
    /// A listing with nothing to dial is recorded and not seeded: planting a target that can never
    /// answer would put a permanent failure in the registry on somebody else's behalf.
    /// </summary>
    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task AListingWithNoPortIsRecordedAndNotSeeded(int port)
    {
        var targets = new FakeTargets(Now);
        var listings = new FakeListings();

        var result = await Cycle(
                new StubHub([Game("Unlaunched", "example.org", port)]),
                targets, new FakeFields(), listings)
            .RunAsync();

        await Assert.That(result.Unlistable).IsEqualTo(1);
        await Assert.That(result.Seeded).IsEqualTo(0);
        await Assert.That(targets.Added).IsEmpty();
        await Assert.That(listings.Rows.Count).IsEqualTo(1);
    }

    /// <summary>
    /// A listing with no hostname is not a listing we can key on, so it is counted and dropped —
    /// there is no address to remember it under.
    /// </summary>
    [Test]
    public async Task AListingWithNoHostnameIsCountedAndNotRecorded()
    {
        var targets = new FakeTargets(Now);
        var listings = new FakeListings();

        var result = await Cycle(
                new StubHub([Game("Nameless", "   ", 4201)]), targets, new FakeFields(), listings)
            .RunAsync();

        await Assert.That(result.Unlistable).IsEqualTo(1);
        await Assert.That(targets.Added).IsEmpty();
        await Assert.That(listings.Rows).IsEmpty();
    }

    /// <summary>
    /// The one that matters most. A fetch that fails writes nothing and, above all, does not sweep —
    /// otherwise one bad response dates every listing we hold as delisted at once.
    /// </summary>
    [Test]
    public async Task AFailedFetchWritesNothingAndDoesNotSweep()
    {
        var targets = new FakeTargets(Now);
        var listings = new FakeListings();
        var fields = new FakeFields();

        await Assert.That(async () =>
                await Cycle(new ThrowingHub(), targets, fields, listings).RunAsync())
            .Throws<HttpRequestException>();

        await Assert.That(targets.Added).IsEmpty();
        await Assert.That(fields.Written).IsEmpty();
        await Assert.That(listings.Rows).IsEmpty();
        await Assert.That(listings.SweptAt).IsNull();
    }

    /// <summary>
    /// A second pass over the same list seeds nothing: the addresses are in the registry now.
    /// </summary>
    /// <remarks>
    /// Verified against a real database too, but it belongs here as well — this is the property that
    /// keeps an hourly pass from adding the same eighteen addresses every hour for ever, and a fake
    /// that forgot what it was handed would let that regress unnoticed.
    /// </remarks>
    [Test]
    public async Task ASecondPassOverTheSameListSeedsNothing()
    {
        var targets = new FakeTargets(Now);
        var hub = new StubHub([Game("Pacifica", "bsgpacifica.org", 4201)]);

        var first = await Cycle(hub, targets, new FakeFields()).RunAsync();
        var second = await Cycle(hub, targets, new FakeFields()).RunAsync();

        await Assert.That(first.Seeded).IsEqualTo(1);
        await Assert.That(second.Seeded).IsEqualTo(0);
        await Assert.That(targets.Added.Count).IsEqualTo(1);
    }

    /// <summary>An empty list is a real answer and does sweep — the hub said it lists nothing.</summary>
    [Test]
    public async Task AnEmptyListIsAnAnswerAndSweeps()
    {
        var listings = new FakeListings();

        var result = await Cycle(new StubHub([]), new FakeTargets(Now), new FakeFields(), listings)
            .RunAsync();

        await Assert.That(result.Listed).IsEqualTo(0);
        await Assert.That(listings.SweptAt).IsEqualTo(Now);
    }

    /// <summary>
    /// The hostname is normalised before anything is keyed on it, so one address never becomes two
    /// rows.
    /// </summary>
    /// <remarks>
    /// Not hypothetical: the hub lists <c>PokemorphParadise.aresmush.com</c> in mixed case today.
    /// Keying on the raw spelling would mean that the day it is written in lower case we insert a
    /// second listing and date the first as delisted — reporting a game as having left on a pass
    /// where nothing happened at all. <c>crawl_target</c> normalises for the same reason.
    /// </remarks>
    [Test]
    [Arguments(" bsgpacifica.org ")]
    [Arguments("BSGPacifica.org")]
    [Arguments("bsgpacifica.org.")]
    public async Task AHostnameIsNormalisedBeforeItIsUsedAsAKey(string spelling)
    {
        var targets = new FakeTargets(Now);
        var listings = new FakeListings();

        await Cycle(
                new StubHub([Game("Pacifica", spelling, 4201)]), targets, new FakeFields(), listings)
            .RunAsync();

        await Assert.That(targets.Added.Single().Host).IsEqualTo("bsgpacifica.org");
        await Assert.That(listings.Rows.Single().Key).IsEqualTo(("bsgpacifica.org", 4201));
    }

    private static string? Value(FakeFields fields, string name) =>
        fields.Written.SingleOrDefault(f => f.Field == name)?.Value;

    private static AresCycle Cycle(
        IAresGames hub,
        FakeTargets targets,
        FakeFields fields,
        FakeListings? listings = null) =>
        new(hub, targets, listings ?? new FakeListings(), fields, new SettableClock(Now));

    private sealed class StubHub(IReadOnlyList<AresListedGame> games) : IAresGames
    {
        public Task<IReadOnlyList<AresListedGame>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult(games);
    }

    private sealed class ThrowingHub : IAresGames
    {
        public Task<IReadOnlyList<AresListedGame>> ListAsync(CancellationToken ct = default) =>
            throw new HttpRequestException("401");
    }

    private sealed class FakeListings : IAresListingRepository
    {
        public Dictionary<(string, int), AresListing> Rows { get; } = [];

        public DateTimeOffset? SweptAt { get; private set; }

        public Task UpsertAsync(AresListing listing, CancellationToken ct)
        {
            Rows[(listing.Hostname, listing.Port)] = listing;
            return Task.CompletedTask;
        }

        public Task BindAsync(string hostname, int port, Guid gameId, CancellationToken ct)
        {
            if (Rows.TryGetValue((hostname, port), out var row))
            {
                Rows[(hostname, port)] = row with { GameId = gameId };
            }

            return Task.CompletedTask;
        }

        public Task<int> DelistMissingAsync(DateTimeOffset asOf, CancellationToken ct)
        {
            SweptAt = asOf;
            return Task.FromResult(0);
        }

        public Task<IReadOnlyList<AresListing>> AllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AresListing>>([.. Rows.Values]);
    }
}
