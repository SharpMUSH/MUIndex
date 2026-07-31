using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// The scored fingerprint of spec §7.3. Duplicate listings are the specific failure that clutters every
/// incumbent catalogue, and this is the component that prevents it.
/// </summary>
public class IdentityMatcherTests
{
    private static readonly CancellationToken None = CancellationToken.None;

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
        const string banner = "Welcome to Corvid.\nType 'connect'.";
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

        await Assert.That(score.Signals.Count).IsEqualTo(6);
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
