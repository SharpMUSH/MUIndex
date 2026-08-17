using System.Text.Json;

using MUI.Catalog;
using MUI.Crawler;
using MUI.Discovery;
using MUI.I3;

namespace MUI.Crawler.Tests;

/// <summary>
/// What one I3 pass does, with the gateway and both stores replaced.
/// </summary>
/// <remarks>
/// The gateway is faked at the socket in <c>MUI.I3.Tests</c>; here it is faked at the client, because
/// what is under test is the order of the three steps and what each of them is allowed to write —
/// not framing, which has its own suite.
/// </remarks>
public class I3CycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    private static I3Mud Mud(string name, string host, int port, bool who = true, string status = "up") =>
        new()
        {
            Name = name,
            Host = host,
            Port = port,
            Status = status,
            Services = who
                ? new Dictionary<string, JsonElement> { ["who"] = JsonDocument.Parse("1").RootElement.Clone() }
                : [],
        };

    /// <summary>
    /// §7.6's rule, applied to a new source: an address we do not have becomes a target and nothing
    /// else. The router also handed us this mud's name, driver, mudlib and contact address, and none
    /// of them may be written anywhere — every fact this site shows is measured by our own crawler.
    /// </summary>
    [Test]
    public async Task AnUnknownMudContributesItsAddressAndNothingElse()
    {
        var targets = new FakeTargets();
        var bindings = new FakeBindings();
        var presence = new FakePresence();

        var cycle = new I3Cycle(
            new StubGateway([Mud("Nightfall", "82.153.225.173", 4242)]),
            targets, bindings, presence, new FakeFields(), new I3Options(), Clock());

        var result = await cycle.RunAsync(CancellationToken.None);

        await Assert.That(result.Seeded).IsEqualTo(1);
        var planted = targets.Added.Single();
        await Assert.That(planted.Host).IsEqualTo("82.153.225.173");
        await Assert.That(planted.Port).IsEqualTo(4242);

        // The security-relevant half. These are stranger-supplied addresses and the scope guard has
        // to rule on every one of them, exactly as it does on a REFERRAL.
        await Assert.That(planted.IsOperatorSeed).IsFalse();

        // No game, no count: an address that has not answered for itself is not a game (§7.1).
        await Assert.That(presence.Written).IsEmpty();
    }

    /// <summary>
    /// Binding is a lookup, not a match. The address was seeded on an earlier pass, the ordinary
    /// crawl probed and promoted it, and this pass asks which game that turned out to be.
    /// </summary>
    [Test]
    public async Task AMudWhoseAddressHasBeenPromotedIsBoundToThatGame()
    {
        var game = Guid.CreateVersion7();
        var targets = new FakeTargets();
        targets.Existing[("82.153.225.173", 4242)] = game;
        var bindings = new FakeBindings();

        var cycle = new I3Cycle(
            new StubGateway([Mud("Nightfall", "82.153.225.173", 4242)]),
            targets, bindings, new FakePresence(), new FakeFields(), new I3Options(), Clock());

        var result = await cycle.RunAsync(CancellationToken.None);

        await Assert.That(result.Bound).IsEqualTo(1);
        await Assert.That(result.Seeded).IsEqualTo(0);
        await Assert.That(bindings.Rows["Nightfall"].GameId).IsEqualTo(game);
    }

    /// <summary>
    /// What the mudlist says a bound game runs, which arrived on every pass and was dropped.
    /// </summary>
    /// <remarks>
    /// <c>I3Mud.Driver</c>, <c>Mudlib</c> and <c>MudType</c> were modelled when the gateway was
    /// written and nothing read them until now. Asked of the live network on 2026-08-17, 179 of 179
    /// entries filled in all three, and 47 of the games bound to an I3 name had no codebase on
    /// record at all.
    /// </remarks>
    [Test]
    public async Task WhatABoundMudSaysItRunsIsRecordedAsTheClaimItIs()
    {
        var game = Guid.CreateVersion7();
        var targets = new FakeTargets();
        targets.Existing[("136.144.155.250", 8888)] = game;
        var fields = new FakeFields();

        var cycle = new I3Cycle(
            new StubGateway([
                Mud("Mystic Falls", "136.144.155.250", 8888) with
                {
                    Driver = "FluffOS v2.23-ds03",
                    Mudlib = "Dead Souls 3.9",
                    MudType = "LPMud",
                },
            ]),
            targets, new FakeBindings(), new FakePresence(), fields, new I3Options(), Clock());

        await cycle.RunAsync(CancellationToken.None);

        var codebase = fields.Written.Single(f => f.Field == FieldObservations.CodebaseField);
        var mudlib = fields.Written.Single(f => f.Field == I3Description.MudlibField);
        var family = fields.Written.Single(f => f.Field == I3Description.FamilyField);

        // The driver is the codebase and the mudlib is not. Dead Souls is a library; FluffOS is what
        // the game runs, and MSSP means the same thing by CODEBASE.
        await Assert.That(codebase.Value).IsEqualTo("FluffOS v2.23-ds03");
        await Assert.That(mudlib.Value).IsEqualTo("Dead Souls 3.9");
        await Assert.That(family.Value).IsEqualTo("LPMud");

        // Declared, on the rung below MSSP: the mud told a router at some past startup and the router
        // repeated it onward. A game filling in its own MSSP still outranks all three.
        await Assert.That(codebase.Source).IsEqualTo(FieldSource.I3Mudlist);
        await Assert.That(FieldSources.IsMeasured(FieldSource.I3Mudlist)).IsFalse();
    }

    [Test]
    public async Task AnUnboundMudStillContributesNothingButItsAddress()
    {
        // The fields above are written only where there is a game to write them against. An address
        // that has not answered for itself is not a game (§7.1), and §7.6 takes host and port.
        var fields = new FakeFields();

        var cycle = new I3Cycle(
            new StubGateway([
                Mud("Nightfall", "82.153.225.173", 4242) with { Driver = "DGD 1.4.1", MudType = "LPMud" },
            ]),
            new FakeTargets(), new FakeBindings(), new FakePresence(), fields, new I3Options(), Clock());

        await cycle.RunAsync(CancellationToken.None);

        await Assert.That(fields.Written).IsEmpty();
    }

    /// <summary>
    /// One game under several I3 names binds once and the pass carries on.
    /// </summary>
    /// <remarks>
    /// <b>This is a defect the end-to-end run found, not a hypothetical.</b> The live network lists
    /// <c>The Zone</c>, <c>The Zone-dalet</c>, <c>The Zone-i4</c> and <c>The Zone-wpr</c> at one
    /// address — one mud registered once per router — and the unique index that stops a game being
    /// counted twice threw out of the cycle, so every pass died at the same mud on every retry.
    /// </remarks>
    [Test]
    public async Task AGameWithSeveralI3NamesBindsOnceAndTheRestDoNotStopThePass()
    {
        var game = Guid.CreateVersion7();
        var targets = new FakeTargets();
        targets.Existing[("136.144.155.250", 8888)] = game;
        var bindings = new FakeBindings();

        var cycle = new I3Cycle(
            new StubGateway([
                Mud("The Zone", "136.144.155.250", 8888),
                Mud("The Zone-dalet", "136.144.155.250", 8888),
                Mud("The Zone-i4", "136.144.155.250", 8888),
                Mud("The Zone-wpr", "136.144.155.250", 8888),
            ]),
            targets, bindings, new FakePresence(), new FakeFields(),
            new I3Options { BetweenAsks = TimeSpan.Zero }, Clock());

        var result = await cycle.RunAsync(CancellationToken.None);

        await Assert.That(result.Listed).IsEqualTo(4);
        await Assert.That(result.Bound).IsEqualTo(1);
        await Assert.That(bindings.Rows.Values.Count(r => r.GameId is not null)).IsEqualTo(1);

        // All four are still recorded: a name we cannot bind is still a name the network uses, and
        // forgetting it would mean rediscovering it every pass.
        await Assert.That(bindings.Rows.Count).IsEqualTo(4);
    }

    /// <summary>
    /// A mud publishing port 0 has said there is nothing to dial — the spec permits it for a private
    /// or closed mud, and MUIndex publishes exactly that about itself. Recording it is right; turning
    /// it into a crawl target is not.
    /// </summary>
    [Test]
    public async Task AMudWithNoPlayerPortIsRecordedButNeverSeeded()
    {
        var targets = new FakeTargets();
        var bindings = new FakeBindings();

        var cycle = new I3Cycle(
            new StubGateway([Mud("Epitaph", "135.181.197.219", 0)]),
            targets, bindings, new FakePresence(), new FakeFields(), new I3Options(), Clock());

        var result = await cycle.RunAsync(CancellationToken.None);

        await Assert.That(result.Unlistable).IsEqualTo(1);
        await Assert.That(targets.Added).IsEmpty();
        await Assert.That(bindings.Rows).ContainsKey("Epitaph");
    }

    /// <summary>An answered who becomes a presence row under the source it came down.</summary>
    [Test]
    public async Task AnAnsweredWhoIsCountedAgainstTheBoundGame()
    {
        var game = Guid.CreateVersion7();
        var bindings = new FakeBindings();
        bindings.Rows["Nightfall"] = Bound("Nightfall", game);
        var presence = new FakePresence();

        var gateway = new StubGateway([Mud("Nightfall", "82.153.225.173", 4242)])
        {
            Replies = { ["Nightfall"] = new I3WhoReply { FromMud = "Nightfall", Users = [new(), new(), new()] } },
        };

        var targets = new FakeTargets();
        targets.Existing[("82.153.225.173", 4242)] = game;

        var cycle = new I3Cycle(
            gateway, targets, bindings, presence, new FakeFields(),
            new I3Options { BetweenAsks = TimeSpan.Zero }, Clock());

        await cycle.RunAsync(CancellationToken.None);

        var sample = presence.Written.Single();
        await Assert.That(sample.GameId).IsEqualTo(game);
        await Assert.That(sample.Count).IsEqualTo(3);
        await Assert.That(sample.Source).IsEqualTo(FieldSource.I3);
    }

    /// <summary>
    /// Silence is the middle state of §5.4 and says so. It is not a zero, and it is not filed under
    /// the telnet pipe.
    /// </summary>
    [Test]
    public async Task SilenceIsRecordedAsUnmeasurableUnderTheI3Source()
    {
        var game = Guid.CreateVersion7();
        var bindings = new FakeBindings();
        bindings.Rows["Silent"] = Bound("Silent", game);
        var presence = new FakePresence();

        // Listed, up, advertising who — and silent. An empty mudlist would instead mean the gateway
        // is disconnected, which is a different thing and correctly skips the ask entirely.
        var cycle = new I3Cycle(
            new StubGateway([Mud("Silent", "203.0.113.1", 4000)]), new FakeTargets(), bindings, presence,
            new FakeFields(), new I3Options { BetweenAsks = TimeSpan.Zero }, Clock());

        await cycle.RunAsync(CancellationToken.None);

        var sample = presence.Written.Single();
        await Assert.That(sample.Count).IsNull();
        await Assert.That(sample.Reason).IsEqualTo(UnmeasurableReason.I3NoReply);
        await Assert.That(sample.Source).IsEqualTo(FieldSource.I3);
    }

    /// <summary>
    /// Marked asked before the answer, so a mud that never replies does not become the one we bother
    /// most.
    /// </summary>
    [Test]
    public async Task AMudThatDoesNotAnswerIsStillMarkedAsAsked()
    {
        var bindings = new FakeBindings();
        bindings.Rows["Silent"] = Bound("Silent", Guid.CreateVersion7());

        var cycle = new I3Cycle(
            new StubGateway([Mud("Silent", "203.0.113.1", 4000)]), new FakeTargets(), bindings,
            new FakePresence(), new FakeFields(), new I3Options { BetweenAsks = TimeSpan.Zero }, Clock());

        await cycle.RunAsync(CancellationToken.None);

        await Assert.That(bindings.Rows["Silent"].LastAskedAt).IsEqualTo(Now);
    }

    /// <summary>
    /// An empty mudlist means the gateway is not connected, not that the network is empty. Seeding
    /// nothing is already the case; the point is that it must not be read as evidence of anything.
    /// </summary>
    [Test]
    public async Task AnEmptyMudlistIsTreatedAsNoAnswerRatherThanAnEmptyNetwork()
    {
        var targets = new FakeTargets();
        var presence = new FakePresence();

        var result = await new I3Cycle(
            new StubGateway([]), targets, new FakeBindings(), presence, new FakeFields(), new I3Options(), Clock())
            .RunAsync(CancellationToken.None);

        await Assert.That(result.Listed).IsEqualTo(0);
        await Assert.That(targets.Added).IsEmpty();
        await Assert.That(presence.Written).IsEmpty();
    }

    /// <summary>Stopped at <see cref="Now"/>, so every row one pass writes is comparable.</summary>
    private static TimeProvider Clock() => new Support.SettableClock(Now);

    private static I3Binding Bound(string name, Guid game) => new()
    {
        MudName = name,
        GameId = game,
        Host = "203.0.113.1",
        Port = 4000,
        AnswersWho = true,
        FirstSeenAt = Now,
        LastSeenAt = Now,
    };

    private sealed class StubGateway(IReadOnlyList<I3Mud> muds) : II3Gateway
    {
        public Dictionary<string, I3WhoReply> Replies { get; } = [];

        public Task<IReadOnlyList<I3Mud>> MudlistAsync(CancellationToken ct = default) =>
            Task.FromResult(muds);

        public Task<I3WhoReply?> WhoAsync(string mud, CancellationToken ct = default) =>
            Task.FromResult(Replies.GetValueOrDefault(mud));
    }

    private sealed class FakeTargets : ICrawlTargetRepository
    {
        public Dictionary<(string, int), Guid?> Existing { get; } = [];

        public List<CrawlTarget> Added { get; } = [];

        public Task<CrawlTarget?> ByAddressAsync(string host, int port, CancellationToken ct) =>
            Task.FromResult(Existing.TryGetValue((host, port), out var game)
                ? new CrawlTarget
                {
                    Id = Guid.CreateVersion7(),
                    GameId = game,
                    Host = host,
                    Port = port,
                    NextProbeAt = Now,
                    FirstSeenAt = Now,
                }
                : null);

        public Task<Guid> AddAsync(CrawlTarget target, CancellationToken ct)
        {
            Added.Add(target);
            return Task.FromResult(target.Id);
        }

        public Task<IReadOnlyList<CrawlTarget>> DueAsync(DateTimeOffset now, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CrawlTarget>>([]);

        public Task RecordAttemptAsync(
            Guid id, DateTimeOffset at, bool succeeded, TimeSpan? crawlDelay,
            DateTimeOffset nextProbeAt, CancellationToken ct) => Task.CompletedTask;

        public Task AttachGameAsync(Guid id, Guid gameId, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeBindings : II3BindingRepository
    {
        public Dictionary<string, I3Binding> Rows { get; } = [];

        public Task UpsertAsync(
            string mudName, string host, int port, bool answersWho, DateTimeOffset seenAt,
            CancellationToken ct)
        {
            Rows[mudName] = Rows.TryGetValue(mudName, out var existing)
                ? existing with { Host = host, Port = port, AnswersWho = answersWho, LastSeenAt = seenAt }
                : new I3Binding
                {
                    MudName = mudName,
                    Host = host,
                    Port = port,
                    AnswersWho = answersWho,
                    FirstSeenAt = seenAt,
                    LastSeenAt = seenAt,
                };

            return Task.CompletedTask;
        }

        public Task<bool> BindAsync(string mudName, Guid gameId, CancellationToken ct)
        {
            // One game, one mud — the unique index's rule, kept here so the double lives in the same
            // world the database does.
            if (Rows.Values.Any(r => r.GameId == gameId))
            {
                return Task.FromResult(false);
            }

            if (Rows.TryGetValue(mudName, out var row) && row.GameId is null)
            {
                Rows[mudName] = row with { GameId = gameId };
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }

        public Task<IReadOnlyList<I3Binding>> AllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<I3Binding>>([.. Rows.Values]);

        public Task<IReadOnlyList<I3Binding>> AskableAsync(
            DateTimeOffset notSince, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<I3Binding>>(
                [.. Rows.Values
                    .Where(r => r.GameId is not null && r.AnswersWho
                        && (r.LastAskedAt is null || r.LastAskedAt < notSince))
                    .Take(limit)]);

        public Task MarkAskedAsync(string mudName, DateTimeOffset at, CancellationToken ct)
        {
            if (Rows.TryGetValue(mudName, out var row))
            {
                Rows[mudName] = row with { LastAskedAt = at };
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeFields : IGameFieldStore
    {
        public List<GameField> Written { get; } = [];

        public Task UpsertAsync(GameField field, CancellationToken ct = default)
        {
            Written.Add(field);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<GameField>> ForGameAsync(Guid gameId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GameField>>([]);

        public Task<IReadOnlyList<GameField>> ForGameAsync(
            Guid gameId, FieldSource source, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GameField>>([]);

        public Task RecordChangeAsync(FieldChange change, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<DateTimeOffset?> LastChangedAtAsync(Guid gameId, string field, CancellationToken ct = default) =>
            Task.FromResult<DateTimeOffset?>(null);
    }

    private sealed class FakePresence : IPresenceStore
    {
        public List<PresenceSample> Written { get; } = [];

        public Task AppendAsync(PresenceSample sample, CancellationToken ct)
        {
            Written.Add(sample);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PresenceSample>> ForGameAsync(
            Guid gameId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PresenceSample>>([]);
    }
}
