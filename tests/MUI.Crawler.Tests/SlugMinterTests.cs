using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawler.Tests.Support;
using MUI.Discovery;

namespace MUI.Crawler.Tests;

/// <summary>
/// Spec §5.7 — a game renames itself, and its URL follows it once the name has held.
/// </summary>
/// <remarks>
/// A rename that re-minted immediately would churn the URL of any game that flips its name, so
/// nothing moves until the grace period passes; a re-mint that didn't record the old slug would break
/// every existing link.
/// </remarks>
public class SlugMinterTests
{
    private static readonly DateTimeOffset Now = Probes.Observed;

    private static readonly TimeSpan Grace = TimeSpan.FromDays(14);

    [Test]
    public async Task ANameThatOnlyJustChangedDoesNotMoveTheUrl()
    {
        var catalogue = new Catalogue();
        var game = catalogue.Listed();
        await DeclaredAsync(catalogue, game, "Harbourlight", changedAt: Now.AddDays(-1));

        var rename = await catalogue.Minter(Grace).ConsiderAsync(game, Now);

        await Assert.That(rename).IsNull();
        await Assert.That((await catalogue.Games.ByIdAsync(game))!.Slug).IsEqualTo("corvid");
        await Assert.That((await catalogue.Games.ByIdAsync(game))!.Name).IsEqualTo("Corvid");
    }

    [Test]
    public async Task ANameThatHasHeldForTheGracePeriodMintsANewUrlAndTheOldOneRedirects()
    {
        var catalogue = new Catalogue();
        var game = catalogue.Listed();
        await DeclaredAsync(catalogue, game, "Harbourlight", changedAt: Now.AddDays(-15));

        var rename = await catalogue.Minter(Grace).ConsiderAsync(game, Now);

        await Assert.That(rename!.Slug).IsEqualTo("harbourlight");
        await Assert.That(rename.FormerSlug).IsEqualTo("corvid");
        await Assert.That((await catalogue.Games.ByIdAsync(game))!.Name).IsEqualTo("Harbourlight");
        await Assert.That(await catalogue.Slugs.CurrentSlugAsync("corvid")).IsEqualTo("harbourlight");
    }

    [Test]
    public async Task AGameThatFlipsItsNameDailyNeverChurnsItsUrl()
    {
        // §5.7's own reason for the grace period. Fourteen renames a day apart, considered after each
        // one: the name is never fourteen days old, so the URL never moves.
        var catalogue = new Catalogue();
        var game = catalogue.Listed();
        var minter = catalogue.Minter(Grace);

        for (var day = 1; day <= 14; day++)
        {
            await DeclaredAsync(catalogue, game, $"Corvid Day {day}", changedAt: Now.AddDays(day));
            await minter.ConsiderAsync(game, Now.AddDays(day));
        }

        await Assert.That((await catalogue.Games.ByIdAsync(game))!.Slug).IsEqualTo("corvid");
        await Assert.That(catalogue.Slugs.All).IsEmpty();
    }

    [Test]
    public async Task ANameThatRestatesTheCodebaseIsNotARename()
    {
        // The same rule CatalogueBinder lists a game under, read a second time: a server whose NAME is
        // its codebase's has not said what it is called, and re-minting on it would march a dozen
        // unrelated games onto /g/pennmush-4.
        var catalogue = new Catalogue();
        var game = catalogue.Listed();
        await DeclaredAsync(catalogue, game, "PennMUSH 1.8.8p0", changedAt: Now.AddYears(-1));
        await catalogue.Fields.UpsertAsync(new GameField(
            game, IdentityMsspVariables.Codebase, FieldSource.Mssp, "PennMUSH 1.8.8p0", Now, Now));

        var rename = await catalogue.Minter(Grace).ConsiderAsync(game, Now);

        await Assert.That(rename).IsNull();
        await Assert.That((await catalogue.Games.ByIdAsync(game))!.Slug).IsEqualTo("corvid");
    }

