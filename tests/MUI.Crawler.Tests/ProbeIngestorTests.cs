using MUI.Catalog;
using MUI.Crawl;
using MUI.Crawler.Tests.Support;

namespace MUI.Crawler.Tests;

/// <summary>
/// The four rules of spec §5.4 and §6.5, at the seam that has to obey them.
/// </summary>
/// <remarks>
/// Each of these is a bug the design already reasoned about, so each is pinned by name. Collapsing
/// "probed but uncountable" into either neighbour is the worst one this codebase could ship; writing
/// a presence row for a failed probe is the second; and recording a decision of ours as a measurement
/// of theirs is the rule the other three are instances of.
/// </remarks>
public class ProbeIngestorTests
{
    [Test]
    public async Task AProbeThatAnsweredWithACountWritesThatCount()
    {
        var catalogue = new Catalogue();
        var game = catalogue.Listed();

        var ingestion = await catalogue.Ingestor().IngestAsync(
            game, Probes.Answered(who: new WhoReading(WhoConfidence.Count, 13)));

        var sample = catalogue.Presence.Samples.Single();

        await Assert.That(ingestion.Presence).IsEqualTo(PresenceOutcome.Counted);
        await Assert.That(sample.Count).IsEqualTo(13);
        await Assert.That(sample.Source).IsEqualTo(FieldSource.Who);
        await Assert.That(sample.Reason).IsNull();
    }

    [Test]
    public async Task AFailedProbeKeepsWhatTheDialActuallySaid()
    {
        // The cause vocabulary is six words wide and three of them are wastebaskets: "timeout"
        // carries a socket timeout, an exhausted probe budget and every error with no word of its
        // own. Four days of production had 157 dark episodes labelled "timeout" or
        // "handshake_stalled" and nothing else recorded anywhere, because the message was computed
        // in DialFailure and dropped on the way here — and the container keeps half an hour of logs.
        var catalogue = new Catalogue();
        var game = catalogue.Listed();

        await catalogue.Ingestor().IngestAsync(
            game, Probes.Failed(cause: "timeout", detail: "Network is unreachable [203.0.113.9]:4000"));

        var interval = catalogue.Availability.Intervals.Single();

        await Assert.That(interval.Cause).IsEqualTo(FailureCause.Timeout);
        await Assert.That(interval.Detail).IsEqualTo("Network is unreachable [203.0.113.9]:4000");
    }

    [Test]
    public async Task ARouteThatDoesNotExistIsNotATimeout()
    {
        // FailureReading maps every cause it does not recognise to Timeout, which is why "no_route"
        // had to be taught here as well as in DialFailure: teaching only the probe would have moved
        // the word one step and lost it in the same place.
        var catalogue = new Catalogue();
        var game = catalogue.Listed();

        await catalogue.Ingestor().IngestAsync(
            game, Probes.Failed(cause: "no_route", detail: "Network is unreachable"));

        var interval = catalogue.Availability.Intervals.Single();

        await Assert.That(interval.State).IsEqualTo(AvailabilityState.Unreachable);
        await Assert.That(interval.Cause).IsEqualTo(FailureCause.NoRoute);
    }

    [Test]
    public async Task ADetailThatChangedIsNotATransition()
    {
        // §5.3's invariant survives the new column. Two timeouts whose messages differ — a different
        // address in the text, say — are still one interval, or the table this exists for becomes a
        // sample series again.
        var catalogue = new Catalogue();
        var game = catalogue.Listed();
        var ingestor = catalogue.Ingestor();

        await ingestor.IngestAsync(game, Probes.Failed(cause: "timeout", detail: "first"));
        var second = await ingestor.IngestAsync(game, Probes.Failed(cause: "timeout", detail: "second"));

        await Assert.That(second.Availability).IsEqualTo(AvailabilityOutcome.Extended);
        await Assert.That(catalogue.Availability.Intervals.Single().Detail).IsEqualTo("first");
    }

    [Test]
    public async Task AMeasuredZeroIsAStoredCountAndNotAnAbsence()
    {
        // We got in and nobody was there. That is a real and useful fact about a game, and it is a
        // filled cell rather than a gap.
        var catalogue = new Catalogue();
        var game = catalogue.Listed();

        var ingestion = await catalogue.Ingestor().IngestAsync(
            game, Probes.Answered(who: new WhoReading(WhoConfidence.Count, 0)));

        await Assert.That(ingestion.Presence).IsEqualTo(PresenceOutcome.Counted);
        await Assert.That(catalogue.Presence.Samples.Single().Count).IsEqualTo(0);
        await Assert.That(catalogue.Presence.Samples.Single().IsCounted).IsTrue();
    }

