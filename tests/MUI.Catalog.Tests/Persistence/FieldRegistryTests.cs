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
        // We re-measure the handshake on every probe, so a measured capability unconfirmed for a day
        // is a fact about our crawler rather than about the game. The game's own claim is hand-typed
        // and expected to sit still.
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
        // Spec §8.5, widened: an MSSP report is not a measurement — §5.1 calls it a game filling in a
        // self-description it maintains — so a verified owner may answer it. What they may answer is
        // what a person types into mush.cnf, and nothing the codebase fills in for them.
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
        // The line between the two halves of MSSP. HOSTNAME, PORT, IP and CODEBASE describe the
        // connection rather than the game and are auto-filled by the codebase, so a hand-typed answer
        // and a measured one would mean genuinely different things under one name.
        //
        // capability.*.declared is left out for a sharper reason: the matrix's whole job is to show
        // what a game claims beside what its handshake offered, and an owner editing the claimed
        // column is editing one half of a comparison about themselves.
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
        // A game may emit any unofficial MSSP variable it likes, and the registry is not a gate on
        // ingestion. We store the value; we decline to judge an age we have no window for, because
        // inventing one would put a fabricated fact on a public page.
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
}
