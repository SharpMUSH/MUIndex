using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawl;
using MUI.Crawler.Tests.Support;

namespace MUI.Crawler.Tests;

/// <summary>
/// Measured beside declared, each with its own source (spec §5.1, §6.1, §6.4).
/// </summary>
public class FieldObservationTests
{
    private static string? Value(IReadOnlyList<FieldObservation> observed, string field, FieldSource source) =>
        observed.FirstOrDefault(o => o.Field == field && o.Source == source)?.Value;

    [Test]
    public async Task AnObservedProtocolIsMeasuredAndAClaimedOneIsDeclared()
    {
        // The two columns of the capability matrix, from one probe, never contending for one row.
        var observed = FieldObservations.From(Probes.Answered(
            offered: Probes.Offered("MSSP", "CHARSET"),
            mssp: Probes.Mssp(("GMCP", "1"), ("MSSP", "1"))));

        await Assert.That(Value(observed, CapabilityFields.Measured("MSSP"), FieldSource.Handshake))
            .IsEqualTo("true");
        await Assert.That(Value(observed, CapabilityFields.Declared("GMCP"), FieldSource.Mssp))
            .IsEqualTo("true");

        // And the disagreement the site exists to publish: claimed, never observed.
        await Assert.That(Value(observed, CapabilityFields.Measured("GMCP"), FieldSource.Handshake))
            .IsNull();
    }

    /// <summary>
    /// An encoding nobody determined is not stored, in any form and under any source.
    /// </summary>
    /// <remarks>
    /// A screen that isn't UTF-8 and that nobody has explained is read with Latin-1 to keep its bytes
    /// whole — a way of holding the bytes, not a reading of them. Writing <c>iso-8859-1</c> down for
    /// it would produce a value where the honest answer is unknown (rule 4) signed with a source that
    /// claims we measured it (rule 5). The two determined cases carry the source of their
    /// determination: a strict decoder proved the bytes, or a person asserted them.
    /// </remarks>
    [Test]
    public async Task AnUndeterminedEncodingIsNotRecordedAsOne()
    {
        var undetermined = FieldObservations.From(Probes.Answered(banner: "…") with
        {
            ReadAs = "iso-8859-1",
            CharsetSource = WireCharset.Undetermined,
        });

        await Assert.That(undetermined.Any(o => o.Field == FieldObservations.CharsetReadField))
            .IsFalse();

        var proven = FieldObservations.From(Probes.Answered(banner: "…") with
        {
            ReadAs = "utf-8",
            CharsetSource = WireCharset.Proven,
        });

        await Assert.That(Value(proven, FieldObservations.CharsetReadField, FieldSource.Handshake))
            .IsEqualTo("utf-8");

        var overridden = FieldObservations.From(Probes.Answered(banner: "…") with
        {
            ReadAs = "gb2312",
            CharsetSource = WireCharset.Overridden,
        });

        await Assert.That(Value(overridden, FieldObservations.CharsetReadField, FieldSource.Staff))
            .IsEqualTo("gb2312");
        await Assert.That(Value(overridden, FieldObservations.CharsetReadField, FieldSource.Handshake))
            .IsNull();
    }

    [Test]
    public async Task TheNegotiatedOptionAndTheDeclaredVariableLandOnOneField()
    {
        // A server offers the telnet option, which is MCCP2, and its MSSP names the feature, which
        // is MCCP; MSSP's own variable is SSL where plenty of games write TLS. Stored under the
        // spellings they arrived in, each pair rendered as two rows on the dashboard that were
        // half-empty by construction — and the point of holding measured beside declared is that
        // they can be compared, which they cannot be across two rows.
        var observed = FieldObservations.From(Probes.Answered(
            offered: Probes.Offered("MSSP", "MCCP2"),
            mssp: Probes.Mssp(("MCCP", "1"), ("SSL", "4202"))));

        await Assert.That(Value(observed, CapabilityFields.Measured("MCCP"), FieldSource.Handshake))
            .IsEqualTo("true");
        await Assert.That(Value(observed, CapabilityFields.Declared("MCCP"), FieldSource.Mssp))
            .IsEqualTo("true");
        await Assert.That(Value(observed, CapabilityFields.Declared("TLS"), FieldSource.Mssp))
            .IsEqualTo("true");

        await Assert.That(observed.Where(o => o.Field.Contains("mccp2", StringComparison.Ordinal)))
            .IsEmpty();
        await Assert.That(observed.Where(o => o.Field == CapabilityFields.Declared("SSL")))
            .IsEmpty();

        // And the word the game used is not lost with the boolean: a value carrying more than a yes
        // still writes its own descriptive row, so the port they published survives verbatim.
        await Assert.That(Value(observed, "SSL", FieldSource.Mssp)).IsEqualTo("4202");
    }