    [Test]
    public async Task WhoOutranksMsspAndTheChoiceIsMadeBeforeTheWriter()
    {
        // presence_sample is keyed (game_id, at), so one probe writes at most one row and there is
        // nowhere to keep both and decide later. §5.2 says the ladder is applied by whatever assembles
        // the reading, and this is the assertion that it was.
        var catalogue = new Catalogue();
        var game = catalogue.Listed();

        await catalogue.Ingestor().IngestAsync(
            game,
            Probes.Answered(
                mssp: Probes.Mssp(("PLAYERS", "48")),
                who: new WhoReading(WhoConfidence.Count, 3)));

        var sample = catalogue.Presence.Samples.Single();

        await Assert.That(catalogue.Presence.Samples).Count().IsEqualTo(1);
        await Assert.That(sample.Count).IsEqualTo(3);
        await Assert.That(sample.Source).IsEqualTo(FieldSource.Who);
    }

    [Test]
    public async Task AnAnsweredProbeWithNoCountObtainableWritesANullCountAndAReasonAndNeverAZero()
    {
        // The worst bug this codebase could ship: a game whose DOING header is customised past our
        // parser rendering as permanently dark while running perfectly well.
        var catalogue = new Catalogue();
        var game = catalogue.Listed();

        var ingestion = await catalogue.Ingestor().IngestAsync(
            game, Probes.Answered(who: WhoReading.Unreadable));

        var sample = catalogue.Presence.Samples.Single();

        await Assert.That(ingestion.Presence).IsEqualTo(PresenceOutcome.RecordedUnmeasurable);
        await Assert.That(sample.Count).IsNull();
        await Assert.That(sample.Reason).IsEqualTo(UnmeasurableReason.WhoUnparseable);
    }

    [Test]
    public async Task NeverHavingAskedAndAskingAndFailingReachStorageAsDifferentReasons()
    {
        var catalogue = new Catalogue();
        var game = catalogue.Listed();
        var ingestor = catalogue.Ingestor();

        await ingestor.IngestAsync(game, Probes.Answered());
        await ingestor.IngestAsync(
            game, Probes.Answered(who: WhoReading.Unreadable, at: Probes.Observed.AddHours(1)));

        await Assert.That(catalogue.Presence.Samples.Select(s => s.Reason).ToList())
            .IsEquivalentTo(new List<UnmeasurableReason?>
            {
                UnmeasurableReason.WhoNotOffered,
                UnmeasurableReason.WhoUnparseable,
            });
    }

    [Test]
    public async Task AFailedProbeWritesAnAvailabilityTransitionAndNoPresenceRowAtAll()
    {
        var catalogue = new Catalogue();
        var game = catalogue.Listed();

        var ingestion = await catalogue.Ingestor().IngestAsync(game, Probes.Failed(cause: "refused"));

        var interval = catalogue.Availability.Intervals.Single();

        await Assert.That(ingestion.Presence).IsNull();
        await Assert.That(catalogue.Presence.Samples).IsEmpty();
        await Assert.That(interval.State).IsEqualTo(AvailabilityState.Unreachable);
        await Assert.That(interval.Cause).IsEqualTo(FailureCause.Refused);
    }

    [Test]
    public async Task AFailedProbeConfirmsNoFieldsBecauseItObservedNone()
    {
        // last_confirmed_at means "somebody saw this again". A dial that never got in saw nothing, and
        // bumping it would record our crawler having run as the game having answered.
        var catalogue = new Catalogue();
        var game = catalogue.Listed();
        var ingestor = catalogue.Ingestor();

        await ingestor.IngestAsync(game, Probes.Answered(mssp: Probes.Mssp(("GENRE", "Fantasy"))));

        var confirmed = catalogue.Fields.All.Single(f => f.Field == "GENRE").LastConfirmedAt;

        await ingestor.IngestAsync(game, Probes.Failed(at: Probes.Observed.AddDays(1)));

        await Assert.That(catalogue.Fields.All.Single(f => f.Field == "GENRE").LastConfirmedAt)
            .IsEqualTo(confirmed);
    }

