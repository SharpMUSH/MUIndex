using MUI.Catalog.Persistence;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// Spec §5.6: "old" is not one duration. The two anchors the spec argues from — a player count stale
/// in hours, a hand-typed <c>GENRE</c> unremarkable at six months and notable at six years — are what
/// these pin. If a window moves past one of them, this file is what says so.
/// </summary>
public class FieldRegistryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static readonly IFieldRegistry Registry = FieldRegistry.Instance;

    [Test]
    public async Task APlayerCountIsStaleInHours()
    {
        await Assert.That(Registry.IsStale("PLAYERS", Now.AddHours(-3), Now)).IsTrue();
    }

    [Test]
    public async Task AHandTypedGenreIsUnremarkableAtSixMonths()
    {
        await Assert.That(Registry.IsStale("GENRE", Now.AddDays(-183), Now)).IsFalse();
    }

    [Test]
    public async Task AHandTypedGenreIsNotableAtSixYears()
    {
        await Assert.That(Registry.IsStale("GENRE", Now.AddDays(-2192), Now)).IsTrue();
    }

    [Test]
    public async Task AMeasuredCapabilityGoesStaleFasterThanADeclaredOne()
    {
        // A measured capability unconfirmed for a day is a fact about our crawler, not the game.
        var measured = Registry.Find(CapabilityFields.Measured("GMCP"))!.ExpectedRefresh;
        var declared = Registry.Find(CapabilityFields.Declared("GMCP"))!.ExpectedRefresh;

        await Assert.That(measured).IsLessThan(declared);
    }

    [Test]
    public async Task TheEnrichmentFieldsAreTheOnesMsspCannotExpress()
    {
        // Spec §3.2 names exactly these as absent from MSSP.
        var enrichment = FieldRegistry.All
            .Where(f => f.OwnerWritable is OwnerWritable.Enrichment)
            .Select(f => f.Name)
            .ToList();

        await Assert.That(enrichment).Contains("FANDOM");
        await Assert.That(enrichment).Contains("APPLICATION PROCESS");
        await Assert.That(enrichment).Contains("RP ENFORCEMENT");
        await Assert.That(enrichment).Contains("CONSENT TOOLS");
    }

    [Test]
    public async Task TheOverridableFieldsAreTheHandTypedHalfOfMssp()
    {
        // Spec §8.5: an MSSP report is not a measurement, so a verified owner may answer it — but only
        // what a person types into mush.cnf, nothing the codebase fills in for them.
        var overridable = FieldRegistry.All
            .Where(f => f.OwnerWritable is OwnerWritable.Override)
            .Select(f => f.Name)
            .ToList();

        await Assert.That(overridable).IsEquivalentTo(new[]
        {
            "NAME", "GENRE", "SUBGENRE", "GAMEPLAY", "GAMESYSTEM", "DESCRIPTION", "STATUS",
            "WEBSITE", "CONTACT", "DISCORD", "ICON", "LANGUAGE", "LOCATION", "MINIMUM AGE",
            "CREATED", "INTERMUD",
        });
    }

    [Test]
    public async Task TheConnectionDescribingFieldsAreNobodysToAssert()
    {
        // HOSTNAME, PORT, IP and CODEBASE describe the connection rather than the game and are
        // auto-filled, so a hand-typed answer would mean something different under one name.
        // capability.*.declared is excluded because the matrix's job is to show what a game claims
        // beside what its handshake offered — an owner editing "claimed" edits half that comparison.
        var writable = Writable();

        foreach (var machinery in new[]
                 {
                     "HOSTNAME", "PORT", "IP", "IPV6", "CODEBASE", "FAMILY", "CHARSET", "CRAWL DELAY",
                     CapabilityFields.Declared("GMCP"),
                 })
        {
            await Assert.That(writable).DoesNotContain(machinery);
        }
    }

    [Test]
    public async Task NothingAProbeMeasuredIsWritableByAnybody()
    {
        // §8.5's line, asserted rather than trusted to the layout of a table. A successful write to
        // any of these would make the whole site a self-report with extra steps.
        var writable = Writable();

        await Assert.That(writable).DoesNotContain("PLAYERS");
        await Assert.That(writable).DoesNotContain("UPTIME");
        await Assert.That(writable).DoesNotContain(InternalFields.BannerHash);
        await Assert.That(writable).DoesNotContain(InternalFields.ConnectScreen);
        await Assert.That(writable.Where(CapabilityFields.IsMeasured)).IsEmpty();
    }

    private static List<string> Writable() => FieldRegistry.All
        .Where(f => f.OwnerWritable is not OwnerWritable.No)
        .Select(f => f.Name)
        .ToList();

    [Test]
    public async Task TheRequiredMsspTrioIsDeclared()
    {
        var names = FieldRegistry.All.Select(f => f.Name).ToList();

        await Assert.That(names).Contains("NAME");
        await Assert.That(names).Contains("PLAYERS");
        await Assert.That(names).Contains("UPTIME");
    }

    [Test]
    public async Task AFieldNobodyDeclaredIsNeverStaleRatherThanGuessedAt()
    {
        // The registry is not a gate on ingestion. We store the value but decline to judge an age we
        // have no window for — inventing one would put a fabricated fact on a public page.
        await Assert.That(Registry.Find("SOME UNOFFICIAL THING")).IsNull();
        await Assert.That(Registry.IsStale("SOME UNOFFICIAL THING", Now.AddYears(-40), Now)).IsFalse();
    }

    [Test]
    public async Task NoFieldIsDeclaredTwice()
    {
        var duplicates = FieldRegistry.All
            .GroupBy(f => f.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        await Assert.That(duplicates).IsEmpty();
    }

    [Test]
    public async Task ACapabilityFieldNameIsNamespacedAndSaysWhichSideItCameFrom()
    {
        await Assert.That(CapabilityFields.Measured("XTERM 256 COLORS"))
            .IsEqualTo("capability.xterm-256-colors.measured");
        await Assert.That(CapabilityFields.Declared("GMCP")).IsEqualTo("capability.gmcp.declared");
    }

    [Test]
    public async Task ACapabilityFieldNameReadsBackToItsCapability()
    {
        // The matrix has to put a row it read back into the right column, and it must not need a
        // second convention to do it.
        await Assert.That(CapabilityFields.CapabilityOf("capability.gmcp.measured")).IsEqualTo("GMCP");
        await Assert.That(CapabilityFields.CapabilityOf("capability.xterm-256-colors.declared"))
            .IsEqualTo("XTERM 256 COLORS");
        await Assert.That(CapabilityFields.CapabilityOf("CODEBASE")).IsNull();
        await Assert.That(CapabilityFields.IsMeasured("capability.gmcp.declared")).IsFalse();
    }

    [Test]
    public async Task TwoVocabulariesForOneCapabilityArriveAtOneField()
    {
        // The handshake names the telnet option it negotiated, MSSP names the feature: MCCP2 against
        // MCCP, SSL against TLS. Left apart, each produces a half-empty row that reads as absence.
        await Assert.That(CapabilityFields.Canonical("MCCP2")).IsEqualTo("MCCP");
        await Assert.That(CapabilityFields.Canonical("SSL")).IsEqualTo("TLS");
        await Assert.That(CapabilityFields.Canonical("ssl")).IsEqualTo("TLS");

        await Assert.That(CapabilityFields.Measured("MCCP2")).IsNotEqualTo(
            CapabilityFields.Measured(CapabilityFields.Canonical("MCCP2")!));
    }

    [Test]
    public async Task FoldingIsIdempotentAndNoNameIsAlsoAnAlias()
    {
        // A canonical name that is itself an alias would make the field a capability lands in depend
        // on how many times the fold ran — the write path folds once, the read path not at all.
        foreach (var name in CapabilityFields.Names)
        {
            await Assert.That(CapabilityFields.Canonical(name)).IsEqualTo(name);
        }
    }

    [Test]
    public async Task AProtocolWeCarryNoColumnForFoldsToNothingRatherThanToSomethingNear()
    {
        // The registry is not a gate on ingestion — an unknown option is still stored, under its own
        // name — so this has to answer null rather than reach for the closest entry.
        await Assert.That(CapabilityFields.Canonical("MCCP3")).IsNull();
        await Assert.That(CapabilityFields.Canonical("SOME UNOFFICIAL THING")).IsNull();
    }

    [Test]
    public async Task EveryCanonicalNameStillHasAStalenessWindowOnBothSides()
    {
        // Names drives the registry, so a name removed in favour of an alias would leave the field
        // the crawler now writes with no window and silently unjudgeable for age.
        foreach (var capability in CapabilityFields.Names)
        {
            await Assert.That(Registry.Find(CapabilityFields.Measured(capability))).IsNotNull();
            await Assert.That(Registry.Find(CapabilityFields.Declared(capability))).IsNotNull();
        }

        await Assert.That(Registry.Find(CapabilityFields.Measured("MCCP"))).IsNotNull();
        await Assert.That(Registry.Find(CapabilityFields.Declared("TLS"))).IsNotNull();
    }
}
