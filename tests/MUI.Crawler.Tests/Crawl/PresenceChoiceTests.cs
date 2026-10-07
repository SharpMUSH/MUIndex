using MUI.Catalog;
using MUI.Crawl;
using MUI.Crawler.Tests.Support;

namespace MUI.Crawler.Tests;

/// <summary>
/// Which of a probe's possible counts becomes the one presence row (spec §5.2's ladder).
/// </summary>
public class PresenceChoiceTests
{
    [Test]
    public async Task WhoBeatsMsspBecauseItIsLiveRatherThanCached()
    {
        var reading = PresenceChoice.From(Probes.Answered(
            mssp: Probes.Mssp(("PLAYERS", "48")),
            who: new WhoReading(WhoConfidence.Count, 3)));

        await Assert.That(reading.Count).IsEqualTo(3);
        await Assert.That(reading.Source).IsEqualTo(FieldSource.Who);
    }

    [Test]
    public async Task AZeroFromWhoStillBeatsANonZeroFromMssp()
    {
        // The uncomfortable case, and the one the ladder is for. A measured zero is a measurement; an
        // MSSP PLAYERS is whatever the codebase last cached. Preferring the larger number would be
        // preferring the more flattering one.
        var reading = PresenceChoice.From(Probes.Answered(
            mssp: Probes.Mssp(("PLAYERS", "48")),
            who: new WhoReading(WhoConfidence.Count, 0)));

        await Assert.That(reading.Count).IsEqualTo(0);
        await Assert.That(reading.Source).IsEqualTo(FieldSource.Who);
    }

    [Test]
    public async Task MsspIsReadWhenWhoCouldNotBe()
    {
        var reading = PresenceChoice.From(Probes.Answered(
            mssp: Probes.Mssp(("PLAYERS", "48")), who: WhoReading.Unreadable));

        await Assert.That(reading.Count).IsEqualTo(48);
        await Assert.That(reading.Source).IsEqualTo(FieldSource.Mssp);
    }

    /// <summary>
    /// PennMUSH's <c>dump_info()</c> in shape, for a game that offers no MSSP and whose <c>DOING</c>
    /// header our WHO parser cannot read — which is the whole population this rung exists for.
    /// </summary>
    private const string PennInfo = """
        ### Begin INFO 1.1
        Name: Convergence MUSH
        Address: game.convergencemush.org
        Uptime: Tue Sep 16 23:39:43 2025
        Connected: 60
        Size: 1929
        Version: RhostMUSH 4.27.3
        ### End INFO
        """;

    [Test]
    public async Task TheInfoBlockIsReadWhenNeitherWhoNorMsspCould()
    {
        // The defect this rung closes: the count was already on the probe result, parsed off the
        // wire, and the ladder had nowhere to put it — so the game was written down unmeasurable.
        var reading = PresenceChoice.From(Probes.Answered(info: PennInfo, who: WhoReading.Unreadable));

        await Assert.That(reading.Count).IsEqualTo(60);
        await Assert.That(reading.Source).IsEqualTo(FieldSource.Info);
    }

    [Test]
    public async Task AnInfoCountIsDeclaredRatherThanMeasured()
    {
        // Same class as MSSP and against the same temptation. We open the socket, but what comes back
        // is a labelled line the codebase generated about itself — a report, not a reading.
        await Assert.That(FieldSources.IsMeasured(FieldSource.Info)).IsFalse();
        await Assert.That(FieldSources.IsMeasured(FieldSource.Mssp)).IsFalse();
        await Assert.That(FieldSources.IsMeasured(FieldSource.Banner)).IsTrue();
    }

    [Test]
    public async Task MsspStillWinsSoNoGameThatMeasuresTodayChangesItsRecordedValue()
    {
        // The reason the rung is third rather than second. Both figures come out of PennMUSH's one
        // count_players() call, so there is nothing to choose between them on accuracy — and a rung
        // added *below* an existing one cannot restate the provenance of a count already published.
        var reading = PresenceChoice.From(Probes.Answered(
            mssp: Probes.Mssp(("PLAYERS", "48")), info: PennInfo, who: WhoReading.Unreadable));

        await Assert.That(reading.Count).IsEqualTo(48);
        await Assert.That(reading.Source).IsEqualTo(FieldSource.Mssp);
    }

    [Test]
    public async Task AReadableWhoStillOutranksTheInfoBlock()
    {
        var reading = PresenceChoice.From(Probes.Answered(
            info: PennInfo, who: new WhoReading(WhoConfidence.Count, 3)));

        await Assert.That(reading.Count).IsEqualTo(3);
        await Assert.That(reading.Source).IsEqualTo(FieldSource.Who);
    }

    [Test]
    public async Task TheInfoBlockOutranksTheConnectScreen()
    {
        // A generated `Connected:` line beats a number found in ASCII art, which may be a high score
        // or last week's figure in a static file.
        var reading = PresenceChoice.From(Probes.Answered(
            banner: "Players Currently Online: 215", info: PennInfo, who: WhoReading.Unreadable));

        await Assert.That(reading.Count).IsEqualTo(60);
        await Assert.That(reading.Source).IsEqualTo(FieldSource.Info);
    }