    [Test]
    public async Task AGameThatHasNeverSaidWhatItIsCalledIsLeftAlone()
    {
        var catalogue = new Catalogue();
        var game = catalogue.Listed();

        await Assert.That(await catalogue.Minter(Grace).ConsiderAsync(game, Now)).IsNull();
    }

    [Test]
    public async Task AGameRenamedTwiceRedirectsFromItsOldestUrl()
    {
        var catalogue = new Catalogue();
        var game = catalogue.Listed();
        var minter = catalogue.Minter(Grace);

        await DeclaredAsync(catalogue, game, "Harbourlight", changedAt: Now.AddDays(-15));
        await minter.ConsiderAsync(game, Now);

        await DeclaredAsync(catalogue, game, "Tidewater Nights", changedAt: Now.AddDays(1));
        await minter.ConsiderAsync(game, Now.AddDays(16));

        await Assert.That((await catalogue.Games.ByIdAsync(game))!.Slug).IsEqualTo("tidewater-nights");
        await Assert.That(await catalogue.Slugs.CurrentSlugAsync("corvid"))
            .IsEqualTo("tidewater-nights");
        await Assert.That(await catalogue.Slugs.CurrentSlugAsync("harbourlight"))
            .IsEqualTo("tidewater-nights");
    }

    [Test]
    public async Task ASecondLookAtAGameThatHasAlreadyBeenRenamedWritesNothing()
    {
        // Every probe of every game passes through here, for ever. The common case has to be a read.
        var catalogue = new Catalogue();
        var game = catalogue.Listed();
        var minter = catalogue.Minter(Grace);
        await DeclaredAsync(catalogue, game, "Harbourlight", changedAt: Now.AddDays(-15));

        await minter.ConsiderAsync(game, Now);
        var second = await minter.ConsiderAsync(game, Now.AddDays(1));

        await Assert.That(second).IsNull();
        await Assert.That(catalogue.Slugs.All).Count().IsEqualTo(1);
    }

    [Test]
    public async Task AUrlAnotherGameIsHoldingIsNotTakenFromIt()
    {
        var catalogue = new Catalogue();
        var game = catalogue.Listed();
        catalogue.Games.Seed(new GameRecord(
            Guid.CreateVersion7(), "harbourlight", "Harbourlight", null, LifecycleState.Active,
            false, Now.AddYears(-2)));

        await DeclaredAsync(catalogue, game, "Harbourlight", changedAt: Now.AddDays(-15));

        var rename = await catalogue.Minter(Grace).ConsiderAsync(game, Now);

        await Assert.That(rename!.Slug).IsEqualTo("harbourlight-2");
        await Assert.That(await catalogue.Slugs.CurrentSlugAsync("corvid")).IsEqualTo("harbourlight-2");
    }

    [Test]
    public async Task AUrlAnotherGameGaveUpIsStillNotFree()
    {
        // Nobody wears it and somebody is still holding it: a bookmark for /g/harbourlight points at
        // the game that gave it up, and handing it to a second game would silently redirect that
        // reader somewhere they never asked for.
        var catalogue = new Catalogue();
        var game = catalogue.Listed();
        var other = Guid.CreateVersion7();
        catalogue.Slugs.Retire("harbourlight", other, Now.AddYears(-1));

        await DeclaredAsync(catalogue, game, "Harbourlight", changedAt: Now.AddDays(-15));

        var rename = await catalogue.Minter(Grace).ConsiderAsync(game, Now);

        await Assert.That(rename!.Slug).IsEqualTo("harbourlight-2");
    }

