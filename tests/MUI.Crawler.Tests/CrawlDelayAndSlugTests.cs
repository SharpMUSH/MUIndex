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
        // §7.7 resolves §7.4 against §11 in favour of politeness: the backoff is clamped to a week
        // first and the server's request applied afterwards, so a server asking for thirty days gets
        // thirty days. ProbeSchedule owns that; this is the pin that the value reaches it intact.
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
    public async Task ASlugIsBounded()
    {
        var slug = GameSlug.Mint(new string('a', 500));

        await Assert.That(slug.Length).IsEqualTo(GameSlug.MaxLength);
    }
}