    [Test]
    public async Task AConnectScreenSayingConnectedIsNotAnInfoBlock()
    {
        // The delimiters are the whole defence. Without them "Connected:" on a connect screen is a
        // word about the reader, and the banner rung — which is bounded by its own rules — is where
        // a connect screen belongs.
        var reading = PresenceChoice.From(Probes.Answered(
            info: "Connected: 41\nType 'connect <name> <password>' to play.",
            who: WhoReading.Unreadable));

        await Assert.That(reading.Count).IsNull();
        await Assert.That(reading.Reason).IsEqualTo(UnmeasurableReason.WhoUnparseable);
    }

    [Test]
    public async Task AnInfoBlockWeCouldNotReadStillNamesTheWhoProblem()
    {
        // There is no fourth unmeasurable reason and there should not be one: a game that printed no
        // readable INFO has not done anything a maintainer can act on, while "we asked WHO and could
        // not read the answer" is a fact about our parser meeting their dialect.
        var reading = PresenceChoice.From(Probes.Answered(
            info: "### Begin INFO 1.1\nName: Some MUSH\nSize: 1929\n### End INFO",
            who: WhoReading.Unreadable));

        await Assert.That(reading.Count).IsNull();
        await Assert.That(reading.Reason).IsEqualTo(UnmeasurableReason.WhoUnparseable);
    }

    [Test]
    public async Task AnInfoConnectedZeroIsAMeasuredZeroAndNotAFallThrough()
    {
        // The uncomfortable case one rung down. A stated zero is a filled cell, so it must not be
        // allowed to look like "no reading" and let the connect screen answer instead.
        var reading = PresenceChoice.From(Probes.Answered(
            banner: "Players Currently Online: 215",
            info: "### Begin INFO 1.1\nName: Quiet MUSH\nConnected: 0\n### End INFO",
            who: WhoReading.Unreadable));

        await Assert.That(reading.Count).IsEqualTo(0);
        await Assert.That(reading.Source).IsEqualTo(FieldSource.Info);
    }

    [Test]
    public async Task TheConnectScreenIsReadLastAndOnlyWhenBothOthersFailed()
    {
        // Aardwolf: no MSSP, no pre-login WHO, and "Players Currently Online: 215" on the screen it
        // hands every anonymous connection. The only rung that reaches it, and last for a reason.
        var reading = PresenceChoice.From(Probes.Answered(
            banner: "Players Currently Online: 215", who: WhoReading.Unreadable));

        await Assert.That(reading.Count).IsEqualTo(215);
        await Assert.That(reading.Source).IsEqualTo(FieldSource.Banner);
    }

    [Test]
    public async Task ANonNumericMsspPlayersIsSaidOutLoudRatherThanTreatedAsAbsent()
    {
        var reading = PresenceChoice.From(Probes.Answered(
            mssp: Probes.Mssp(("PLAYERS", "lots")), who: WhoReading.Unreadable));

        await Assert.That(reading.Count).IsNull();
        await Assert.That(reading.Reason).IsEqualTo(UnmeasurableReason.PlayersNotNumeric);
    }

    [Test]
    public async Task AnUnreadableWhoIsUncountableAndNeverZero()
    {
        var reading = PresenceChoice.From(Probes.Answered(who: WhoReading.Unreadable));

        await Assert.That(reading.Count).IsNull();
        await Assert.That(reading.Reason).IsEqualTo(UnmeasurableReason.WhoUnparseable);
    }

    [Test]
    public async Task AGameThatWasNeverAskedIsADifferentReasonFromOneWeCouldNotRead()
    {
        var reading = PresenceChoice.From(Probes.Answered());

        await Assert.That(reading.Reason).IsEqualTo(UnmeasurableReason.WhoNotOffered);
    }

    /// <summary>
    /// The fourth reason, and the one that is not about us.
    /// </summary>
    /// <remarks>
    /// <c>who_unparseable</c> says our parser met a dialect it couldn't read — a defect with an owner
    /// and a fix — and this says the game has no pre-login <c>WHO</c> at all: the word went in as a
    /// character name and <c>Illegal name, try again.</c> came back.
    /// </remarks>
    [Test]
    public async Task AGameWhoseLoginPromptAteTheWordIsNotAParserWeOwe()
    {
        var reading = PresenceChoice.From(Probes.Answered(who: WhoReading.LoginPrompt));

        await Assert.That(reading.Count).IsNull();
        await Assert.That(reading.Reason).IsEqualTo(UnmeasurableReason.WhoLoginPrompt);
    }

    [Test]
    public async Task ADeclaredCountStillOutranksTheLoginPromptReason()
    {
        // The reason is only ever reached when no rung produced a number, and the ladder is
        // unchanged: a game that ate the word and publishes MSSP PLAYERS is counted, not hatched.
        var reading = PresenceChoice.From(Probes.Answered(
            mssp: Probes.Mssp(("PLAYERS", "48")), who: WhoReading.LoginPrompt));

        await Assert.That(reading.Count).IsEqualTo(48);
    }

