using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// Spec §11 — "aggregates use salted hashes with a rotating salt, so a unique-player estimate is
/// possible while re-identification across salt epochs is not".
/// </summary>
/// <remarks>
/// Both halves of that sentence are testable and both are tested here, because a rotating salt that
/// never rotates keeps the first half and quietly drops the second, and nothing downstream would ever
/// notice.
/// </remarks>
public class SaltTests
{
    private static readonly DateTimeOffset Anchor = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static RotatingSaltProvider Weekly(string secret = "not-the-production-secret") =>
        new(new SaltRotationOptions
        {
            Period = TimeSpan.FromDays(7),
            Anchor = Anchor,
            Secret = secret,
        });

    [Test]
    public async Task OneNameHashesToOneValueForAsLongAsTheEpochLasts()
    {
        // The half that makes an estimate possible: two probes an hour apart can tell that they saw
        // the same player without either of them recording who it was.
        var salt = Weekly();

        var early = salt.Current(Anchor.AddHours(1)).Hash("Corvid");
        var late = salt.Current(Anchor.AddDays(6).AddHours(23)).Hash("Corvid");

        await Assert.That(early).IsEqualTo(late);
        await Assert.That(early).IsNotEqualTo(salt.Current(Anchor).Hash("Rook"));
    }

    [Test]
    public async Task TheSameNameIsUnrecognisableInTheNextEpoch()
    {
        // The half that makes re-identification impossible. Without this, a hash is a stable
        // pseudonym for a player for ever, which is the thing not persisting names was for.
        var salt = Weekly();

        var thisWeek = salt.Current(Anchor.AddDays(1));
        var nextWeek = salt.Current(Anchor.AddDays(8));

        await Assert.That(nextWeek.Label).IsNotEqualTo(thisWeek.Label);
        await Assert.That(nextWeek.Hash("Corvid")).IsNotEqualTo(thisWeek.Hash("Corvid"));
    }

    [Test]
    public async Task TheEpochLabelNamesTheInstantItBeganAndNothingElse()
    {
        // The label is the only part of this that is ever written down, so it may carry nothing the
        // salt could be derived from and nothing about a player.
        var salt = Weekly();

        await Assert.That(salt.Current(Anchor.AddDays(3)).Label).IsEqualTo("20260101T000000Z");
        await Assert.That(salt.Current(Anchor.AddDays(10)).Label).IsEqualTo("20260108T000000Z");
    }

    [Test]
    public async Task AHashIsFixedWidthAndCarriesNoneOfTheNameItCameFrom()
    {
        var epoch = Weekly().Current(Anchor);

        var hash = epoch.Hash("Corvid");

        await Assert.That(hash.Length).IsEqualTo(32);
        await Assert.That(hash.Contains("Corvid", StringComparison.OrdinalIgnoreCase)).IsFalse();
        await Assert.That(hash).IsNotEqualTo(Weekly().Current(Anchor).Hash("corvid"));
    }

    [Test]
    public async Task TwoReplicasSharingASecretAgreeAndTwoWithoutOneDoNot()
    {
        // A deployment-wide secret is what makes the estimate deployment-wide. Without one every
        // process invents its own salt, which is a weaker number rather than a shared pseudonym — the
        // safe way round for a default.
        var first = Weekly("shared");
        var second = Weekly("shared");

        await Assert.That(first.Current(Anchor).Hash("Corvid"))
            .IsEqualTo(second.Current(Anchor).Hash("Corvid"));

        var lonely = new RotatingSaltProvider(new SaltRotationOptions { Anchor = Anchor });
        var alsoLonely = new RotatingSaltProvider(new SaltRotationOptions { Anchor = Anchor });

        await Assert.That(lonely.Current(Anchor).Hash("Corvid"))
            .IsNotEqualTo(alsoLonely.Current(Anchor).Hash("Corvid"));
    }

    [Test]
    public async Task ASaltThatNeverRotatesIsRefused()
    {
        await Assert.That(() => new RotatingSaltProvider(new SaltRotationOptions { Period = TimeSpan.Zero }))
            .Throws<ArgumentException>();
    }
}
