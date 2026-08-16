using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// Spec §13: the matcher against known move events and deliberate near-collisions. Each case names the
/// real-world shape it stands for.
/// </summary>
/// <remarks>
/// This file tests the <em>judgement</em>, where <c>IdentityMatcherTests</c> tests the mechanism, and it
/// is separate because a reviewer can reasonably accept the mechanism and reject the calibration. Do not
/// move a weight to make one case pass without re-running every other case here.
/// </remarks>
public class IdentityCorpusTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly IdentityWorld _world = new();

    private IdentityMatcher Matcher => _world.Matcher;

    // ---- Known move events ----

    [Test]
    public async Task AGameThatChangedHostingProviderIsRecognised()
    {
        // The commonest real move: new host, new port, same everything else. §5.5 says a game that moves
        // must not become unfindable, and this is the mechanism that keeps it findable.
        const string banner = "  ---===  CORVID  ===---\r\n  A place for slow stories.\r\n";
        var corvid = await _world.GameAsync(
            (IdentityFields.Name, "Corvid"),
            (IdentityFields.Created, "2003"),
            (IdentityFields.BannerHash, BannerFingerprint.Of(banner)),
            (IdentityFields.Website, "https://corvid.example"),
            (IdentityFields.Codebase, "PennMUSH 1.8.8p2"));
        await _world.EndpointAsync(corvid, "old.hoster.example", 4201);

        var verdict = await Matcher.ResolveAsync(ProbeResults.Answered(
            host: "new.hoster.example", port: 6250,
            mssp: ProbeResults.Mssp(
                ("NAME", "Corvid"), ("CREATED", "2003"),
                ("WEBSITE", "https://corvid.example"), ("CODEBASE", "PennMUSH 1.8.8p2")),
            banner: banner), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Merge>();
        await Assert.That(((IdentityVerdict.Merge)verdict).GameId).IsEqualTo(corvid);
    }

    [Test]
    public async Task AGameThatMovedAndRedesignedItsLoginScreenIsStillRecognised()
    {
        // The banner signal is the one that goes away on a redesign — by design. NAME + CREATED +
        // WEBSITE is 1.00 exactly, which is what stops a redesign during a move minting a duplicate.
        var corvid = await _world.GameAsync(
            (IdentityFields.Name, "Corvid"),
            (IdentityFields.Created, "2003"),
            (IdentityFields.BannerHash, BannerFingerprint.Of("the old screen")),
            (IdentityFields.Website, "https://corvid.example"));
        await _world.EndpointAsync(corvid, "old.hoster.example", 4201);

        var verdict = await Matcher.ResolveAsync(ProbeResults.Answered(
            host: "new.hoster.example",
            mssp: ProbeResults.Mssp(
                ("NAME", "Corvid"), ("CREATED", "2003"), ("WEBSITE", "https://corvid.example")),
            banner: "An entirely new screen, with nothing of the old one left in it."), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Merge>();
        await Assert.That(((IdentityVerdict.Merge)verdict).GameId).IsEqualTo(corvid);
    }

    [Test]
    public async Task AGameThatMovedWithNothingButItsBannerOpensAReviewRatherThanMerging()
    {
        // A game with no MSSP at all that moved host. 0.50 is a real signal and not a proof, so both
        // pages stay live and a person decides.
        const string banner = "Welcome to the Rookery.\nSeven towers, and a long memory.";
        await _world.GameAsync((IdentityFields.BannerHash, BannerFingerprint.Of(banner)));

        var verdict = await Matcher.ResolveAsync(
            ProbeResults.Answered(host: "moved.example.org", banner: banner), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Review>();
    }

    // ---- Deliberate near-collisions ----

    [Test]
    public async Task TwoGamesOnOneHostWithDifferentPortsStaySeparate()
    {
        // Shared hosting, and a common shape for a hobby community running several games on one box.
        // The endpoint signal is keyed on (host, port) precisely so this does not collapse.
        var first = await _world.GameAsync(
            (IdentityFields.Name, "Corvid"), (IdentityFields.Created, "2003"),
            (IdentityFields.Codebase, "PennMUSH 1.8.8p2"));
        await _world.EndpointAsync(first, "shared.example.org", 4201);

        var verdict = await Matcher.ResolveAsync(ProbeResults.Answered(
            host: "shared.example.org", port: 4202,
            mssp: ProbeResults.Mssp(
                ("NAME", "Magpie"), ("CREATED", "2019"), ("CODEBASE", "PennMUSH 1.8.8p2"))), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
        await Assert.That(((IdentityVerdict.Fresh)verdict).Best?.Score ?? 0d)
            .IsLessThan(IdentityWeights.ReviewThreshold);
    }

    [Test]
    public async Task TwoGamesCalledFantasyMudWithNothingElseInCommonStaySeparate()
    {
        // The generic-name collision. NAME alone must score nothing, or every "Fantasy MUD", "The Realm"
        // and "Midnight Sun" in the hobby folds into one page.
        await _world.GameAsync(
            (IdentityFields.Name, "Fantasy MUD"),
            (IdentityFields.Created, "1997"),
            (IdentityFields.Website, "https://one.example"));

        var verdict = await Matcher.ResolveAsync(ProbeResults.Answered(
            host: "other.example.org",
            mssp: ProbeResults.Mssp(
                ("NAME", "Fantasy MUD"), ("CREATED", "2021"), ("WEBSITE", "https://two.example"))), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
    }

    [Test]
    public async Task TwoGamesSharingOnlyACodebaseStaySeparate()
    {
        // Nearly every MUSH in the catalogue will report the same CODEBASE string. 0.15 alone must be
        // far below the review bar, or the review queue becomes the whole catalogue.
        await _world.GameAsync((IdentityFields.Codebase, "PennMUSH 1.8.8p2"));

        var verdict = await Matcher.ResolveAsync(ProbeResults.Answered(
            host: "other.example.org",
            mssp: ProbeResults.Mssp(("NAME", "Magpie"), ("CODEBASE", "PennMUSH 1.8.8p2"))), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
    }

    [Test]
    public async Task TwoGamesSharingOneAdminsContactAddressOnlyReachReview()
    {
        // One person running two games, with the same CONTACT on both. Worth a look; never an automatic
        // merge, because folding a person's two games together is a visible, embarrassing error.
        await _world.GameAsync((IdentityFields.Contact, "admin@example.org"));

        var verdict = await Matcher.ResolveAsync(ProbeResults.Answered(
            host: "other.example.org",
            mssp: ProbeResults.Mssp(("NAME", "Magpie"), ("CONTACT", "admin@example.org"))), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Review>();
    }

    // ---- Placeholders: the failure that would be silent and unrecoverable ----

    [Test]
    public async Task TwoUneditedPennMushesDoNotMatchEachOther()
    {
        // Observed on a live server: an unedited PennMUSH publishes NAME "PennMUSH", and so does every
        // other unedited PennMUSH on the internet. Scored naively they all match each other on the
        // strongest textual signal in §7.3's table, and auto-merge fuses unrelated games into one
        // listing — silently, and afterwards indistinguishably from a merge that should have happened.
        //
        // A placeholder contributes nothing, not a little.
        await _world.GameAsync(
            (IdentityFields.Name, "PennMUSH"),
            (IdentityFields.Created, "2020"),
            (IdentityFields.Codebase, "PennMUSH 1.8.8p2"));

        var verdict = await Matcher.ResolveAsync(ProbeResults.Answered(
            host: "unrelated.example.org",
            mssp: ProbeResults.Mssp(
                ("NAME", "PennMUSH"), ("CREATED", "2020"), ("CODEBASE", "PennMUSH 1.8.8p2"))), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
    }

    [Test]
    public async Task ANameThatMerelyRestatesTheCodebaseWithAVersionIsAlsoNotAName()
    {
        // NAME "PennMUSH 1.8.8p0" is the same non-answer as NAME "PennMUSH", and the pair travels
        // together often enough to matter.
        await _world.GameAsync(
            (IdentityFields.Name, "PennMUSH 1.8.8p0"),
            (IdentityFields.Created, "1999"));

        var verdict = await Matcher.ResolveAsync(ProbeResults.Answered(
            host: "unrelated.example.org",
            mssp: ProbeResults.Mssp(
                ("NAME", "PennMUSH 1.8.8p0"), ("CREATED", "1999"), ("CODEBASE", "PennMUSH 1.8.8p0"))), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
    }

    [Test]
    [Arguments("Unknown")]
    [Arguments("N/A")]
    [Arguments("Change Me")]
    [Arguments("Your MUD Name")]
    [Arguments("   ")]
    public async Task TemplateTextIsAnAbsenceOnBothSidesOfTheComparison(string placeholder)
    {
        // Two absences must never score as an agreement — and the stored side is filtered as well as
        // the observed one, because an import or an older write may have persisted a placeholder.
        await _world.GameAsync(
            (IdentityFields.Name, placeholder),
            (IdentityFields.Created, "2003"),
            (IdentityFields.Website, placeholder),
            (IdentityFields.Contact, placeholder));

        var verdict = await Matcher.ResolveAsync(ProbeResults.Answered(
            host: "unrelated.example.org",
            mssp: ProbeResults.Mssp(
                ("NAME", placeholder), ("CREATED", "2003"),
                ("WEBSITE", placeholder), ("CONTACT", placeholder))), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
        await Assert.That(((IdentityVerdict.Fresh)verdict).Best?.Score ?? 0d).IsEqualTo(0d);
    }

    [Test]
    public async Task TwoSilentConnectScreensAreNotTheSameGame()
    {
        // Plenty of servers send nothing before the first prompt. The hash of an empty screen is a
        // perfectly stable value they would all share, so an unfingerprintable banner is an absence and
        // not a signal — the same rule as a placeholder, one layer down.
        await _world.GameAsync((IdentityFields.BannerHash, BannerFingerprint.Of(string.Empty)));

        var verdict = await Matcher.ResolveAsync(
            ProbeResults.Answered(host: "unrelated.example.org", banner: "  \r\n  "), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
    }

    [Test]
    public async Task AStockColourPromptIsNotAConnectScreen()
    {
        // The same rule as the silent screen, one notch up, and it is not hypothetical: three unrelated
        // ROM games in the live catalogue — Adventures Unlimited, Pendulum's Calm and StockMUD — send
        // nothing before the first prompt but "Do you want ANSI? (Y/n)", hash identically, and had two
        // duplicate reviews open between them at 0.50, the same score as a genuine twin.
        //
        // A screen this short is the codebase talking, not the game. Measured against the live
        // catalogue: every connect screen under 40 flattened characters is a capability-negotiation
        // prompt, and the shortest one that names its game is 42.
        const string prompt = "Do you want ANSI? (Y/n) ";
        await _world.GameAsync((IdentityFields.BannerHash, BannerFingerprint.Of(prompt)));

        var verdict = await Matcher.ResolveAsync(
            ProbeResults.Answered(host: "unrelated.example.org", banner: prompt), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
    }

    [Test]
    public async Task AShortScreenThatNamesTheGameIsStillASignal()
    {
        // The floor must not be so high it eats the real ones. Minstrel Hall's whole screen is 42
        // characters and it says who it is, which is exactly what the banner signal is for.
        const string banner = "Welcome to Minstrel Hall! Enter your name:";
        var hall = await _world.GameAsync((IdentityFields.BannerHash, BannerFingerprint.Of(banner)));

        var verdict = await Matcher.ResolveAsync(
            ProbeResults.Answered(host: "unrelated.example.org", banner: banner), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Review>();
        await Assert.That(((IdentityVerdict.Review)verdict).GameId).IsEqualTo(hall);
    }

    [Test]
    public async Task APlaceholderNameDoesNotEvenGatherACandidate()
    {
        // Not just unweighted: a placeholder must not put a game into the candidate set at all, or every
        // probe of an unedited server would score itself against every other one in the catalogue.
        await _world.GameAsync((IdentityFields.Name, "PennMUSH"));

        var verdict = await Matcher.ResolveAsync(ProbeResults.Answered(
            host: "unrelated.example.org", mssp: ProbeResults.Mssp(("NAME", "PennMUSH"))), None);

        await Assert.That(((IdentityVerdict.Fresh)verdict).Best).IsNull();
    }

    // ---- The claim token ----

    [Test]
    public async Task TheWireSpellingsAreWhatWeTellOperatorsToType()
    {
        // The one place in this suite where the literal is the subject rather than a dependency. These
        // strings are a published contract with server operators: they appear in the claim instructions
        // an owner is given and in this reader, and if either side edits one the other stops seeing a
        // token that is sitting right there in the MSSP report. Changing a value here is changing what
        // every already-claimed game has typed into its config, so it is a migration, not an edit.
        await Assert.That(ClaimTokenBeacon.MsspVariable).IsEqualTo("MUINDEX CLAIM");
        await Assert.That(ClaimTokenBeacon.ConnectScreenPrefix).IsEqualTo("MUINDEX-CLAIM:");
        await Assert.That(ClaimTokenBeacon.DnsLabel).IsEqualTo("_muindex");

        // The canonical name must be first, because Read returns the first accepted variable it finds
        // and a game setting both should be read as having set the canonical one.
        await Assert.That(ClaimTokenBeacon.AcceptedMsspVariables[0]).IsEqualTo(ClaimTokenBeacon.MsspVariable);
    }

    [Test]
    public async Task AClaimedGameIsNeverDuplicated()
    {
        // §7.3: "decisive when present — a claimed game is never duplicated". Everything else disagrees
        // here: different host, different name, no shared field at all.
        var corvid = await _world.GameAsync((IdentityFields.ClaimToken, "7f3a91c4e2"));

        var verdict = await Matcher.ResolveAsync(ProbeResults.Answered(
            host: "somewhere.else.example", port: 9999,
            mssp: ProbeResults.Mssp(
                ("NAME", "Totally Different"),
                (ClaimTokenBeacon.MsspVariable, "7f3a91c4e2"))), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Merge>();
        await Assert.That(((IdentityVerdict.Merge)verdict).GameId).IsEqualTo(corvid);
    }

    [Test]
    public async Task AClaimTokenOnTheConnectScreenIsJustAsDecisive()
    {
        // §8's second channel. A server operator who cannot edit MSSP can always edit the login screen —
        // and it is read through the same flattening the fingerprint uses, so colour does not hide it.
        var corvid = await _world.GameAsync((IdentityFields.ClaimToken, "7f3a91c4e2"));

        var verdict = await Matcher.ResolveAsync(ProbeResults.Answered(
            host: "somewhere.else.example",
            banner: $"Welcome!\r\n\e[1;36m{ClaimTokenBeacon.ConnectScreenPrefix} 7f3a91c4e2\e[0m\r\nType 'connect'.\r\n"), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Merge>();
        await Assert.That(((IdentityVerdict.Merge)verdict).GameId).IsEqualTo(corvid);
    }

    [Test]
    public async Task AnOperatorWhoUsedAnAliasIsStillRecognised()
    {
        // Someone who did exactly what they were told, through a variable name their config file spells
        // differently, must not be informed their claim failed.
        var alias = await _world.GameAsync((IdentityFields.ClaimToken, "7f3a91c4e2"));

        var verdict = await Matcher.ResolveAsync(ProbeResults.Answered(
            host: "somewhere.else.example",
            mssp: ProbeResults.Mssp(("NAME", "Totally Different"), ("CONTACT_TOKEN", "7f3a91c4e2"))), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Merge>();
        await Assert.That(((IdentityVerdict.Merge)verdict).GameId).IsEqualTo(alias);
    }

    [Test]
    public async Task AWrongClaimTokenIsNotASignalAtAll()
    {
        // Weight 10.0 means a token that matched the wrong game would be unrecoverable, so the match is
        // exact and a near miss contributes nothing.
        await _world.GameAsync((IdentityFields.ClaimToken, "7f3a91c4e2"), (IdentityFields.Name, "Corvid"));

        var verdict = await Matcher.ResolveAsync(ProbeResults.Answered(
            host: "somewhere.else.example",
            mssp: ProbeResults.Mssp(("NAME", "Corvid"), (ClaimTokenBeacon.MsspVariable, "7f3a91c4e3"))), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
    }
}