    [Test]
    public async Task AGameGetsItsOwnFormerUrlBackRatherThanASuffix()
    {
        var catalogue = new Catalogue();
        var game = catalogue.Listed();
        var minter = catalogue.Minter(Grace);

        await DeclaredAsync(catalogue, game, "Harbourlight", changedAt: Now.AddDays(-15));
        await minter.ConsiderAsync(game, Now);

        await DeclaredAsync(catalogue, game, "Corvid", changedAt: Now.AddDays(1));
        var back = await minter.ConsiderAsync(game, Now.AddDays(16));

        await Assert.That(back!.Slug).IsEqualTo("corvid");

        // And the row that now points at a current slug stops answering rather than looping.
        await Assert.That(await catalogue.Slugs.CurrentSlugAsync("corvid")).IsNull();
        await Assert.That(await catalogue.Slugs.CurrentSlugAsync("harbourlight")).IsEqualTo("corvid");
    }

    [Test]
    public async Task AUrlGivenUpTwiceIsReportedAsGivenUpBothTimes()
    {
        // A -> B -> A -> B: the fourth rename retires a slug the history already holds. The store
        // keeps the first retirement and still reports the move; the same sequence is asserted
        // against Postgres in AGameThatGivesUpAUrlItHasGivenUpBeforeStillReportsTheMove.
        var catalogue = new Catalogue();
        var game = catalogue.Listed();
        var minter = catalogue.Minter(Grace);

        await DeclaredAsync(catalogue, game, "Harbourlight", changedAt: Now.AddDays(-15));
        await minter.ConsiderAsync(game, Now);

        await DeclaredAsync(catalogue, game, "Corvid", changedAt: Now.AddDays(1));
        await minter.ConsiderAsync(game, Now.AddDays(16));

        await DeclaredAsync(catalogue, game, "Harbourlight", changedAt: Now.AddDays(17));
        var again = await minter.ConsiderAsync(game, Now.AddDays(32));

        await Assert.That(again!.Slug).IsEqualTo("harbourlight");
        await Assert.That(again.FormerSlug).IsEqualTo("corvid");

        // "corvid" was retired the first time round and is retired again here; the earlier date
        // stands, because it is when the URL somebody bookmarked started redirecting.
        await Assert.That(catalogue.Slugs.All.Single(row => row.Slug == "corvid").RetiredAt)
            .IsEqualTo(Now);
        await Assert.That(catalogue.Slugs.All.Single(row => row.Slug == "harbourlight").RetiredAt)
            .IsEqualTo(Now.AddDays(16));
    }

    [Test]
    public async Task ANameThatMintsTheSameSlugStillFollowsTheGame()
    {
        // The listing shows the game's name, and it has genuinely changed. The URL has not, so there
        // is nothing to redirect and nothing is written to the history.
        var catalogue = new Catalogue();
        var game = catalogue.Listed();
        await DeclaredAsync(catalogue, game, "Corvid!", changedAt: Now.AddDays(-15));

        var rename = await catalogue.Minter(Grace).ConsiderAsync(game, Now);

        await Assert.That(rename!.FormerSlug).IsNull();
        await Assert.That(rename.Slug).IsEqualTo("corvid");
        await Assert.That((await catalogue.Games.ByIdAsync(game))!.Name).IsEqualTo("Corvid!");
        await Assert.That(catalogue.Slugs.All).IsEmpty();
    }

    [Test]
    public async Task AGameListedUnderItsAddressGetsTheNameItLaterPublishes()
    {
        // A game with no meaningful MSSP is listed as host:port (CatalogueBinder.NameOf). When it
        // does publish a name, the value has never *changed* — so stability is measured from the age
        // of the row, which is the same question asked of a field that has only ever said one thing.
        var catalogue = new Catalogue();
        var game = Guid.CreateVersion7();

        catalogue.Games.Seed(new GameRecord(
            game, "mud-example-org-4201", "mud.example.org:4201", null, LifecycleState.Active,
            false, Now.AddYears(-1)));

        await catalogue.Fields.UpsertAsync(new GameField(
            game, IdentityMsspVariables.Name, FieldSource.Mssp, "Harbourlight",
            FirstSeenAt: Now.AddDays(-15), LastConfirmedAt: Now));

        var rename = await catalogue.Minter(Grace).ConsiderAsync(game, Now);

        await Assert.That(rename!.Slug).IsEqualTo("harbourlight");
        await Assert.That(await catalogue.Slugs.CurrentSlugAsync("mud-example-org-4201"))
            .IsEqualTo("harbourlight");
    }

