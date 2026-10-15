using MUI.Catalog.Persistence;

namespace MUI.Catalog.Tests;

/// <summary>
/// The variables SharpMUSH reports beyond the generic set: what each is read as, and what is
/// refused rather than repaired.
/// </summary>
public class SelfReportTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static ProvenanceChip Chip(string value, double ageHours = 1) =>
        new(value, FieldSource.Mssp, Now.AddHours(-ageHours), IsStale: false);

    // ── PORTAL APPLICATION ───────────────────────────────────────────────────────────────────

    [Test]
    public async Task APortalApplicationIsAUrlAndTheRestOfTheLineIsItsName()
    {
        var pages = PortalApplications.Parse(
            "https://mush.example.com/apps/chargen Character Application");

        var page = pages.Single();

        await Assert.That(page.Href).IsEqualTo("https://mush.example.com/apps/chargen");
        await Assert.That(page.Name).IsEqualTo("Character Application");
    }

    [Test]
    public async Task EveryLineIsItsOwnPage()
    {
        var pages = PortalApplications.Parse(
            "https://mush.example.com/apps/chargen Character Application\nhttp://mush.example.com/wiki Wiki");

        await Assert.That(pages.Select(p => p.Name ?? string.Empty))
            .IsEquivalentTo(new[] { "Character Application", "Wiki" });
    }

    [Test]
    public async Task AUrlWithNoNameIsNotALink()
    {
        // We would have to choose the link text, and the game did not give us any.
        var page = PortalApplications.Parse("https://mush.example.com/apps/chargen").Single();

        await Assert.That(page.Href).IsNull();
        await Assert.That(page.Shown).IsEqualTo("https://mush.example.com/apps/chargen");
    }

    [Test]
    [Arguments("javascript:alert(1) Character Application")]
    [Arguments("ftp://mush.example.com/apps Files")]
    [Arguments("mush.example.com/apps/chargen Character Application")]
    [Arguments("Character Application")]
    [Arguments("https://user:pass@mush.example.com/ Login")]
    public async Task AnythingButAnHttpAddressAndANameStaysText(string line)
    {
        // Refused as a link, never hidden: the line is what the game published.
        var page = PortalApplications.Parse(line).Single();

        await Assert.That(page.Href).IsNull();
        await Assert.That(page.Name).IsNull();
        await Assert.That(page.Shown).IsEqualTo(line);
    }

    [Test]
    public async Task NothingIsNoPages()
    {
        await Assert.That(PortalApplications.Parse(null)).IsEmpty();
        await Assert.That(PortalApplications.Parse("  \n ")).IsEmpty();
    }

    // ── PLAYERS DAY / PLAYERS WEEK ───────────────────────────────────────────────────────────

    [Test]
    [Arguments("12", 12)]
    [Arguments(" 0 ", 0)]
    [Arguments("-3", null)]
    [Arguments("1,024", null)]
    [Arguments("lots", null)]
    [Arguments("", null)]
    public async Task OnlyAWholeNumberAtOrAboveZeroIsACount(string value, int? expected)
    {
        await Assert.That(SelfReport.Count(value)).IsEqualTo(expected);
    }

    [Test]
    public async Task TheSentenceIsDatedByItsOlderHalf()
    {
        var recent = RecentPlayers.From(new Dictionary<string, ProvenanceChip>
        {
            ["players day"] = Chip("12", ageHours: 1),
            ["players week"] = Chip("40", ageHours: 5),
        });

        await Assert.That(recent).IsNotNull();
        await Assert.That(recent!.Day).IsEqualTo(12);
        await Assert.That(recent.Week).IsEqualTo(40);
        await Assert.That(recent.Provenance.LastConfirmedAt).IsEqualTo(Now.AddHours(-5));
    }

    [Test]
    public async Task ACountThatIsNotANumberIsLeftToTheDeclaredList()
    {
        // The sentence says only what parses; the rest prints as the game wrote it.
        var declared = new Dictionary<string, ProvenanceChip>
        {
            ["players day"] = Chip("many"),
            ["players week"] = Chip("40"),
        };

        var recent = RecentPlayers.From(declared);

        await Assert.That(recent!.Day).IsNull();
        await Assert.That(recent.Week).IsEqualTo(40);
        await Assert.That(RecentPlayers.Says("players day", declared["players day"])).IsFalse();
        await Assert.That(RecentPlayers.Says("players week", declared["players week"])).IsTrue();
    }

    [Test]
    public async Task NoCountIsNoSentence()
    {
        await Assert.That(RecentPlayers.From(new Dictionary<string, ProvenanceChip>())).IsNull();
    }

    // ── the flags and the registry ───────────────────────────────────────────────────────────

    [Test]
    [Arguments("1", true)]
    [Arguments("0", false)]
    [Arguments("wizards only", null)]
    [Arguments("yes", null)]
    public async Task AFlagIsOneOrZeroAndAnythingElseIsTheGamesOwnWords(string value, bool? expected)
    {
        await Assert.That(SelfReport.Flag(value)).IsEqualTo(expected);
    }

    [Test]
    public async Task TheSixAreRegisteredAndNoneIsOwnerWritable()
    {
        string[] six =
        [
            SelfReport.PlayersDay, SelfReport.PlayersWeek, SelfReport.Logins,
            SelfReport.PlayerCreation, SelfReport.Guests, SelfReport.PortalApplication,
        ];

        foreach (var name in six)
        {
            var definition = FieldRegistry.Instance.Find(name);

            await Assert.That(definition).IsNotNull();
            await Assert.That(definition!.OwnerWritable).IsEqualTo(OwnerWritable.No);
        }

        await Assert.That(FieldRegistry.Instance.Find(SelfReport.Guests)!.Shape).IsEqualTo(FieldShape.Flag);
        await Assert.That(FieldRegistry.Instance.Find(SelfReport.PlayersDay)!.ExpectedRefresh)
            .IsEqualTo(FieldRegistry.Rolling);
    }
}
