using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// The scored fingerprint of spec §7.3. Duplicate listings are the specific failure that clutters every
/// incumbent catalogue, and this is the component that prevents it.
/// </summary>
public class IdentityMatcherTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static readonly DateTimeOffset Then = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AnUnknownGameIsFresh()
    {
        var world = new IdentityWorld();

        var verdict = await world.Matcher.ResolveAsync(
            ProbeResults.Answered(mssp: ProbeResults.Mssp(("NAME", "Corvid"))), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
        await Assert.That(((IdentityVerdict.Fresh)verdict).Best).IsNull();
    }

    [Test]
    public async Task AKnownEndpointIsTheGameAndMergesOnItsOwn()
    {
        // Weight 1.00 equals the auto-merge threshold, deliberately: a previously-seen (host, port) is
        // direct continuity and needs no corroboration.
        var world = new IdentityWorld();
        var corvid = await world.GameAsync();
        await world.EndpointAsync(corvid, "mud.example.org", 4201);

        var verdict = await world.Matcher.ResolveAsync(ProbeResults.Answered(), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Merge>();
        var merge = (IdentityVerdict.Merge)verdict;
        await Assert.That(merge.GameId).IsEqualTo(corvid);
        await Assert.That(merge.Score.Score).IsGreaterThanOrEqualTo(IdentityWeights.AutoMergeThreshold);
    }

    [Test]
    public async Task NameAndCreatedTogetherAreMiddlingAndOpenAReview()
    {
        var world = new IdentityWorld();
        await world.GameAsync((IdentityFields.Name, "Corvid"), (IdentityFields.Created, "2003"));

        var verdict = await world.Matcher.ResolveAsync(ProbeResults.Answered(
            host: "new.example.org",
            mssp: ProbeResults.Mssp(("NAME", "Corvid"), ("CREATED", "2003"))), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Review>();
        await Assert.That(((IdentityVerdict.Review)verdict).Score.Score)
            .IsEqualTo(IdentityWeights.MsspNameAndCreated);
    }

    [Test]
    public async Task NameAloneScoresNothingBecauseCreatedIsWhatMakesItSpecific()
    {
        var world = new IdentityWorld();
        await world.GameAsync((IdentityFields.Name, "Corvid"), (IdentityFields.Created, "2003"));

        var verdict = await world.Matcher.ResolveAsync(ProbeResults.Answered(
            host: "new.example.org",
            mssp: ProbeResults.Mssp(("NAME", "Corvid"))), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
        await Assert.That(((IdentityVerdict.Fresh)verdict).Best!.Score).IsEqualTo(0d);
    }

    [Test]
    public async Task NameAndCreatedWithABannerMatchIsEnoughToMerge()
    {
        // 0.60 + 0.50 = 1.10. This is the known-move shape, and it is the reason the two weights add to
        // more than the threshold rather than exactly to it.
        var world = new IdentityWorld();
        // Long enough to be a connect screen rather than a colour prompt (BannerFingerprint
        // .MinimumIdentifyingLength). The 33-character version this used to carry was shorter than any
        // screen in the live catalogue, which is how a fixture comes to agree with a bug.
        const string banner = "Welcome to Corvid.\nA place for slow stories.\nType 'connect'.";
        var corvid = await world.GameAsync(
            (IdentityFields.Name, "Corvid"),
            (IdentityFields.Created, "2003"),
            (IdentityFields.BannerHash, BannerFingerprint.Of(banner)));

        var verdict = await world.Matcher.ResolveAsync(ProbeResults.Answered(
            host: "new.example.org",
            mssp: ProbeResults.Mssp(("NAME", "Corvid"), ("CREATED", "2003")),
            banner: banner), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Merge>();
        await Assert.That(((IdentityVerdict.Merge)verdict).GameId).IsEqualTo(corvid);
    }

    [Test]
    public async Task TheSignalsAreReportedWhetherOrNotTheyMatched()
    {
        // A review is a thing a human reads. "Which six signals were considered and how did each land"
        // is the whole content of that judgement, so the losing signals are carried too.
        var world = new IdentityWorld();
        await world.GameAsync((IdentityFields.Name, "Corvid"), (IdentityFields.Created, "2003"));

        var verdict = await world.Matcher.ResolveAsync(ProbeResults.Answered(
            host: "new.example.org",
            mssp: ProbeResults.Mssp(("NAME", "Corvid"), ("CREATED", "2003"))), None);

        var score = ((IdentityVerdict.Review)verdict).Score;

        await Assert.That(score.Signals.Count).IsEqualTo(7);
        await Assert.That(score.Signals.Count(s => s.Matched)).IsEqualTo(1);
        await Assert.That(score.Signals.Sum(s => s.Weight)).IsGreaterThan(score.Score);
    }

    [Test]
    public async Task WebsiteAndCodebaseTogetherReachReviewButNotMerge()
    {
        // 0.40 + 0.15 = 0.55: worth a human's eye, nowhere near enough to fold two games together.
        var world = new IdentityWorld();
        await world.GameAsync(
            (IdentityFields.Website, "https://corvid.example"),
            (IdentityFields.Codebase, "PennMUSH 1.8.8p2"));

        var verdict = await world.Matcher.ResolveAsync(ProbeResults.Answered(
            host: "new.example.org",
            mssp: ProbeResults.Mssp(
                ("NAME", "Corvid"),
                ("WEBSITE", "https://corvid.example"),
                ("CODEBASE", "PennMUSH 1.8.8p2"))), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Review>();
    }

    [Test]
    public async Task CodebaseCanComeFromInfoWhenMsspCodebaseIsAbsent()
    {
        var world = new IdentityWorld();
        await world.GameAsync(
            (IdentityFields.Codebase, "RhostMUSH 4.27.3"),
            (IdentityFields.Website, "https://convergence.example"));

        var verdict = await world.Matcher.ResolveAsync(ProbeResults.Answered(
            host: "new.example.org",
            mssp: ProbeResults.Mssp(("WEBSITE", "https://convergence.example")),
            info: """
                ### Begin INFO 1
                Name: Convergence MUSH
                Version: RhostMUSH 4.27.3
                ### End INFO
                """), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Review>();
        await Assert.That(((IdentityVerdict.Review)verdict).Score.Score)
            .IsEqualTo(IdentityWeights.WebsiteOrContact + IdentityWeights.CodebaseAndVersion);
    }

    [Test]
    public async Task TheThresholdsAreConfigurableBecauseTheyNeedCalibrating()
    {
        // Spec §15.5. Ship conservative, tune against real data — without a redeploy of the constants.
        var strict = new IdentityWorld
        {
            Options = new DiscoveryOptions { AutoMergeThreshold = 2.0, ReviewThreshold = 1.5 },
        };
        var corvid = await strict.GameAsync();
        await strict.EndpointAsync(corvid, "mud.example.org", 4201);

        var verdict = await strict.Matcher.ResolveAsync(ProbeResults.Answered(), None);

        // The endpoint's 1.00 no longer clears either bar.
        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
    }

    [Test]
    public async Task ACandidateWhoseGameHasGoneIsIgnoredRatherThanReturned()
    {
        var world = new IdentityWorld();
        var orphan = await world.GameAsync();
        await world.EndpointAsync(orphan, "mud.example.org", 4201);
        world.Games.Forget(orphan);

        var verdict = await world.Matcher.ResolveAsync(ProbeResults.Answered(), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
    }

    [Test]
    public async Task AFailedProbeResolvesToNothingRatherThanGuessing()
    {
        // Parsers never fabricate, and neither does this. A refused connection carries no MSSP, no
        // banner and no evidence of any kind — including the address it was refused at.
        var world = new IdentityWorld();
        var corvid = await world.GameAsync();
        await world.EndpointAsync(corvid, "mud.example.org", 4201);

        var verdict = await world.Matcher.ResolveAsync(ProbeResults.Failed(), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
        await Assert.That(((IdentityVerdict.Fresh)verdict).Best).IsNull();
    }

    [Test]
    public async Task AnOwnerCannotTypeTheirGameIntoAnotherGamesFingerprint()
    {
        // The hazard §8.5's widened writable set introduces, and the reason this test exists before
        // that set widens. Owner outranks Mssp in FieldPrecedence — correctly, for a page, where an
        // owner's answer about their own game is the one to show. Scored here it would let anybody
        // with a verified claim type NAME and CREATED to match a game they have nothing to do with,
        // and §7.3 merges above threshold.
        //
        // De-duplication asks which host is which game. A person's typing is not evidence in that
        // question, however true it happens to be.
        var world = new IdentityWorld();
        var impostor = await world.GameAsync((IdentityFields.Name, "Something Else"));

        foreach (var (field, value) in new[] { (IdentityFields.Name, "Corvid"), (IdentityFields.Created, "2003") })
        {
            await world.Fields.UpsertAsync(new MUI.Catalog.GameField(
                impostor, field, MUI.Catalog.FieldSource.Owner, value, Then, Then), None);
        }

        var verdict = await world.Matcher.ResolveAsync(ProbeResults.Answered(
            host: "new.example.org",
            mssp: ProbeResults.Mssp(("NAME", "Corvid"), ("CREATED", "2003"))), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
    }

    [Test]
    public async Task ABareIpResolvingToAKnownHostnameMergesOnceAnyOtherSignalFoundTheCandidate()
    {
        // The I3 shadow-game shape: nightfall.org is on record and its only other trace on the wire is
        // WEBSITE — on its own worth only a review (0.40). WEBSITE is one of the fields GatherAsync
        // actually reverse-looks-up, so it is what finds the candidate at all; ResolvedEndpoint then
        // corroborates on its own strength (1.00), exactly as Endpoint would if the address had matched
        // literally instead of by resolution.
        var world = new IdentityWorld { Resolver = new FakeHostResolver().Resolving("nightfall.org", "45.79.224.33") };
        var nightfall = await world.GameAsync((IdentityFields.Website, "https://nightfall.example"));
        await world.EndpointAsync(nightfall, "nightfall.org", 4201);

        var verdict = await world.Matcher.ResolveAsync(
            ProbeResults.Answered(
                host: "45.79.224.33",
                port: 4201,
                mssp: ProbeResults.Mssp(("WEBSITE", "https://nightfall.example"))),
            None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Merge>();
        var merge = (IdentityVerdict.Merge)verdict;
        await Assert.That(merge.GameId).IsEqualTo(nightfall);
        await Assert.That(merge.Score.Score).IsGreaterThanOrEqualTo(IdentityWeights.AutoMergeThreshold);
    }

    [Test]
    public async Task ABareIpWithNoOtherEvidenceAtAllGathersNoCandidateEvenWithAResolverWired()
    {
        // ResolvedEndpoint corroborates a candidate CandidatesAsync already found; it is not itself a
        // gathering step, deliberately — resolving every listed game's hostname against a bare IP that
        // matched nothing else would be an unbounded DNS fan-out per probe, and it would let a stranger
        // reach an existing game with no textual evidence in common at all.
        var world = new IdentityWorld { Resolver = new FakeHostResolver().Resolving("nightfall.org", "45.79.224.33") };
        var nightfall = await world.GameAsync();
        await world.EndpointAsync(nightfall, "nightfall.org", 4201);

        var verdict = await world.Matcher.ResolveAsync(
            ProbeResults.Answered(host: "45.79.224.33", port: 4201), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
        await Assert.That(((IdentityVerdict.Fresh)verdict).Best).IsNull();
    }

    [Test]
    public async Task WithNoResolverWiredTheBareIpShapeStaysWhateverItWasBefore()
    {
        // The degrade-gracefully guarantee: a caller that has not wired a resolver gets exactly the
        // pre-fix behaviour, never an exception and never a silently wrong merge.
        var world = new IdentityWorld
        {
            Resolver = null,
        };
        var nightfall = await world.GameAsync((IdentityFields.BannerHash, "irrelevant-without-a-banner-match"));
        await world.EndpointAsync(nightfall, "nightfall.org", 4201);

        var verdict = await world.Matcher.ResolveAsync(
            ProbeResults.Answered(host: "45.79.224.33", port: 4201), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
    }

    [Test]
    public async Task ADifferentPortAtTheSameResolvedAddressDoesNotMatch()
    {
        // Two listeners cannot share one address and port, but they can easily share one address on
        // different ports — a second game hosted on the same box. That must not merge.
        var world = new IdentityWorld { Resolver = new FakeHostResolver().Resolving("nightfall.org", "45.79.224.33") };
        var nightfall = await world.GameAsync();
        await world.EndpointAsync(nightfall, "nightfall.org", 4201);

        var verdict = await world.Matcher.ResolveAsync(
            ProbeResults.Answered(host: "45.79.224.33", port: 9999), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
    }

    [Test]
    public async Task AResolvedAddressThatMatchesNobodyIsJustAnAbsence()
    {
        var world = new IdentityWorld { Resolver = new FakeHostResolver().Resolving("nightfall.org", "45.79.224.33") };
        var nightfall = await world.GameAsync();
        await world.EndpointAsync(nightfall, "nightfall.org", 4201);

        var verdict = await world.Matcher.ResolveAsync(
            ProbeResults.Answered(host: "203.0.113.9", port: 4201), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
    }

    [Test]
    public async Task AHostnameProbeNeverUsesResolvedEndpointEvenWithAResolverWired()
    {
        // Hostname-to-hostname is Endpoint's comparison, unchanged. ResolvedEndpoint's job starts and
        // ends at a probe that arrived by literal address.
        var world = new IdentityWorld
        {
            Resolver = new FakeHostResolver().Resolving("nightfall.org", "45.79.224.33"),
        };
        var nightfall = await world.GameAsync();
        await world.EndpointAsync(nightfall, "nightfall.org", 4201);

        var verdict = await world.Matcher.ResolveAsync(
            ProbeResults.Answered(host: "mirror.nightfall.org", port: 4201), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
    }

    [Test]
    public async Task ResolvedEndpointCorroboratesABannerMatchAcrossTheReviewThreshold()
    {
        // The shape nine confirmed production pairs actually had: BannerHash alone (0.50) sat between
        // ReviewThreshold and AutoMergeThreshold and stuck there for ever. A resolver wired in closes
        // exactly this gap.
        const string banner = "Welcome to Nightfall.\nA world of shadow and steel.\nType 'connect'.";
        var world = new IdentityWorld { Resolver = new FakeHostResolver().Resolving("nightfall.org", "45.79.224.33") };
        var nightfall = await world.GameAsync((IdentityFields.BannerHash, BannerFingerprint.Of(banner)));
        await world.EndpointAsync(nightfall, "nightfall.org", 4201);

        var verdict = await world.Matcher.ResolveAsync(
            ProbeResults.Answered(host: "45.79.224.33", port: 4201, banner: banner), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Merge>();
    }

    [Test]
    public async Task AStrangerCannotManufactureACandidateByDiallingFromAResolvedAddressAlone()
    {
        // ResolvedEndpoint only ever corroborates a candidate CandidatesAsync already gathered by some
        // other signal (name, banner, website, contact, claim token). An IP that happens to resolve the
        // same as an unrelated listed game's hostname, with nothing else in common, must gather no
        // candidate at all — GatherAsync never asks a resolver anything.
        var world = new IdentityWorld { Resolver = new FakeHostResolver().Resolving("nightfall.org", "45.79.224.33") };
        var nightfall = await world.GameAsync();
        await world.EndpointAsync(nightfall, "nightfall.org", 4201);

        var verdict = await world.Matcher.ResolveAsync(
            ProbeResults.Answered(
                host: "45.79.224.33",
                port: 4201,
                mssp: ProbeResults.Mssp(("NAME", "Something Entirely Unrelated"))),
            None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
    }

    [Test]
    public async Task TheWinningSourceIsWhatIsCompared()
    {
        // (game, field, source) means several rows can answer one field. Comparing against an arbitrary
        // one would make the match depend on dictionary order; the site shows the winner, so identity
        // scores the winner.
        var world = new IdentityWorld();
        var corvid = await world.GameAsync((IdentityFields.Name, "Old Name"), (IdentityFields.Created, "2003"));
        await world.Fields.UpsertAsync(new MUI.Catalog.GameField(
            corvid, IdentityFields.Name, MUI.Catalog.FieldSource.Staff, "Corvid",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)), None);

        var verdict = await world.Matcher.ResolveAsync(ProbeResults.Answered(
            host: "new.example.org",
            mssp: ProbeResults.Mssp(("NAME", "Corvid"), ("CREATED", "2003"))), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Review>();
    }
}
