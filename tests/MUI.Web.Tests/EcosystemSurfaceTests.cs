using MUI.Catalog;
using MUI.Web.Components;
using MUI.Web.Fixtures;
using MUI.Web.Localization;

namespace MUI.Web.Tests;

/// <summary>
/// The two aggregate surfaces, asserted in words.
/// </summary>
/// <remarks>What the page must <em>refuse</em> to say: no absolute player figure, no share without its denominator, no snapshot described as a trend.</remarks>
public class EcosystemSurfaceTests
{
    private static readonly DateTimeOffset Now = FixtureGameQueries.Now;
    private static readonly FixtureGameQueries Queries = new();

    private static async Task<string> EcosystemAsync() =>
        PlainText.RenderEcosystem(await Queries.EcosystemAsync(), Now);

    private static async Task<string> RankingsAsync() =>
        PlainText.RenderRankings(await Queries.RankingsAsync(), Now);

    /// <summary>
    /// One message, as the source locale renders it.
    /// </summary>
    /// <remarks>Reads the claim out of the bundle, not the English literal, so wording stays free to be translated while a page that stopped saying it still fails here.</remarks>
    private static string Say(string id, params (string Key, object? Value)[] args) =>
        Messages.For("en", id, args.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal));

    [Test]
    public async Task NoAbsolutePlayerFigureIsEmittedByEitherSurface()
    {
        // §15.7: the absolute headcount is withheld because it can't survive the unclaimed/unreachable
        // biases a ratio can. Pinned via arithmetic so a future total fails here, not in review.
        var listed = await Queries.ListAsync(new GameFilter());
        var total = listed.Sum(g => g.PlayersNow ?? 0).ToString();
        var text = await EcosystemAsync() + await RankingsAsync();

        await Assert.That(total).IsNotEqualTo("0");
        await Assert.That(text).DoesNotContain(total);
        await Assert.That(text).DoesNotContain("players in total");
        await Assert.That(text).DoesNotContain("total players");
        await Assert.That(text).DoesNotContain("across all games");

        // And it says out loud that the omission is deliberate rather than an oversight.
        await Assert.That(Render.Words(text)).Contains(Say("ecosystem.noTotals"));
    }

    [Test]
    public async Task EveryPercentageOnTheDashboardArrivesWithItsDenominator()
    {
        // A percentage without its denominator isn't a fact.
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
        // Measured and declared are counted over different sets of games; one shared denominator
        // would compare the reachable against the talkative and call the difference adoption.
        var text = Render.Words(await EcosystemAsync());

        var dashboard = await Queries.EcosystemAsync();

        await Assert.That(text).Contains(EcosystemCopy.Handshakes("en", dashboard.Handshakes));
        await Assert.That(text).Contains(EcosystemCopy.MsspReports("en", dashboard.MsspReports));
        await Assert.That(text).Contains(Say(
            "ecosystem.plain.denominators",
            ("measured", EcosystemCopy.Handshakes("en", dashboard.Handshakes)),
            ("declared", EcosystemCopy.MsspReports("en", dashboard.MsspReports))));
    }

    [Test]
    public async Task AProtocolWithNoMeasurementSaysSoRatherThanShowingNoughtPerCent()
    {
        // Nothing observed to offer this is a fact about our reach; "0.0%" would claim it about their servers.
        var never = new ProtocolAdoption("TLS", Offered: null, Declined: 0, Handshakes: 400, Declared: 12, MsspReports: 300);

        await Assert.That(EcosystemCopy.Measured("en", never))
            .IsEqualTo(Say("ecosystem.measured.never"));
        await Assert.That(EcosystemCopy.Measured("en", never)).DoesNotContain("0.0%");
        await Assert.That(EcosystemCopy.Measured("en", never)).DoesNotContain("0 of 400");

        // Declared is unaffected and still carries its own set.
        await Assert.That(EcosystemCopy.Declared("en", never)).IsEqualTo(
            Say("ecosystem.share", ("count", 12), ("total", 300), ("fraction", 12d / 300)));
        await Assert.That(EcosystemCopy.Declared("en", never)).IsEqualTo("12 of 300 (4.0%)");
    }

    [Test]
    public async Task AnEmptyDenominatorIsNothingMeasuredAndNotNoughtPerCent()
    {
        // 0 of 0 is not 0%, in the same way CapabilityState.Unknown is not Absent.
        var nothing = new MeasuredShare("GMCP", 0, 0);

        await Assert.That(nothing.Fraction).IsNull();
        await Assert.That(EcosystemCopy.Share("en", nothing))
            .IsEqualTo(Say("ecosystem.share.nothing", ("count", 0), ("total", 0)));
        await Assert.That(EcosystemCopy.Share("en", nothing)).DoesNotContain("%");
    }

    [Test]
    public async Task TheMeasuredColumnIsSaidToBeAFloor()
    {
        // Printing the measured column without saying it's a floor publishes our own instrumentation
        // limits as a fact about the game.
        var text = Render.Words(await EcosystemAsync());

        await Assert.That(text).Contains(Say("ecosystem.protocols.floor"));
    }

    [Test]
    public async Task TheDashboardCallsItselfASnapshotAndNotATrend()
    {
        // §9 asks for adoption curves; the store can't honestly draw one yet, so it says so rather
        // than plotting first-sightings, which would just show the crawl's own reach.
        var text = Render.Words(await EcosystemAsync());

        await Assert.That(text).Contains(Say("ecosystem.snapshot"));
        await Assert.That(text).Contains(Say("ecosystem.transitions.none"));
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
        await Assert.That(text).Contains(Say(
            "ecosystem.codebases.basis",
            ("listed", dashboard.Codebases.Identified + dashboard.Codebases.NotIdentified),
            ("identified", dashboard.Codebases.Identified)));
    }

    [Test]
    public async Task TheCodebasePanelNamesTheListingBesideTheGamesThatAnswered()
    {
        // The identified count once sat alone and read as the size of the whole catalogue; both
        // numbers must be in the sentence, unambiguous.
        var dashboard = await Queries.EcosystemAsync();
        var text = Render.Words(await EcosystemAsync());

        var listed = dashboard.Codebases.Identified + dashboard.Codebases.NotIdentified;

        await Assert.That(listed).IsEqualTo(dashboard.ListedGames);
        await Assert.That(dashboard.Codebases.Identified).IsNotEqualTo(listed);
        // Both numbers in one sentence, and each unmistakable for the other.
        await Assert.That(text).Contains(Say(
            "ecosystem.codebases.basis",
            ("listed", listed),
            ("identified", dashboard.Codebases.Identified)));
        await Assert.That(text).Contains($"{listed} games listed");
        await Assert.That(text).Contains($"over those {dashboard.Codebases.Identified}");
    }

    /// <summary>The one-game codebases are folded out of the chart and listed under it.</summary>
    /// <remarks>A share of one is a name, not a share. Folded rather than dropped: the surface still prints every one, so the panel's arithmetic stays checkable.</remarks>
    [Test]
    public async Task ACodebaseOnlyOneGameRunsIsFoldedOutOfTheChartAndStillPrinted()
    {
        var codebases = (await Queries.EcosystemAsync()).Codebases;
        var text = Render.Words(await EcosystemAsync());

        await Assert.That(codebases.Shared).IsNotEmpty();
        await Assert.That(codebases.SoleUse).IsNotEmpty();

        await Assert.That(text).Contains(
            $"{codebases.SoleUseTotal.Count} of {codebases.Identified}");
        await Assert.That(text).Contains(
            Say("ecosystem.soleUse", ("share", EcosystemCopy.Share("en", codebases.SoleUseTotal))));

        foreach (var alone in codebases.SoleUse)
        {
            await Assert.That(text).Contains(alone.Label);
        }
    }

    [Test]
    public async Task TheMsspRowStaysBecauseItIsTheOnlyMeasurementThatIsNotAFloor()
    {
        // The only row with a real negative beside it: we request MSSP by name, so every other
        // measured figure is an undercount and this one is an answer.
        var dashboard = await Queries.EcosystemAsync();
        var text = Render.Words(await EcosystemAsync());

        var mssp = dashboard.Protocols.Single(p => p.Protocol == "MSSP");

        await Assert.That(mssp.Measured).IsNotNull();
        await Assert.That(mssp.Declined).IsGreaterThan(0);
        await Assert.That(text).Contains("MSSP");
        await Assert.That(text).Contains(
            Say("ecosystem.mssp.instrument", ("instrument", EcosystemProtocols.Instrument)));
    }

    [Test]
    public async Task OnlyMsspHasNoDeclaredFigureAndTheReasonIsItsDenominator()
    {
        // Every game whose report we hold has proved MSSP support by sending one, so there's no
        // population left for a share to be of. A protocol nobody declared is still 0% — absence of
        // a claim is a claim — so MSSP is the one blank.
        var dashboard = await Queries.EcosystemAsync();
        var text = Render.Words(await EcosystemAsync());

        var blank = dashboard.Protocols.Where(p => p.DeclaredShare is null).Select(p => p.Protocol);

        await Assert.That(blank).IsEquivalentTo(new[] { "MSSP" });
        await Assert.That(EcosystemCopy.Declared("en", dashboard.Protocols.Single(p => p.Protocol == "MSSP")))
            .DoesNotContain("%");
        await Assert.That(text).Contains(Say("ecosystem.declared.none"));
    }

    [Test]
    public async Task TwoCountsOfMsspAreReconciledRatherThanLeftToSubtract()
    {
        // We hold more reports than games offering MSSP today (a report isn't discarded when a game
        // stops reissuing it) — left unexplained, that reads as an arithmetic error.
        var dashboard = await Queries.EcosystemAsync();

        await Assert.That(dashboard.Mssp).IsNotNull();

        var withGap = new ProtocolAdoption("MSSP", Offered: 123, Declined: 295, Handshakes: 418,
            Declared: 1, MsspReports: 131);

        await Assert.That(EcosystemCopy.MsspBasis("en", withGap, 131)).Contains(
            Say("ecosystem.mssp.gap", ("reports", 131), ("offered", 123), ("gap", 8)));

        // And no gap means no sentence about one, rather than "the other 0".
        var level = withGap with { Offered = 131 };

        await Assert.That(EcosystemCopy.MsspBasis("en", level, 131))
            .IsEqualTo(Say("ecosystem.mssp.instrument", ("instrument", EcosystemProtocols.Instrument)));
    }

    [Test]
    public async Task TheRankingsStateWhatTheyRankOnAndOverWhatWindow()
    {
        var text = Render.Words(await RankingsAsync());

        await Assert.That(text).Contains(Say("rankings.basis.median", ("days", 7)));

        // The threshold as a sentence, not arithmetic the reader has to do themselves.
        await Assert.That(text).Contains("24 samples across 4 days");

        // Rule 4 on the surface that most invites a zero to be read as an absence.
        await Assert.That(text).Contains(Say("rankings.basis.zero"));
    }

    [Test]
    public async Task TheRankingsOfferNoVoteAndClaimNoBest()
    {
        // §2's permanent non-goal.
        var text = Render.Words(await RankingsAsync());

        await Assert.That(text).Contains(Say("rankings.noVote"));
        await Assert.That(text.ToLowerInvariant()).DoesNotContain("rate this");
        await Assert.That(text.ToLowerInvariant()).DoesNotContain("top rated");
    }

    [Test]
    public async Task AMeasuredZeroIsRankedRatherThanDroppedFromTheTable()
    {
        // A measured zero across the week is a strong measurement; omitting it softens a fact into a shrug.
        var rankings = await Queries.RankingsAsync();
        var lowest = rankings.Busiest[^1];

        await Assert.That(lowest.Slug).IsEqualTo("eldertale");
        await Assert.That(lowest.Median).IsEqualTo(0);
        await Assert.That(lowest.Samples).IsGreaterThan(0);
    }

    [Test]
    public async Task AGameThatCannotBeCountedIsNotRankedAtZero()
    {
        // Answers but offers nothing countable: absent from the busiest table, present in the
        // reachability one — two different measurements, only one failed.
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

        // Counted from the fixture, not a literal, so adding a demo game doesn't spuriously fail this.
        var catalogue = await Queries.ListAsync(new GameFilter { IncludeArchived = true });

        await Assert.That(slugs).IsNotEmpty();
        await Assert.That(rankings.Busiest.Any(g => slugs.Contains(g.Slug))).IsFalse();
        await Assert.That(rankings.LongestUnbroken.Any(s => slugs.Contains(s.Slug))).IsFalse();
        await Assert.That(dashboard.ListedGames).IsEqualTo(catalogue.Count - slugs.Count);
    }

    [Test]
    public async Task NothingOnEitherSurfaceExceedsEightyColumns()
    {
        // A text browser is eighty columns wide.
        foreach (var line in (await EcosystemAsync() + await RankingsAsync()).Split('\n'))
        {
            // The line itself in the message, so a failure doesn't require hunting for which one grew.
            await Assert.That(line.TrimEnd().Length)
                .IsLessThanOrEqualTo(PlainText.Columns)
                .Because($"too wide: {line.TrimEnd()}");
        }
    }
}
