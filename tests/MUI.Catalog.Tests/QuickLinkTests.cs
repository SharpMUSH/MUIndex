using MUI.Catalog.Persistence;

namespace MUI.Catalog.Tests;

/// <summary>
/// The links beside a game's name: which values become one, and which are shown and not linked.
/// </summary>
/// <remarks>
/// <para>
/// The values here are real. Every malformed one is a value this catalogue actually holds —
/// <c>www.slothmud.org</c>, <c>msocorcim (at) gmail (dot) com</c>, an <c>https://</c> contact form
/// where MSSP asks for an address — because the interesting half of this feature is what it refuses,
/// and inventing the refusals would test a guess about strangers rather than the strangers.
/// </para>
/// <para>
/// Rule 5 runs through all of it: a value we decline to link is still a value the game published, so
/// the refusal never reaches the row, the declared list or the plain surface. We render it as text.
/// </para>
/// </remarks>
public class QuickLinkTests
{
    private static readonly Guid Game = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 0, 0, 0, TimeSpan.Zero);

    private static GameField Row(string field, FieldSource source, string value, double ageDays = 1) =>
        new(Game, field, source, value, Now.AddDays(-400), Now.AddDays(-ageDays));

    private static IReadOnlyList<QuickLink> Links(params GameField[] rows) =>
        QuickLinks.From(rows, FieldRegistry.Instance, Now);

    // ── what MSSP gives us ───────────────────────────────────────────────────────────────────

    [Test]
    public async Task TheThreeMsspVariablesThatCarryAnAddressBecomeLinks()
    {
        // WEBSITE, DISCORD and CONTACT are the whole of what MSSP offers here. The protocol has no
        // variable for a forum, a wiki or any other social account — DISCORD is the only one it
        // ever added — which is why the other six fields exist and why only an owner can fill them.
        var links = Links(
            Row("WEBSITE", FieldSource.Mssp, "https://www.slothmud.org/"),
            Row("DISCORD", FieldSource.Mssp, "https://discord.gg/5GtCY52"),
            Row("CONTACT", FieldSource.Mssp, "kali@realmsofdespair.com"));

        await Assert.That(links.Select(l => l.Kind))
            .IsEquivalentTo(new[] { LinkKind.Website, LinkKind.Discord, LinkKind.Email });

        await Assert.That(links.Single(l => l.Kind is LinkKind.Email).Href)
            .IsEqualTo("mailto:kali@realmsofdespair.com");
    }

    [Test]
    public async Task TheOrderIsTheSiteThenTheRoomsThenSomebodysInbox()
    {
        var links = Links(
            Row("CONTACT", FieldSource.Mssp, "ops@example.org"),
            Row("X", FieldSource.Owner, "https://x.com/example"),
            Row("DISCORD", FieldSource.Mssp, "https://discord.gg/abc"),
            Row("WEBSITE", FieldSource.Mssp, "https://example.org"));

        // Not the order the rows arrived in, and not alphabetical: the place to go first is first,
        // and the inbox is last because it is the only one of these that reaches a person.
        await Assert.That(links.Select(l => l.Kind).ToArray())
            .IsEquivalentTo(new[] { LinkKind.Website, LinkKind.Discord, LinkKind.X, LinkKind.Email });
    }

    [Test]
    public async Task AnOwnersAnswerOutranksTheGamesOwnReport()
    {
        var links = Links(
            Row("WEBSITE", FieldSource.Mssp, "https://old.example.org", ageDays: 200),
            Row("WEBSITE", FieldSource.Owner, "https://new.example.org"));

        var website = links.Single(l => l.Kind is LinkKind.Website);

        await Assert.That(website.Href).IsEqualTo("https://new.example.org/");
        await Assert.That(website.Source).IsEqualTo(FieldSource.Owner);

        // Declared either way. An owner typing is the same kind of fact as their game reporting —
        // §5.1's ladder decides which is shown and calls neither a measurement.
        await Assert.That(website.IsMeasured).IsFalse();
    }

    [Test]
    public async Task TheUnofficialEmailVariableAnswersOnlyWhereContactDoesNot()
    {
        await Assert.That(Links(Row("EMAIL", FieldSource.Mssp, "staff@example.org")).Single().Field)
            .IsEqualTo("EMAIL");

        var both = Links(
            Row("CONTACT", FieldSource.Mssp, "official@example.org"),
            Row("EMAIL", FieldSource.Mssp, "other@example.org"));

        // One envelope, and it is MSSP's own variable. Two would ask a reader to choose between two
        // addresses for one game with nothing to choose on.
        await Assert.That(both).HasSingleItem();
        await Assert.That(both.Single().Field).IsEqualTo("CONTACT");
    }

    [Test]
    public async Task AContactPageIsAcceptedWhereMsspAsksForAnAddress()
    {
        // One game in this catalogue publishes an https:// form as its CONTACT. It is a way to reach
        // the same people, and refusing it on the specification's technicality would drop a working
        // address to enforce a distinction no reader has.
        var link = Links(Row("CONTACT", FieldSource.Mssp, "https://www.eternitymud.com/contact/")).Single();

        await Assert.That(link.Kind).IsEqualTo(LinkKind.Email);
        await Assert.That(link.Href).IsEqualTo("https://www.eternitymud.com/contact/");
    }

    // ── what is shown and not linked ─────────────────────────────────────────────────────────

    [Test]
    [Arguments("www.slothmud.org")]
    [Arguments("play.arxgame.org")]
    public async Task AWebsiteWithNoSchemeIsNotLinkedAndIsNotRepaired(string published)
    {
        // Three games here publish WEBSITE this way. Prepending https:// would be us guessing
        // whether their server answers on TLS and publishing the guess as their address; the value
        // still prints under "declared by the game", and the scorecard on their own page names the
        // missing prefix. A link we invented that 404s is worse than the text they actually wrote.
        await Assert.That(Links(Row("WEBSITE", FieldSource.Mssp, published))).IsEmpty();
    }

    [Test]
    [Arguments("javascript:alert(1)")]
    [Arguments("data:text/html;base64,PHNjcmlwdD4=")]
    [Arguments("file:///etc/passwd")]
    [Arguments("https://user:pass@evil.example/")]
    [Arguments("/relative/path")]
    [Arguments("")]
    [Arguments("   ")]
    public async Task AValueThatIsNotAnAddressNeverReachesAnHref(string hostile)
    {
        // The MSSP side of this is not a hypothetical: 135 of the WEBSITE values in this catalogue
        // came off strangers' sockets. Blazor encodes the text of an attribute and not the meaning
        // of its scheme, so `javascript:` in an href is script running on our origin.
        await Assert.That(Links(Row("WEBSITE", FieldSource.Mssp, hostile))).IsEmpty();
    }

    [Test]
    [Arguments("msocorcim (at) gmail (dot) com")]
    [Arguments("molly(dot)4d@tele2(dot)se")]
    [Arguments("ausinpowetrs<at>jedimud.net")]
    public async Task AnAddressObfuscatedAgainstHarvestersIsPrintedRatherThanDialled(string written)
    {
        // Six CONTACT values here are written this way on purpose. Turning "(at)" back into "@" is
        // undoing a decision the operator made about their own inbox — and one of these has a real
        // @ in it already, so a naive repair would produce an address with two.
        await Assert.That(Links(Row("CONTACT", FieldSource.Mssp, written))).IsEmpty();
    }

    [Test]
    public async Task AClearedFieldIsAnAbsenceAndNotALink()
    {
        // An owner withdrawing a value writes an empty row rather than deleting one (§7.5). It has
        // to lose here before the ladder runs, exactly as it does in the declared list: filtered
        // after the winner is chosen, an empty owner row wins its group and takes the game's own
        // report down with it.
        var links = Links(
            Row("DISCORD", FieldSource.Mssp, "https://discord.gg/abc"),
            Row("DISCORD", FieldSource.Owner, string.Empty));

        await Assert.That(links.Single().Href).IsEqualTo("https://discord.gg/abc");
    }

    [Test]
    public async Task AWinnerWeCannotLinkDoesNotFallBackToTheRowBeneathIt()
    {
        // The icon beside the title and the value in the list below must be the same fact. Falling
        // back would put an owner's address in one place and their game's in the other, with
        // nothing on the page saying the two disagree.
        var links = Links(
            Row("WEBSITE", FieldSource.Mssp, "https://example.org"),
            Row("WEBSITE", FieldSource.Owner, "www.example.org"));

        await Assert.That(links).IsEmpty();
    }

    // ── the fields themselves ────────────────────────────────────────────────────────────────

    [Test]
    public async Task EveryFieldBehindALinkIsOneTheRegistryShapes()
    {
        // The shape is what the write gate, the renderer and the linter all read. A field named in
        // QuickLinks.Fields and left at the default shape would be silently unlinkable — no error,
        // no icon, and nothing anywhere saying why.
        foreach (var field in QuickLinks.Fields)
        {
            await Assert.That(FieldRegistry.Instance.Find(field)?.Shape)
                .IsNotEqualTo(FieldShape.Text)
                .Because($"{field} can be a link and must be shaped");
        }
    }

    [Test]
    public async Task TheSixFieldsMsspHasNoVariableForAreTheOwnersToFill()
    {
        // MSSP added DISCORD and stopped. There is no FORUM, WIKI, MASTODON, BLUESKY, X or TELEGRAM
        // variable in the specification and none in the wild — every URL-valued field in this
        // catalogue is WEBSITE, ICON or DISCORD — so a crawler will never fill these and the owner
        // is the only source there could be.
        foreach (var field in new[] { "WIKI", "FORUM", "TELEGRAM", "MASTODON", "BLUESKY", "X" })
        {
            await Assert.That(FieldRegistry.Instance.Find(field)?.OwnerWritable)
                .IsEqualTo(OwnerWritable.Enrichment)
                .Because($"{field} has no MSSP variable to override");
        }
    }

    [Test]
    public async Task ALinkCarriesTheAgeOfItsOwnFieldAndNotThePagesAge()
    {
        // Ninety days for an address, per the registry: an invite link expires and a directory that
        // never asked would keep pointing at a room nobody can enter.
        var fresh = Links(Row("DISCORD", FieldSource.Mssp, "https://discord.gg/abc", ageDays: 30)).Single();
        var old = Links(Row("DISCORD", FieldSource.Mssp, "https://discord.gg/abc", ageDays: 200)).Single();

        await Assert.That(fresh.IsStale).IsFalse();
        await Assert.That(old.IsStale).IsTrue();
    }
}