    [Test]
    public async Task ANegativeMsspPlayersIsRefusedRatherThanStored()
    {
        // The schema refuses it too (presence_sample_count_is_not_negative), and a reading that got
        // that far would fail at the database with no way back. Refuse where the value is read.
        var reading = PresenceChoice.From(Probes.Answered(
            mssp: Probes.Mssp(("PLAYERS", "-4")), who: WhoReading.Unreadable));

        await Assert.That(reading.Count).IsNull();
        await Assert.That(reading.Reason).IsEqualTo(UnmeasurableReason.PlayersNotNumeric);
    }

    /// <summary>
    /// A roster the report published is counted where nothing better exists, under its own source.
    /// </summary>
    /// <remarks>
    /// The shape <c>dead-souls.net:8000</c> sends — one <c>WHO</c> occurrence per player — with the
    /// stated count taken away. Its own source and not <c>mssp</c>, because it is a floor rather than
    /// a total: <c>tdome.nukefire.org:4000</c> states seventy and names sixty-nine.
    /// </remarks>
    [Test]
    public async Task ARosterIsCountedUnderItsOwnSourceWhereNothingBetterExists()
    {
        var reading = PresenceChoice.From(Probes.Answered(
            mssp: Probes.Mssp(("WHO", "Ninja"), ("WHO", "Cratylus"), ("WHO", "Joshua")),
            who: WhoReading.NotAsked));

        await Assert.That(reading.Count).IsEqualTo(3);
        await Assert.That(reading.Source).IsEqualTo(FieldSource.MsspRoster);
    }

    [Test]
    public async Task AnEmptyRosterIsAMeasuredZeroAndNotAHatchedCell()
    {
        // Vithasnir and Xanth Mud both send WHO with no value beside PLAYERS = 0. Reading the empty
        // occurrence as a name would report one player on every quiet Dead Souls game there is.
        var reading = PresenceChoice.From(Probes.Answered(
            mssp: Probes.Mssp(("WHO", string.Empty)), who: WhoReading.NotAsked));

        await Assert.That(reading.Count).IsEqualTo(0);
        await Assert.That(reading.Source).IsEqualTo(FieldSource.MsspRoster);
    }

    [Test]
    public async Task ARosterNeverRelabelsACountAlreadyPublishedUnderMssp()
    {
        // Migration 0019's rule, which every new rung is held to: it may fill a row that would
        // otherwise be NULL and may never move an existing one. A game publishing both is counted
        // under `mssp`, at the number it stated, not at the length of its list.
        var reading = PresenceChoice.From(Probes.Answered(
            mssp: Probes.Mssp(("PLAYERS", "70"), ("PLAYERNAMES", "Ninja, Cratylus, Joshua")),
            who: WhoReading.NotAsked));

        await Assert.That(reading.Count).IsEqualTo(70);
        await Assert.That(reading.Source).IsEqualTo(FieldSource.Mssp);
    }

    [Test]
    public async Task TheInfoBlockStillOutranksARoster()
    {
        // A count the game stated about itself beats one we arrived at by counting its list, whichever
        // door the statement came through.
        var reading = PresenceChoice.From(Probes.Answered(
            mssp: Probes.Mssp(("PLAYERNAMES", "Ninja, Cratylus, Joshua")),
            info: "### Begin INFO 1.1\nName: Elendor\nConnected: 11\n### End INFO",
            who: WhoReading.NotAsked));

        await Assert.That(reading.Count).IsEqualTo(11);
        await Assert.That(reading.Source).IsEqualTo(FieldSource.Info);
    }

    [Test]
    public async Task ARosterOutranksANumberFoundInAsciiArt()
    {
        var reading = PresenceChoice.From(Probes.Answered(
            mssp: Probes.Mssp(("PLAYERNAMES", "Ninja, Cratylus, Joshua")),
            banner: "Welcome!\nPlayers Currently Online: 12\n",
            who: WhoReading.NotAsked));

        await Assert.That(reading.Count).IsEqualTo(3);
        await Assert.That(reading.Source).IsEqualTo(FieldSource.MsspRoster);
    }

    [Test]
    public async Task ARosterIsDeclaredRatherThanMeasured()
    {
        // Where this parts company with i3, whose arithmetic is also ours: an I3 who-reply is a list
        // built because we asked, over a socket, now. A roster arrived unsolicited inside the game's
        // own self-description.
        await Assert.That(FieldSources.IsMeasured(FieldSource.MsspRoster)).IsFalse();
        await Assert.That(FieldSources.IsMeasured(FieldSource.I3)).IsTrue();
    }

    [Test]
    public async Task AFailedProbeHasNoReadingAtAll()
    {
        // §5.4's third state is the absence of a row, not a value in one. Asking for a reading is a
        // caller mistake and is refused rather than answered with something plausible.
        await Assert.That(() => PresenceChoice.From(Probes.Failed())).Throws<ArgumentException>();
    }
}