    [Test]
    public async Task AHundredConsecutiveTimeoutsAreOneInterval()
    {
        var catalogue = new Catalogue();
        var game = catalogue.Listed();
        var ingestor = catalogue.Ingestor();

        for (var hour = 0; hour < 100; hour++)
        {
            await ingestor.IngestAsync(
                game, Probes.Failed(cause: "timeout", at: Probes.Observed.AddHours(hour)));
        }

        await Assert.That(catalogue.Availability.Intervals).Count().IsEqualTo(1);
    }

    [Test]
    public async Task ATimeoutAfterTheSessionEngagedIsDegradedRatherThanUnreachable()
    {
        // §5.3: degraded is "we got in and could not finish". It matters because it is not darkness —
        // the archive sweep must not credit our own probe timeout as the game's absence.
        var catalogue = new Catalogue();
        var game = catalogue.Listed();

        await catalogue.Ingestor().IngestAsync(
            game, Probes.Failed(cause: "timeout", offered: Probes.Offered("MSSP", "CHARSET")));

        var interval = catalogue.Availability.Intervals.Single();

        await Assert.That(interval.State).IsEqualTo(AvailabilityState.Degraded);
        await Assert.That(interval.Cause).IsEqualTo(FailureCause.HandshakeStalled);
    }

    [Test]
    public async Task ATimeoutWithNothingNegotiatedIsUnreachable()
    {
        var catalogue = new Catalogue();
        var game = catalogue.Listed();

        await catalogue.Ingestor().IngestAsync(game, Probes.Failed(cause: "timeout"));

        await Assert.That(catalogue.Availability.Intervals.Single().State)
            .IsEqualTo(AvailabilityState.Unreachable);
    }

    [Test]
    public async Task OneSuccessfulProbeTakesAGameOutOfTheArchive()
    {
        // §7.5: un-archiving is automatic and immediate, with no human on either side of it. This is
        // the property no incumbent directory managed.
        var catalogue = new Catalogue();
        var game = catalogue.Listed(LifecycleState.Archived);

        var ingestion = await catalogue.Ingestor().IngestAsync(game, Probes.Answered());

        await Assert.That(ingestion.Restored).IsTrue();
        await Assert.That(catalogue.Games.All.Single().State).IsEqualTo(LifecycleState.Active);
    }

    [Test]
    public async Task AFailedProbeNeverRestoresAnArchivedGame()
    {
        var catalogue = new Catalogue();
        var game = catalogue.Listed(LifecycleState.Archived);

        var ingestion = await catalogue.Ingestor().IngestAsync(game, Probes.Failed());

        await Assert.That(ingestion.Restored).IsFalse();
        await Assert.That(catalogue.Games.All.Single().State).IsEqualTo(LifecycleState.Archived);
    }

    [Test]
    public async Task AnAnsweredProbeMarksTheGameReachableBecauseTheBandAndTheGraceBothReadIt()
    {
        var catalogue = new Catalogue();
        var game = catalogue.Listed();

        await catalogue.Ingestor().IngestAsync(game, Probes.Answered());

        await Assert.That(catalogue.Games.All.Single().LastReachableAt).IsEqualTo(Probes.Observed);
    }

    [Test]
    public async Task AFailedProbeDoesNotMarkTheGameReachable()
    {
        var catalogue = new Catalogue();
        var game = catalogue.Listed();

        await catalogue.Ingestor().IngestAsync(game, Probes.Failed());

        await Assert.That(catalogue.Games.All.Single().LastReachableAt).IsNull();
    }

    [Test]
    public async Task AScopeRefusalCannotBeExpressedAsAProbeOutcome()
    {
        // §7.2's structural guarantee, and the reason "a refusal writes no availability sample" needs
        // no check anybody has to remember: there is no value of ProbeOutcome that means refused, so a
        // refusal cannot reach this type at all. Both members mean the socket was opened.
        var members = Enum.GetNames<ProbeOutcome>();

        await Assert.That(members).Count().IsEqualTo(2);
        await Assert.That(members).IsEquivalentTo(new[] { "Answered", "Failed" });
    }