    [Test]
    public async Task ACapabilityWeNeverAskedAboutIsNotRecordedAsAbsent()
    {
        // The probe only requests a subset of options and its enabled-callback can miss capabilities
        // the payload callbacks still see. Writing "false" here would publish our own instrumentation
        // gap as a fact about their game.
        var observed = FieldObservations.From(Probes.Answered(
            offered: Probes.Offered("MSSP"), mssp: Probes.Mssp()));

        var negatives = observed
            .Where(o => o.Value == "false" && o.Source == FieldSource.Handshake)
            .ToList();

        await Assert.That(negatives).IsEmpty();
    }

    [Test]
    public async Task TheOneMeasuredNegativeIsMsspBecauseMsspIsTheOneWeAskFor()
    {
        // IAC DO 70 goes out on every probe, so a server that answered the session and never engaged
        // MSSP declined a question that was put. Aardwolf is the worked example.
        var observed = FieldObservations.From(Probes.Answered());

        await Assert.That(Value(observed, CapabilityFields.Measured("MSSP"), FieldSource.Handshake))
            .IsEqualTo("false");
    }

    [Test]
    public async Task ADroppedOversizedReportIsNeverRecordedAsNoMssp()
    {
        // §6.4: we asked, the server answered, and we chose not to hold the reply. Publishing that as
        // an absence would state our own size limit as a fact about their game.
        var observed = FieldObservations.From(Probes.Answered(
            msspOutcome: MsspOutcome.RejectedTooLarge));

        await Assert.That(Value(observed, CapabilityFields.Measured("MSSP"), FieldSource.Handshake))
            .IsNotEqualTo("false");
    }

    [Test]
    public async Task ADroppedOversizedReportWithdrawsNothingTheGameHadAlreadyDeclared()
    {
        var observed = FieldObservations.From(Probes.Answered(
            mssp: Probes.Mssp(("GENRE", "Fantasy")), msspOutcome: MsspOutcome.RejectedTooLarge));

        await Assert.That(observed.Where(o => o.Source == FieldSource.Mssp)).IsEmpty();
    }

    [Test]
    public async Task ARepeatedVariableIsReducedByMsspsOwnRuleAndNeverByJoining()
    {
        // "The last reported value should be used as the default value." A join would manufacture
        // something that looks like a value and cannot be split back apart.
        var observed = FieldObservations.From(Probes.Answered(
            mssp: Probes.Mssp(("PORT", "23"), ("PORT", "3000"), ("PORT", "3010"))));

        await Assert.That(Value(observed, "PORT", FieldSource.Mssp)).IsEqualTo("3010");
    }

    [Test]
    public async Task ACapabilityCarryingAPortKeepsItsValueAndABareYesDoesNot()
    {
        // SSL 4202 is a claim and a port; GMCP 1 is only a claim, and listing it among a game's
        // descriptive fields would be the capability matrix restated as noise.
        var observed = FieldObservations.From(Probes.Answered(
            mssp: Probes.Mssp(("SSL", "4202"), ("GMCP", "1"))));

        await Assert.That(Value(observed, "SSL", FieldSource.Mssp)).IsEqualTo("4202");
        await Assert.That(Value(observed, CapabilityFields.Declared("TLS"), FieldSource.Mssp))
            .IsEqualTo("true");
        await Assert.That(Value(observed, "GMCP", FieldSource.Mssp)).IsNull();
    }

    [Test]
    public async Task AZeroIsReadAsADenialRatherThanAsAnAssertion()
    {
        var observed = FieldObservations.From(Probes.Answered(mssp: Probes.Mssp(("GMCP", "0"))));

        await Assert.That(Value(observed, CapabilityFields.Declared("GMCP"), FieldSource.Mssp))
            .IsEqualTo("false");
    }

