using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawler.Tests.Support;
using MUI.Discovery;

namespace MUI.Crawler.Tests;

/// <summary>
/// Spec §5.7 — a game renames itself, and its URL follows it once the name has held.
/// </summary>
/// <remarks>
/// Two halves, and the second is the one with teeth. A rename that re-minted immediately would churn
/// the URL of any game that flips its name, so nothing moves until the grace period has passed; and a
/// re-mint that did not record the old slug would break every link anybody had — which is the promise
/// <c>game_slug_history</c> exists to keep, and the reason the minter and the table are one act.
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
        // A -> B -> A -> B, and the fourth rename retires a slug the history already holds. The
        // store keeps the first retirement and still reports the move; this pins the fake to the
        // same answer, which is where the real one diverged — it read the answer off an insert that
        // ON CONFLICT had suppressed. The same sequence is asserted against Postgres in
        // AGameThatGivesUpAUrlItHasGivenUpBeforeStillReportsTheMove.
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
