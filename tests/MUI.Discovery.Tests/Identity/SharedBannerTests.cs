using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// A connect screen several unrelated listings publish is the codebase's, not a game's (spec §7.3).
/// </summary>
/// <remarks>
/// <para>
/// Found in production, not reasoned about. <see cref="BannerFingerprint.MinimumIdentifyingLength"/>
/// was the whole defence and it is a length floor: an unedited RhostMUSH sends 983 characters of
/// "Welcome to RhostMUSH" plus a connect-command legend, six unrelated games behind one host published
/// it byte for byte, and every pair of them opened a duplicate review at exactly
/// <see cref="IdentityWeights.BannerHash"/> — over <see cref="IdentityWeights.ReviewThreshold"/> on its
/// own. Stock TinyMUX and TinyMUSH screens did the same. Fifteen of the sixty-one reviews open on
/// 2026-08-21 were this and nothing else, and no evidence could ever have settled one.
/// </para>
/// <para>
/// The floor stays: this is the other half of the same idea, measured from the catalogue rather than
/// asserted from a list of engines, so a codebase that ships a distinctive screen needs no entry
/// anywhere and a game that hand-edits one keeps its signal.
/// </para>
/// </remarks>
public class SharedBannerTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    /// <summary>An unedited RhostMUSH's screen, shortened. Says which engine sent it and nothing else.</summary>
    private const string StockScreen =
        "Welcome to RhostMUSH\n"
        + "  To create a new character: create <name> <password>\n"
        + "  To connect: connect <name> <password>\n"
        + "  To see who is on: WHO\n";

    [Test]
    public async Task TwoListingsSharingAScreenStillScoreIt()
    {
        // The ordinary case the signal exists for: one game answering at two addresses. Two is a
        // duplicate, and suppressing here would suppress exactly what we are trying to find.
        var world = new IdentityWorld();
        var first = await world.GameAsync((IdentityFields.BannerHash, BannerFingerprint.Of(StockScreen)));

        var verdict = await world.Matcher.ResolveAsync(
            ProbeResults.Answered(host: "second.example.org", banner: StockScreen), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Review>();
        await Assert.That(((IdentityVerdict.Review)verdict).GameId).IsEqualTo(first);
    }

    [Test]
    public async Task AScreenThreeListingsPublishContributesNothing()
    {
        var world = new IdentityWorld();
        var hash = BannerFingerprint.Of(StockScreen);
        await world.GameAsync((IdentityFields.BannerHash, hash));
        await world.GameAsync((IdentityFields.BannerHash, hash));
        await world.GameAsync((IdentityFields.BannerHash, hash));

        var verdict = await world.Matcher.ResolveAsync(
            ProbeResults.Answered(host: "fourth.example.org", banner: StockScreen), None);

        // Fresh, not a low-scoring review: a non-answer contributes nothing rather than a little, so it
        // may not nominate a candidate either.
        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
        await Assert.That(((IdentityVerdict.Fresh)verdict).Best).IsNull();
    }

    [Test]
    public async Task ASuppressedScreenDoesNotDragDownTheSignalsBesideIt()
    {
        // The suppression is of one signal, not of the pair. A name and a creation year still agree,
        // and that is still worth a review.
        var world = new IdentityWorld();
        var hash = BannerFingerprint.Of(StockScreen);
        var corvid = await world.GameAsync(
            (IdentityFields.Name, "Corvid"),
            (IdentityFields.Created, "2003"),
            (IdentityFields.BannerHash, hash));
        await world.GameAsync((IdentityFields.BannerHash, hash));
        await world.GameAsync((IdentityFields.BannerHash, hash));

        var verdict = await world.Matcher.ResolveAsync(
            ProbeResults.Answered(
                host: "elsewhere.example.org",
                mssp: ProbeResults.Mssp(("NAME", "Corvid"), ("CREATED", "2003")),
                banner: StockScreen),
            None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Review>();
        var review = (IdentityVerdict.Review)verdict;
        await Assert.That(review.GameId).IsEqualTo(corvid);
        await Assert.That(review.Score.Score).IsEqualTo(IdentityWeights.MsspNameAndCreated);

        var banner = review.Score.Signals.Single(s => s.Name == nameof(IdentityWeights.BannerHash));
        await Assert.That(banner.Matched).IsFalse();
    }

    [Test]
    public async Task ListingsAlreadyMergedCountOnce()
    {
        // One game reachable at three addresses is three game rows publishing one screen — a merge is a
        // redirect, so the absorbed rows stay and keep being probed. Counting those three as three would
        // suppress the very signal that found them, so they count as the one listing they redirect to.
        var world = new IdentityWorld();
        var hash = BannerFingerprint.Of(StockScreen);
        var survivor = await world.GameAsync((IdentityFields.BannerHash, hash));
        var absorbed = await world.GameAsync((IdentityFields.BannerHash, hash));
        var alsoAbsorbed = await world.GameAsync((IdentityFields.BannerHash, hash));

        foreach (var loser in (Guid[])[absorbed, alsoAbsorbed])
        {
            await world.Merges.RecordAsync(
                new MergeRecord(Guid.NewGuid(), survivor, loser, 1.0, "[]", DateTimeOffset.UnixEpoch, null),
                None);
        }

        var verdict = await world.Matcher.ResolveAsync(
            ProbeResults.Answered(host: "fourth.example.org", banner: StockScreen), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Review>();
        await Assert.That(((IdentityVerdict.Review)verdict).GameId).IsEqualTo(survivor);
    }

    [Test]
    public async Task AMergeThatWasRevertedStopsCollapsingItsPair()
    {
        // Reverting puts the listing back, which puts the count back up. Nothing here caches.
        var world = new IdentityWorld();
        var hash = BannerFingerprint.Of(StockScreen);
        var survivor = await world.GameAsync((IdentityFields.BannerHash, hash));
        var absorbed = await world.GameAsync((IdentityFields.BannerHash, hash));
        var third = await world.GameAsync((IdentityFields.BannerHash, hash));
        _ = third;

        var mergeId = Guid.NewGuid();
        await world.Merges.RecordAsync(
            new MergeRecord(mergeId, survivor, absorbed, 1.0, "[]", DateTimeOffset.UnixEpoch, null), None);

        var whileMerged = await world.Matcher.ResolveAsync(
            ProbeResults.Answered(host: "fourth.example.org", banner: StockScreen), None);
        await Assert.That(whileMerged).IsTypeOf<IdentityVerdict.Review>();

        await world.Merges.RevertAsync(mergeId, DateTimeOffset.UnixEpoch, None);

        var afterRevert = await world.Matcher.ResolveAsync(
            ProbeResults.Answered(host: "fourth.example.org", banner: StockScreen), None);
        await Assert.That(afterRevert).IsTypeOf<IdentityVerdict.Fresh>();
    }

    [Test]
    public async Task TheFloorIsConfigurable()
    {
        // Spec §15.5's rule for every threshold in §7.3: reasoned, unvalidated, and tuned by the
        // deployment rather than compiled in.
        var world = new IdentityWorld { Options = new DiscoveryOptions { SharedBannerListings = 4 } };
        var hash = BannerFingerprint.Of(StockScreen);
        await world.GameAsync((IdentityFields.BannerHash, hash));
        await world.GameAsync((IdentityFields.BannerHash, hash));
        var third = await world.GameAsync((IdentityFields.BannerHash, hash));
        _ = third;

        var verdict = await world.Matcher.ResolveAsync(
            ProbeResults.Answered(host: "fourth.example.org", banner: StockScreen), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Review>();
    }

    [Test]
    public async Task AFloorBelowTwoIsRefused()
    {
        await Assert.That(() => new DiscoveryOptions { SharedBannerListings = 1 }.Validate())
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ARivalIsSuppressedToo()
    {
        // RivalAsync is the arm that actually opened the fifteen: a listing already attributed to an
        // address, re-scored on every probe against everything else with the same stock screen.
        var world = new IdentityWorld();
        var hash = BannerFingerprint.Of(StockScreen);
        var bound = await world.GameAsync((IdentityFields.BannerHash, hash));
        await world.EndpointAsync(bound, "mud.example.org", 4201);
        await world.GameAsync((IdentityFields.BannerHash, hash));
        await world.GameAsync((IdentityFields.BannerHash, hash));

        var rival = await world.Matcher.RivalAsync(
            ProbeResults.Answered(banner: StockScreen), bound, None);

        await Assert.That(rival).IsNull();
    }
}