    [Test]
    public async Task AVariableNoSpecificationListsIsStillRecorded()
    {
        // A protocol whose entire purpose is servers describing themselves does not get an allow-list,
        // and a field declined at the socket cannot be reconsidered downstream.
        var observed = FieldObservations.From(Probes.Answered(
            mssp: Probes.Mssp(("RP ENFORCEMENT", "strict"))));

        await Assert.That(Value(observed, "RP ENFORCEMENT", FieldSource.Mssp)).IsEqualTo("strict");
    }

    [Test]
    public async Task TheConnectScreenIsNotReconciledBecauseABannerIsNotAnEvent()
    {
        // A banner that states its own live player count would otherwise write a change-feed row on
        // every probe. It's upserted by CatalogueBinder instead, beside the banner fingerprint it's
        // the other half of.
        var observed = FieldObservations.From(Probes.Answered(banner: "Players Online: 206"));

        await Assert.That(observed.Select(o => o.Field))
            .DoesNotContain(CatalogueBinder.ConnectScreenField);
    }

    [Test]
    public async Task ANegotiatedCharsetIsAMeasurementAndAnInterpreterDefaultIsNot()
    {
        var negotiated = FieldObservations.From(Probes.Answered(
            negotiation: new Negotiation { Charset = "utf-8", CharsetNegotiated = true }));
        var defaulted = FieldObservations.From(Probes.Answered(
            negotiation: new Negotiation { Charset = "utf-8", CharsetNegotiated = false }));

        await Assert.That(Value(negotiated, FieldObservations.CharsetField, FieldSource.Handshake))
            .IsEqualTo("utf-8");
        await Assert.That(Value(negotiated, CapabilityFields.Measured("UTF-8"), FieldSource.Handshake))
            .IsEqualTo("true");
        await Assert.That(Value(defaulted, FieldObservations.CharsetField, FieldSource.Handshake))
            .IsNull();
    }

    [Test]
    public async Task AFailedProbeObservedNothingAndSaysSo()
    {
        await Assert.That(FieldObservations.From(Probes.Failed())).IsEmpty();
    }

    [Test]
    public async Task AVersionReplyNamesTheCodebaseOfAGameThatPublishesNoMssp()
    {
        // §6.2: a game that answers the socket but publishes no MSSP would otherwise have a page that
        // could say only that it's reachable. This is the sentence it can say instead.
        var observed = FieldObservations.From(Probes.Answered(
            version: "PennMUSH version 1.8.8p1"));

        await Assert.That(Value(observed, FieldObservations.CodebaseField, FieldSource.Banner))
            .IsEqualTo("PennMUSH version 1.8.8p1");
    }

    [Test]
    public async Task AParsedCodebaseAndADeclaredOneAreBothKeptAndDoNotContend()
    {
        // The reason game_field is keyed by source. The ladder puts mssp above banner, so the
        // declared value is what a page leads with — and "its own VERSION says otherwise" survives
        // as a row rather than being resolved away at write time.
        var observed = FieldObservations.From(Probes.Answered(
            mssp: Probes.Mssp(("CODEBASE", "Evennia")),
            version: "TinyMUX 2.12"));

        await Assert.That(Value(observed, FieldObservations.CodebaseField, FieldSource.Mssp))
            .IsEqualTo("Evennia");
        await Assert.That(Value(observed, FieldObservations.CodebaseField, FieldSource.Banner))
            .IsEqualTo("TinyMUX 2.12");
    }

    [Test]
    public async Task TextThatNamesNoCodebaseWritesNoCodebase()
    {
        // Rule 4. A server that answered INFO with its own house rules, or with nothing, has not
        // told us what it runs, and a parser that produced a value here would be inventing a fact
        // about somebody else's game.
        var quiet = FieldObservations.From(Probes.Answered(
            info: "Welcome. Type CREATE <name> <password> to begin.", version: "Yes"));
        var silent = FieldObservations.From(Probes.Answered());

        await Assert.That(Value(quiet, FieldObservations.CodebaseField, FieldSource.Banner)).IsNull();
        await Assert.That(Value(silent, FieldObservations.CodebaseField, FieldSource.Banner)).IsNull();
    }

