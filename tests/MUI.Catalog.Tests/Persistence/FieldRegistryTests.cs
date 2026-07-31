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
    public async Task TheOwnerEnrichableFieldsAreTheOnesMsspCannotExpress()
    {
        // Spec §3.2 names exactly these as absent from MSSP.
        var enrichable = FieldRegistry.All.Where(f => f.OwnerEnrichable).Select(f => f.Name).ToList();

        await Assert.That(enrichable).Contains("FANDOM");
        await Assert.That(enrichable).Contains("APPLICATION PROCESS");
        await Assert.That(enrichable).Contains("RP ENFORCEMENT");
        await Assert.That(enrichable).Contains("CONSENT TOOLS");
    }

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
