using MUI.Catalog;
using MUI.Web.Components;
using MUI.Web.Fixtures;

namespace MUI.Web.Tests;

/// <summary>
/// The two aggregate surfaces, asserted in words.
/// </summary>
/// <remarks>
/// A dashboard is where a site's honesty is cheapest to lose, because a percentage is quotable and a
/// qualification is not. The three assertions worth having are all about what the page must
/// <em>refuse</em> to say: it may not publish an absolute player figure, it may not print a share
/// without the set it is a share of, and it may not describe a snapshot as a trend.
/// </remarks>
public class EcosystemSurfaceTests
{
    private static readonly DateTimeOffset Now = FixtureGameQueries.Now;
    private static readonly FixtureGameQueries Queries = new();

    private static async Task<string> EcosystemAsync() =>
        PlainText.RenderEcosystem(await Queries.EcosystemAsync(), Now);

    private static async Task<string> RankingsAsync() =>
        PlainText.RenderRankings(await Queries.RankingsAsync(), Now);

    [Test]
    public async Task NoAbsolutePlayerFigureIsEmittedByEitherSurface()
    {
        // §15.7, and the "Never" list. The absolute "how many people play MU*" number is withheld
        // because a ratio over the measured set survives the unclaimed and unreachable biases and a
        // headcount survives neither. The pin is the arithmetic itself: sum the fixture's measured
        // counts and require that the number never appears, so a future "total players on right now"
        // fails here rather than in review.
        var listed = await Queries.ListAsync(new GameFilter());
        var total = listed.Sum(g => g.PlayersNow ?? 0).ToString();
        var text = await EcosystemAsync() + await RankingsAsync();

        await Assert.That(total).IsNotEqualTo("0");
        await Assert.That(text).DoesNotContain(total);
        await Assert.That(text).DoesNotContain("players in total");
        await Assert.That(text).DoesNotContain("total players");
        await Assert.That(text).DoesNotContain("across all games");

        // And it says out loud that the omission is deliberate rather than an oversight.
        await Assert.That(Render.Words(text)).Contains("Shares, never totals");
    }

    [Test]
    public async Task EveryPercentageOnTheDashboardArrivesWithItsDenominator()
    {
        // "62% of games offer UTF-8" is not a fact until "of the 431 games whose handshake we have
        // completed" is attached to it. The count and the set come first on every line, and the
        // percentage second, so a line carrying one and not the other is the defect.
        var lines = (await EcosystemAsync())
            .Split('\n')
            .Where(line => line.Contains('%', StringComparison.Ordinal))
            .ToList();

        await Assert.That(lines).IsNotEmpty();

        foreach (var line in lines)
        {
            await Assert.That(line).Contains(" of ");
        }
    }

    [Test]
    public async Task BothDenominatorsAreNamedRatherThanImplied()
    {
        // Measured and declared are counted over two different sets of games, and a page that named
        // one denominator for both would be comparing a share of the reachable against a share of the
        // talkative and calling the difference adoption.
        var text = Render.Words(await EcosystemAsync());

        await Assert.That(text).Contains("whose handshake we completed");
        await Assert.That(text).Contains("whose MSSP report we hold");
        await Assert.That(text).Contains("Two sets of games, so two denominators");
    }

    [Test]
    public async Task AProtocolWithNoMeasurementSaysSoRatherThanShowingNoughtPerCent()
    {
        // The one place a true number would be a false statement. Nothing has been observed to offer
        // this, which is a fact about our reach; "0.0%" is a claim about everybody else's servers.
        var never = new ProtocolAdoption("TLS", Offered: null, Declined: 0, Handshakes: 400, Declared: 12, MsspReports: 300);

        await Assert.That(EcosystemCopy.Measured(never)).Contains("not measured");
        await Assert.That(EcosystemCopy.Measured(never)).DoesNotContain("0.0%");
        await Assert.That(EcosystemCopy.Measured(never)).DoesNotContain("0 of 400");

        // The declared side is unaffected and still carries its own set.
        await Assert.That(EcosystemCopy.Declared(never)).IsEqualTo("12 of 300 (4.0%)");
    }

    [Test]
    public async Task AnEmptyDenominatorIsNothingMeasuredAndNotNoughtPerCent()
    {
        // 0 of 0 is not 0%, in the same way CapabilityState.Unknown is not Absent.
        var nothing = new MeasuredShare("GMCP", 0, 0);

        await Assert.That(nothing.Fraction).IsNull();
        await Assert.That(EcosystemCopy.Share(nothing)).Contains("nothing measured yet");
        await Assert.That(EcosystemCopy.Share(nothing)).DoesNotContain("%");
    }