    /// <summary>
    /// An owner's name takes effect at once, because the grace answers a question they have already
    /// answered.
    /// </summary>
    /// <remarks>
    /// §5.7's fourteen days exist because <em>MSSP flaps</em>: a name published while a config was
    /// half-edited should not churn a URL, and the only way to tell a settled name from a passing one
    /// is to wait. An owner pressing save on a form is not a flap, and making them wait a fortnight
    /// to see their game's name change — while the site showed the new one on the page and the old
    /// one in the URL — would be the interface disbelieving somebody it has already verified.
    /// </remarks>
    [Test]
    public async Task AnOwnersNameTakesEffectWithoutWaitingOutTheGrace()
    {
        var catalogue = new Catalogue();
        var game = catalogue.Listed();

        var rename = await catalogue.Minter(Grace).ApplyAsync(game, "Harbourlight");

        await Assert.That(rename!.Slug).IsEqualTo("harbourlight");
        await Assert.That(rename.FormerSlug).IsEqualTo("corvid");
        await Assert.That((await catalogue.Games.ByIdAsync(game))!.Name).IsEqualTo("Harbourlight");
        await Assert.That(await catalogue.Slugs.CurrentSlugAsync("corvid")).IsEqualTo("harbourlight");
    }

    /// <summary>
    /// An owner may name their game after its codebase; an unedited server may not.
    /// </summary>
    /// <remarks>
    /// <c>MsspDefaults.IsPlaceholder</c> exists because every unedited PennMUSH on the internet
    /// publishes <c>NAME "PennMUSH"</c>, and admitting one would drag a dozen unrelated games towards
    /// <c>/g/pennmush-4</c>. That is a rule about <em>defaults</em>. A name a verified owner typed on
    /// purpose is edited by definition, and the operator of the PennMUSH development server is
    /// entitled to call it PennMUSH.
    /// </remarks>
    [Test]
    public async Task AnOwnerMayNameTheirGameAfterItsCodebase()
    {
        var catalogue = new Catalogue();
        var game = catalogue.Listed();

        var rename = await catalogue.Minter(Grace).ApplyAsync(game, "PennMUSH");

        await Assert.That(rename!.Name).IsEqualTo("PennMUSH");
        await Assert.That((await catalogue.Games.ByIdAsync(game))!.Name).IsEqualTo("PennMUSH");
    }

    /// <summary>
    /// Once an owner has said what a game is called, the crawler stops arguing with them.
    /// </summary>
    /// <remarks>
    /// The override wins on every surface (§5.1), so a minter still re-minting from MSSP would spend
    /// every cycle renaming the game back to what its config says — the owner's name on the page and
    /// the report's in the URL, for ever, with a change-feed entry each time.
    /// </remarks>
    [Test]
    public async Task TheCrawlerDoesNotRenameAGameBackToWhatItsConfigSays()
    {
        var catalogue = new Catalogue();
        var game = catalogue.Listed();
        await OwnerDeclaredAsync(catalogue, game, "Harbourlight");
        await catalogue.Minter(Grace).ApplyAsync(game, "Harbourlight");
        await DeclaredAsync(catalogue, game, "Corvid Reborn", changedAt: Now.AddDays(-60));

        var rename = await catalogue.Minter(Grace).ConsiderAsync(game, Now);

        await Assert.That(rename).IsNull();
        await Assert.That((await catalogue.Games.ByIdAsync(game))!.Name).IsEqualTo("Harbourlight");
    }

