using MUI.Crawler.Tests.Support;
using MUI.Discovery;

namespace MUI.Crawler.Tests;

/// <summary>
/// Reading the gap a server asked for (spec §7.7, §11), and composing it with the backoff.
/// </summary>
public class MsspCrawlDelayTests
{
    [Test]
    public async Task HoursAreHours()
    {
        var delay = MsspCrawlDelay.From(Probes.Answered(mssp: Probes.Mssp(("CRAWL DELAY", "24"))));

        await Assert.That(delay).IsEqualTo(TimeSpan.FromHours(24));
    }

    [Test]
    public async Task MinusOneIsNoPreferenceAndNotZero()
    {
        // MSSP's own reading. A server stating 0 has said "as often as you like"; one stating -1 has
        // said nothing, and the difference is only visible if the reader keeps it.
        await Assert.That(MsspCrawlDelay.Parse("-1")).IsNull();
        await Assert.That(MsspCrawlDelay.Parse("0")).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task AnUnreadableValueIsNotARequest()
    {
        await Assert.That(MsspCrawlDelay.Parse("soon")).IsNull();
        await Assert.That(MsspCrawlDelay.Parse(null)).IsNull();
    }

    [Test]
    public async Task AnAbsurdRequestIsClampedRatherThanHonouredIntoRetirement()
    {
        // Politeness is honoured; a typo is not allowed to remove a game from the crawl for a century,
        // which is retirement by the back door and is the one thing §7.4 forbids outright.
        await Assert.That(MsspCrawlDelay.Parse("87600")).IsEqualTo(MsspCrawlDelay.Longest);
    }

    [Test]
    public async Task AReportWeNeverReceivedStatesNoDelay()
    {
        await Assert.That(MsspCrawlDelay.From(Probes.Answered())).IsNull();
    }

    [Test]
    public async Task AStatedMonthOutranksTheWeeklyFloor()
    {
        // §7.7 resolves §7.4 against §11 in favour of politeness: the server's request is applied
        // after the weekly clamp, so a server asking for thirty days gets thirty days.
        var asked = MsspCrawlDelay.From(Probes.Answered(mssp: Probes.Mssp(("CRAWL DELAY", "720"))));

        await Assert.That(ProbeSchedule.Next(consecutiveFailures: 0, asked))
            .IsEqualTo(TimeSpan.FromDays(30));
    }
}

/// <summary>The URL segment a game is minted with (spec §5.7).</summary>
public class GameSlugTests
{
    [Test]
    public async Task NonAlphanumericsCollapseToSingleHyphens()
    {
        await Assert.That(GameSlug.Mint("Tidewater  Nights!!")).IsEqualTo("tidewater-nights");
        await Assert.That(GameSlug.Mint("M*U*S*H")).IsEqualTo("m-u-s-h");
    }

    [Test]
    public async Task AnAccentIsFoldedRatherThanPunched()
    {
        await Assert.That(GameSlug.Mint("Café Noir")).IsEqualTo("cafe-noir");
    }

    [Test]
    public async Task ANameThatLeavesNothingBehindDoesNotBecomeSomethingInvented()
    {
        await Assert.That(GameSlug.Mint("！？")).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ACollisionTakesANumericSuffix()
    {
        // Two unedited PennMUSHes really do want the same slug, and both are entitled to a URL.
        var taken = new HashSet<string>(StringComparer.Ordinal) { "corvid", "corvid-2" };

        var slug = await GameSlug.UniqueAsync(
            "Corvid", (candidate, _) => Task.FromResult(taken.Contains(candidate)));

        await Assert.That(slug).IsEqualTo("corvid-3");
    }

    [Test]
    public async Task ANamelessGameStillGetsAUrl()
    {
        var slug = await GameSlug.UniqueAsync("...", (_, _) => Task.FromResult(false));

        await Assert.That(slug).IsEqualTo("game");
    }

    [Test]
    public async Task ANameInAScriptTheFoldCannotKeepIsMintedFromTheAddressInstead()
    {
        // Both callers know an address — the one a probe answered at, or the URL the game already
        // has — and the word "game" is a URL only one game can hold, whatever it is called.
        var slug = await GameSlug.UniqueAsync(
            "엘리시안 전기", (_, _) => Task.FromResult(false), "110.10.160.150:4001");

        await Assert.That(slug).IsEqualTo("110-10-160-150-4001");
    }

    [Test]
    public async Task AnAddressThatFoldsToNothingEitherStillGetsAUrl()
    {
        var slug = await GameSlug.UniqueAsync("엘리시안 전기", (_, _) => Task.FromResult(false), "！？");

        await Assert.That(slug).IsEqualTo("game");
    }

    [Test]
    public async Task ASlugIsBounded()
    {
        var slug = GameSlug.Mint(new string('a', 500));

        await Assert.That(slug.Length).IsEqualTo(GameSlug.MaxLength);
    }

    [Test]
    public async Task TheLastResortReallyDoesTerminate()
    {
        // Regression: the fallback used to slice a fixed length off the GUID-appended result, which
        // threw for any stem shorter than 31 characters. A listing dying on ArgumentOutOfRangeException
        // is a worse answer to upstream collisions than an ugly URL.
        var slug = await GameSlug.UniqueAsync("Corvid", (_, _) => Task.FromResult(true));

        await Assert.That(slug).StartsWith("corvid-");
        await Assert.That(slug.Length).IsLessThanOrEqualTo(GameSlug.MaxLength);
    }

    [Test]
    public async Task TheLastResortIsStillBoundedForALongName()
    {
        var slug = await GameSlug.UniqueAsync(new string('a', 500), (_, _) => Task.FromResult(true));

        await Assert.That(slug.Length).IsEqualTo(GameSlug.MaxLength);
    }
}