    [Test]
    public async Task TheMeasuredColumnIsSaidToBeAFloor()
    {
        // The crawler writes a capability down when it observes one and otherwise writes nothing,
        // because it requests MSSP alone and declines MCCP outright. Printing that column without
        // saying so publishes our own instrumentation as a fact about somebody's game.
        var text = Render.Words(await EcosystemAsync());

        await Assert.That(text).Contains("may support a protocol without ever offering it");
        await Assert.That(text).Contains("as a floor");
    }

    [Test]
    public async Task TheDashboardCallsItselfASnapshotAndNotATrend()
    {
        // §9 asks for adoption curves and the store cannot honestly draw one yet. Saying so is better
        // than a plot of first sightings, which would draw a confident rising line measuring the
        // crawl reaching more games and nothing about anybody adopting anything.
        var text = Render.Words(await EcosystemAsync());

        await Assert.That(text).Contains("A snapshot of what we can measure now");
        await Assert.That(text).Contains("nothing to plot");
        await Assert.That(text).DoesNotContain("growth");
    }

    [Test]
    public async Task AGameWithNoCodebaseIsOutsideTheDenominatorAndSaidToBe()
    {
        var dashboard = await Queries.EcosystemAsync();
        var text = Render.Words(await EcosystemAsync());

        await Assert.That(dashboard.Codebases.NotIdentified).IsGreaterThan(0);
        await Assert.That(dashboard.Codebases.Families.Sum(f => f.Count))
            .IsEqualTo(dashboard.Codebases.Identified);
        await Assert.That(text).Contains("left out of the denominator, never counted as something else");
    }

    [Test]
    public async Task TheCodebasePanelNamesTheListingBesideTheGamesThatAnswered()
    {
        // The identified count sat alone in this sentence — "Share of the 144 listed games that told
        // us what they run" — and read as the size of the catalogue, which was 418. A denominator
        // that can be mistaken for the set it was drawn from is the failure this page exists to
        // argue against, so both numbers are in the sentence and each is unambiguous.
        var dashboard = await Queries.EcosystemAsync();
        var text = Render.Words(await EcosystemAsync());

        var listed = dashboard.Codebases.Identified + dashboard.Codebases.NotIdentified;

        await Assert.That(listed).IsEqualTo(dashboard.ListedGames);
        await Assert.That(dashboard.Codebases.Identified).IsNotEqualTo(listed);
        await Assert.That(text).Contains($"Of the {listed} games listed, {dashboard.Codebases.Identified} told us what they run");
        await Assert.That(text).Contains($"every share below is over those {dashboard.Codebases.Identified}");
    }

    [Test]
    public async Task TheMsspRowStaysBecauseItIsTheOnlyMeasurementThatIsNotAFloor()
    {
        // Not all servers support MSSP, and which ones do is one of the more useful things this page
        // knows — it bounds what anybody can learn about the rest. It is also the only row with a
        // real negative beside it: we request MSSP by name and nothing else, so every other measured
        // figure is an undercount and this one is an answer. Holding it out as "the instrument"
        // deleted the strongest row on the table.
        var dashboard = await Queries.EcosystemAsync();
        var text = Render.Words(await EcosystemAsync());

        var mssp = dashboard.Protocols.Single(p => p.Protocol == "MSSP");

        await Assert.That(mssp.Measured).IsNotNull();
        await Assert.That(mssp.Declined).IsGreaterThan(0);
        await Assert.That(text).Contains("MSSP");
        await Assert.That(text).Contains("is the one row below that is not a floor");
    }

    [Test]
    public async Task OnlyMsspHasNoDeclaredFigureAndTheReasonIsItsDenominator()
    {
        // Every game whose report we hold has proved it supports MSSP by sending one, so there is no
        // population left over for a share to be of: the 1 of 131 that rendered counted the games
        // that also listed MSSP inside their own MSSP report, which measures a habit. A protocol
        // nobody declared is still 0% — absence of a claim is a claim — so the blank is this case
        // alone.
        var dashboard = await Queries.EcosystemAsync();
        var text = Render.Words(await EcosystemAsync());

        var blank = dashboard.Protocols.Where(p => p.DeclaredShare is null).Select(p => p.Protocol);

        await Assert.That(blank).IsEquivalentTo(new[] { "MSSP" });
        await Assert.That(EcosystemCopy.Declared(dashboard.Protocols.Single(p => p.Protocol == "MSSP")))
            .DoesNotContain("%");
        await Assert.That(text).Contains("every report here is the answer");
    }