    [Test]
    public async Task TheActivityBandNeverReadsAnUncountableGameAsQuiet()
    {
        // Parsers never fabricate and neither does the schedule: a game we could not count is not a
        // game with nobody on it, and reporting it as quiet would tighten the interval on evidence
        // that does not exist.
        var catalogue = new Catalogue();
        var game = catalogue.Listed();
        var ingestor = catalogue.Ingestor();

        var uncountable = await ingestor.IngestAsync(game, Probes.Answered(who: WhoReading.Unreadable));
        var empty = await ingestor.IngestAsync(
            game,
            Probes.Answered(who: new WhoReading(WhoConfidence.Count, 0), at: Probes.Observed.AddHours(1)));

        await Assert.That(uncountable.Activity).IsEqualTo(MUI.Discovery.ActivityBand.Unknown);
        await Assert.That(empty.Activity).IsEqualTo(MUI.Discovery.ActivityBand.Quiet);
    }

    [Test]
    public async Task AProbeThatConfirmsASettledNewNameMovesTheUrlAndKeepsTheOldOne()
    {
        // §5.7's re-mint is a consequence of the reconciliation this type has just performed, so it
        // happens here and strictly after it: the minter reads the winning NAME this probe confirmed.
        // The grace is a fortnight, and the two probes are a month apart.
        var catalogue = new Catalogue();
        var game = catalogue.Listed();
        var ingestor = catalogue.Ingestor(grace: TimeSpan.FromDays(14));

        await ingestor.IngestAsync(game, Probes.Answered(mssp: Probes.Mssp(("NAME", "Harbourlight"))));

        var settled = await ingestor.IngestAsync(
            game,
            Probes.Answered(
                mssp: Probes.Mssp(("NAME", "Harbourlight")), at: Probes.Observed.AddDays(30)));

        await Assert.That(settled.Renamed!.Slug).IsEqualTo("harbourlight");
        await Assert.That(settled.Renamed.FormerSlug).IsEqualTo("corvid");
        await Assert.That(await catalogue.Slugs.CurrentSlugAsync("corvid")).IsEqualTo("harbourlight");
    }

    [Test]
    public async Task ARenameThatCannotBeAppliedNeverCostsTheMeasurement()
    {
        // The mint asks whether a slug is taken and the write happens a moment later, so two games
        // settling on one name in the same cycle can lose that race — RenameAsync raises, and the
        // probe's reachability, its un-archiving and its schedule all went down with it. §7.5 then
        // archives a game that answers every probe, because the precondition persists and the same
        // target fails the same way for ever. A URL that could not be re-minted is cosmetic; a
        // measurement dropped is not.
        var catalogue = new Catalogue();
        var game = catalogue.Listed(LifecycleState.Archived);
        var ingestor = catalogue.Ingestor(grace: TimeSpan.FromDays(14), renamesFail: true);

        await ingestor.IngestAsync(game, Probes.Answered(mssp: Probes.Mssp(("NAME", "Harbourlight"))));

        // Dark again by the time the name has settled, so the probe that trips the rename is also
        // the one that has to un-archive it.
        await catalogue.Games.SetStateAsync(game, LifecycleState.Archived, Probes.Observed.AddDays(2));

        var settled = await ingestor.IngestAsync(
            game,
            Probes.Answered(
                mssp: Probes.Mssp(("NAME", "Harbourlight")), at: Probes.Observed.AddDays(30)));

        await Assert.That(settled.Renamed).IsNull();
        await Assert.That(settled.Restored).IsTrue();
        await Assert.That((await catalogue.Games.ByIdAsync(game))!.LastReachableAt)
            .IsEqualTo(Probes.Observed.AddDays(30));
        await Assert.That((await catalogue.Games.ByIdAsync(game))!.State)
            .IsEqualTo(LifecycleState.Active);
    }

    [Test]
    public async Task AFailedProbeRenamesNothing()
    {
        // A dial that never got in confirmed nothing, so it has no opinion about what a game is
        // called — and re-minting a URL off a name nobody just observed would be our crawler's
        // schedule recorded as the game's own history.
        var catalogue = new Catalogue();
        var game = catalogue.Listed();
        var ingestor = catalogue.Ingestor(grace: TimeSpan.FromDays(14));

        await ingestor.IngestAsync(game, Probes.Answered(mssp: Probes.Mssp(("NAME", "Harbourlight"))));

        var failed = await ingestor.IngestAsync(game, Probes.Failed(at: Probes.Observed.AddDays(30)));

        await Assert.That(failed.Renamed).IsNull();
        await Assert.That((await catalogue.Games.ByIdAsync(game))!.Slug).IsEqualTo("corvid");
    }
}