    /// <summary>
    /// Withdrawing the override hands the name back to the report, under the ordinary grace.
    /// </summary>
    /// <remarks>
    /// Nothing is deleted: the empty row goes on existing and the withdrawal is in the change feed.
    /// What it stops doing is outranking anything, so the crawler's own rule applies again — which is
    /// the difference between an override and a permanent claim on a URL.
    /// </remarks>
    [Test]
    public async Task WithdrawingTheOverrideLetsTheReportHaveTheNameBack()
    {
        var catalogue = new Catalogue();
        var game = catalogue.Listed();
        await OwnerDeclaredAsync(catalogue, game, "Harbourlight");
        await catalogue.Minter(Grace).ApplyAsync(game, "Harbourlight");
        await DeclaredAsync(catalogue, game, "Corvid Reborn", changedAt: Now.AddDays(-60));

        // Withdrawn, which is an empty value on a row that goes on existing — nothing is deleted.
        await OwnerDeclaredAsync(catalogue, game, string.Empty);

        var rename = await catalogue.Minter(Grace).ConsiderAsync(game, Now);

        await Assert.That(rename!.Name).IsEqualTo("Corvid Reborn");
    }

    /// <summary>
    /// A game whose name is in a script the slug fold cannot keep takes the name and keeps the URL.
    /// </summary>
    /// <remarks>
    /// <c>GameSlug.Mint</c> keeps ASCII and Latin-1 only, so a Hangul, Cyrillic or Kanji name folds to
    /// the empty string, whose fallback is the word <c>game</c> — a slug this game would then hold
    /// forever while the next such game took <c>game-2</c>. The address it's already listed at is a
    /// real URL of its own; the name belongs only on the page, where the fold doesn't reach.
    /// </remarks>
    [Test]
    public async Task ANameTheFoldCannotKeepLeavesTheAddressInTheUrl()
    {
        var catalogue = new Catalogue();
        var game = catalogue.Listed(slug: "110-10-160-150-4001", name: "110.10.160.150:4001");
        await DeclaredAsync(catalogue, game, "엘리시안 전기", changedAt: Now.AddDays(-15));

        var rename = await catalogue.Minter(Grace).ConsiderAsync(game, Now);

        await Assert.That(rename!.Name).IsEqualTo("엘리시안 전기");
        await Assert.That(rename.Slug).IsEqualTo("110-10-160-150-4001");
        await Assert.That(rename.FormerSlug).IsNull();
        await Assert.That(catalogue.Slugs.All).IsEmpty();
        await Assert.That((await catalogue.Games.ByIdAsync(game))!.Name).IsEqualTo("엘리시안 전기");
    }

    /// <summary>The row <c>OwnerEnrichment</c> stores when an owner answers <c>NAME</c>.</summary>
    private static Task OwnerDeclaredAsync(Catalogue catalogue, Guid game, string name) =>
        catalogue.Fields.UpsertAsync(new GameField(
            game, IdentityMsspVariables.Name, FieldSource.Owner, name, Now.AddDays(-1), Now));

    /// <summary>
    /// A game declaring a name it did not declare before: the row, and the change-feed entry that
    /// says when it started saying it. Both, because they are what the reconciler writes.
    /// </summary>
    private static async Task DeclaredAsync(
        Catalogue catalogue, Guid game, string name, DateTimeOffset changedAt)
    {
        var stored = await catalogue.Fields.ForGameAsync(game);
        var existing = stored.FirstOrDefault(
            row => string.Equals(row.Field, IdentityMsspVariables.Name, StringComparison.OrdinalIgnoreCase));

        await catalogue.Fields.UpsertAsync(new GameField(
            game,
            IdentityMsspVariables.Name,
            FieldSource.Mssp,
            name,
            FirstSeenAt: existing?.FirstSeenAt ?? changedAt.AddYears(-1),
            LastConfirmedAt: changedAt));

        await catalogue.Fields.RecordChangeAsync(new FieldChange(
            game, IdentityMsspVariables.Name, FieldSource.Mssp, existing?.Value, name, changedAt));
    }
}