    [Test]
    public async Task TwoCountsOfMsspAreReconciledRatherThanLeftToSubtract()
    {
        // We hold more reports than there are games offering MSSP today, because a report is not
        // discarded when a game stops reissuing it. Left unexplained the two numbers read as an
        // arithmetic error on a page whose whole argument is that its arithmetic can be checked.
        var dashboard = await Queries.EcosystemAsync();

        await Assert.That(dashboard.Mssp).IsNotNull();

        var withGap = new ProtocolAdoption("MSSP", Offered: 123, Declined: 295, Handshakes: 418,
            Declared: 1, MsspReports: 131);

        await Assert.That(EcosystemCopy.MsspBasis(withGap, 131)).Contains("We hold 131 reports");
        await Assert.That(EcosystemCopy.MsspBasis(withGap, 131)).Contains("the other 8");

        // And no gap means no sentence about one, rather than "the other 0".
        var level = withGap with { Offered = 131 };

        await Assert.That(EcosystemCopy.MsspBasis(level, 131)).DoesNotContain("the other");
    }

    [Test]
    public async Task TheRankingsStateWhatTheyRankOnAndOverWhatWindow()
    {
        var text = Render.Words(await RankingsAsync());

        await Assert.That(text).Contains("Median of the player counts we measured over the last 7 days");
        await Assert.That(text).Contains("counted samples a median needs");
        await Assert.That(text).Contains("A measured zero counts; an unreadable count does not");
    }

    [Test]
    public async Task TheRankingsOfferNoVoteAndClaimNoBest()
    {
        // §2's permanent non-goal, said on the surface a reader would look for it on.
        var text = Render.Words(await RankingsAsync());

        await Assert.That(text).Contains("No votes, stars or ratings, ever");
        await Assert.That(text.ToLowerInvariant()).DoesNotContain("rate this");
        await Assert.That(text.ToLowerInvariant()).DoesNotContain("top rated");
    }

    [Test]
    public async Task AMeasuredZeroIsRankedRatherThanDroppedFromTheTable()
    {
        // Eldertale sits at a measured zero across the week. That is a measurement and a strong one,
        // and a league table that quietly omitted it would be softening a fact into a shrug.
        var rankings = await Queries.RankingsAsync();
        var lowest = rankings.Busiest[^1];

        await Assert.That(lowest.Slug).IsEqualTo("eldertale");
        await Assert.That(lowest.Median).IsEqualTo(0);
        await Assert.That(lowest.Samples).IsGreaterThan(0);
    }

    [Test]
    public async Task AGameThatCannotBeCountedIsNotRankedAtZero()
    {
        // Midnight Sun answers and offers nothing we can count. It has no median, so it is absent
        // from the busiest table — and it is present in the reachability one, because those are two
        // different measurements and only one of them failed.
        var rankings = await Queries.RankingsAsync();

        await Assert.That(rankings.Busiest.Any(g => g.Slug == "midnight-sun")).IsFalse();
        await Assert.That(rankings.LongestUnbroken.Any(s => s.Slug == "midnight-sun")).IsTrue();
    }

    [Test]
    public async Task AnArchivedGameIsInNeitherTable()
    {
        var rankings = await Queries.RankingsAsync();
        var dashboard = await Queries.EcosystemAsync();
        var archived = await Queries.ListAsync(new GameFilter { Band = ActivityBand.Archived });
        var slugs = archived.Select(g => g.Slug).ToHashSet(StringComparer.Ordinal);

        await Assert.That(slugs).IsNotEmpty();
        await Assert.That(rankings.Busiest.Any(g => slugs.Contains(g.Slug))).IsFalse();
        await Assert.That(rankings.LongestUnbroken.Any(s => slugs.Contains(s.Slug))).IsFalse();
        await Assert.That(dashboard.ListedGames).IsEqualTo(8 - slugs.Count);
    }

    [Test]
    public async Task NothingOnEitherSurfaceExceedsEightyColumns()
    {
        // The plain surface is the test of the whole system, and a text browser is eighty wide.
        foreach (var line in (await EcosystemAsync() + await RankingsAsync()).Split('\n'))
        {
            await Assert.That(line.TrimEnd().Length).IsLessThanOrEqualTo(PlainText.Columns);
        }
    }
}