    [Test]
    public async Task AGameThatCallsItselfAMuckIsAssumedToBeOne()
    {
        // Fuzzball offers no MSSP, answers no INFO and prints no labelled version, so every reading
        // above this one comes back empty and the page could name nothing at all. What it does have
        // is the word, on the screen it paints to anyone who connects.
        var observed = FieldObservations.From(Probes.Answered(
            host: "furry.muck.example.org",
            banner: "\e[1;35mWelcome to Tapestries MUCK!\e[0m"));

        await Assert.That(Value(observed, FieldObservations.CodebaseField, FieldSource.Banner))
            .IsEqualTo("MUCK");
    }

    [Test]
    public async Task ADeclaredCodebaseIsNeverGuessedOver()
    {
        // KitsuMUCK declares ProtoMUCK, and a game that answered the question is not asked it again:
        // our guess would lose to their report on the ladder anyway, and all it could do on the way
        // is publish a disagreement that only ever existed between them and us.
        var declared = FieldObservations.From(Probes.Answered(
            mssp: Probes.Mssp(("NAME", "KitsuMUCK"), ("CODEBASE", "ProtoMUCK")),
            banner: "kitsumuck.com : 8888"));

        // FAMILY is the same answer coarsened, and closes the same door.
        var family = FieldObservations.From(Probes.Answered(
            mssp: Probes.Mssp(("NAME", "Somewhere"), ("FAMILY", "TinyMUSH")),
            banner: "Welcome to Somewhere MUCK"));

        await Assert.That(Value(declared, FieldObservations.CodebaseField, FieldSource.Mssp))
            .IsEqualTo("ProtoMUCK");
        await Assert.That(Value(declared, FieldObservations.CodebaseField, FieldSource.Banner))
            .IsNull();
        await Assert.That(Value(family, FieldObservations.CodebaseField, FieldSource.Banner))
            .IsNull();
    }

    [Test]
    public async Task AReadableVersionOutranksTheAssumption()
    {
        // Pegasus prints "Currently running TinyMUCK2.3b2!" — which the reader can name in full.
        // The assumption is a floor under that, never a competitor for the same row.
        var observed = FieldObservations.From(Probes.Answered(
            banner: "Welcome to Pegasus, a MUCK",
            version: "TinyMUCK 2.3b2"));

        await Assert.That(Value(observed, FieldObservations.CodebaseField, FieldSource.Banner))
            .IsEqualTo("TinyMUCK 2.3b2");
        await Assert.That(observed.Count(o => o.Field == FieldObservations.CodebaseField))
            .IsEqualTo(1);
    }

    [Test]
    public async Task AMudWhoseArtSaysMuckIsStillAMud()
    {
        // A screen that jokes about "muck" in its title while its licence notice credits DikuMUD,
        // MERC and ROM must read as DikuMUD, not as a codebase invented from the joke word. What must
        // never happen is MUCK — the licence notice every Diku descendant carries is the real signal.
        var observed = FieldObservations.From(Probes.Answered(
            host: "mud.stick.org",
            banner: """
                "Don't get Stuck...  in da Muck"
                Original DikuMUD by Hans Staerfeldt, Katja Nyboe, Tom Madsen.
                """));

        await Assert.That(Value(observed, FieldObservations.CodebaseField, FieldSource.Banner))
            .IsEqualTo("DikuMUD");
        await Assert.That(observed.Count(o => o.Field == FieldObservations.CodebaseField))
            .IsEqualTo(1);
    }

    [Test]
    public async Task TheVolatileFieldsAreDroppedByTheReconcilerRatherThanHere()
    {
        // One home for that rule. PLAYERS is presence and UPTIME is a counter, and reconciling either
        // would write a change row per probe per game — but the projection does not acquire a second
        // copy of the list to keep in step.
        var observed = FieldObservations.From(Probes.Answered(
            mssp: Probes.Mssp(("PLAYERS", "13"), ("UPTIME", "1672885596"))));

        await Assert.That(observed.Select(o => o.Field))
            .Contains(f => FieldReconciler.VolatileFields.Contains(f));
    }
}
