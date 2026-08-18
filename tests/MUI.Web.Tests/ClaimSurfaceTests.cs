using System.Security.Claims;

using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Discovery;
using MUI.Web.Accounts;
using MUI.Web.Components.Pages;
using MUI.Web.Fixtures;
using MUI.Web.Localization;

namespace MUI.Web.Tests;

/// <summary>
/// What the claim surfaces do when there is no database, and what the token is made of.
/// </summary>
/// <remarks>The demo fixture has no accounts, claims, or provable games — pins the half of §8 assertable without one: surfaces are <em>absent</em>, not present and broken.</remarks>
public class ClaimSurfaceTests
{
    /// <summary>
    /// A game page over the fixture does not invite a claim it cannot process.
    /// </summary>
    /// <remarks>Gated on the claim service being registered — half a claim flow over invented games would have an operator publish a token for a listing that doesn't exist.</remarks>
    [Test]
    public async Task AGamePageOverTheFixtureDoesNotOfferToBeClaimed()
    {
        var page = await Render.PageAsync<Game>(new() { ["Slug"] = "m-u-s-h" });

        // One sentence, where the badge and a paragraph below used to say the same thing twice.
        await Assert.That(Render.Words(page)).Contains("Unclaimed — everything here was measured.");
        await Assert.That(page).DoesNotContain("Claim this game");
    }

    /// <summary>
    /// The sentence and the link it hands off to read as one sentence, not two words run together.
    /// </summary>
    /// <remarks>Whitespace-only text between two Razor code blocks compiles away rather than rendering as a space; needs a real claim service to reach the branch that lost it.</remarks>
    [Test]
    public async Task TheClaimInvitationHasASpaceBeforeTheLink()
    {
        var page = await Render.PageAsync<Game>(
            new() { ["Slug"] = "m-u-s-h" },
            query: string.Empty,
            claimService: new ClaimService(new NullClaimStore(), new NullGameStore(), TimeProvider.System));

        await Assert.That(Render.Text(page))
            .Contains("Unclaimed — everything here was measured. Claim this game");
    }

    /// <summary>The sign-in page says why it cannot sign anybody in, rather than offering a button.</summary>
    [Test]
    public async Task SignInOverTheFixtureSaysThereIsNothingToSignInTo()
    {
        var page = await Render.PageAsync<SignIn>([]);

        await Assert.That(page).Contains("demo fixture");
        await Assert.That(page).DoesNotContain("Sign in with a passkey");
    }

    /// <summary>
    /// A token is unguessable, and shaped so a person reading one back does not lose.
    /// </summary>
    /// <remarks>Random, not derived from the game, so it can't be guessed before the operator publishes it.</remarks>
    [Test]
    public async Task ATokenIsPrefixedUnguessableAndFreeOfLookalikeCharacters()
    {
        var minted = Enumerable.Range(0, 200).Select(_ => ClaimToken.Mint()).ToList();

        await Assert.That(minted.Distinct().Count()).IsEqualTo(minted.Count);

        foreach (var token in minted)
        {
            await Assert.That(ClaimToken.LooksLikeOne(token)).IsTrue();
            await Assert.That(token).StartsWith(ClaimToken.Prefix);

            // 0/o, 1/l/i and u/v each reduced to one member so hand-transcribing doesn't punish the reader.
            var body = token[ClaimToken.Prefix.Length..];
            await Assert.That(body.Any(c => c is '0' or 'o' or '1' or 'l' or 'i' or 'u' or 'v'))
                .IsFalse();
        }
    }

    /// <summary>A shape check is not a verification, and must never be mistaken for one.</summary>
    [Test]
    public async Task LookingLikeATokenProvesNothing()
    {
        await Assert.That(ClaimToken.LooksLikeOne("muidx-22222222222222222222")).IsTrue();
        await Assert.That(ClaimToken.LooksLikeOne("muidx-short")).IsFalse();
        await Assert.That(ClaimToken.LooksLikeOne("not-ours-2222222222222222")).IsFalse();
        await Assert.That(ClaimToken.LooksLikeOne(null)).IsFalse();
    }

    /// <summary>
    /// The channels a token may be published in are the two a probe can read, and DNS is not one.
    /// </summary>
    /// <remarks>§8.3: a TXT record proves control of a hostname only, and hosting routinely puts unrelated games on one domain by port — DNS is excluded to stop a host's operator claiming every game on it.</remarks>
    [Test]
    public async Task OnlyChannelsAProbeCanReadAreClaimChannels()
    {
        await Assert.That(Enum.GetNames<ClaimChannel>()).IsEquivalentTo(new[] { "Mssp", "ConnectScreen" });
    }

    // ── the language the claim page is answered in ────────────────────────────

    /// <summary>
    /// Every word of the claim page comes out of the message bundle.
    /// </summary>
    /// <remarks>Driven on the state with the most words: a signed-in operator with a pending token. The pseudolocale brackets anything that reached a reader through <see cref="Messages"/>.</remarks>
    [Test]
    public async Task EveryWordOfClaimingComesFromTheBundle()
    {
        var english = Render.Text(await ClaimWorld.Pending(Locales.SourceTag));
        var pseudo = Render.Text(await ClaimWorld.Pending("qps-ploc"));

        await Assert.That(pseudo).Contains("⟦");

        await Assert.That(english).Contains(Render.Words(
            Messages.For(Locales.SourceTag, "claim.either.heading")));

        foreach (var id in Sayable("claim."))
        {
            var sentence = Render.Words(Messages.For(Locales.SourceTag, id));

            if (!english.Contains(sentence, StringComparison.Ordinal))
            {
                continue;
            }

            await Assert.That(pseudo)
                .DoesNotContain(sentence)
                .Because($"{id} is rendered as English whatever language the page was asked for");
        }
    }

    /// <summary>
    /// A German request gets German wherever German exists, and the token survives it.
    /// </summary>
    /// <remarks>Gated on <see cref="Messages.HasOwn"/> since most ids fall back to English until translated (designed behaviour). Machine-voice parts an operator pastes into a config file are asserted directly, since they must never translate.</remarks>
    [Test]
    public async Task AGermanRequestGetsGermanOnTheClaimPage()
    {
        var english = Render.Text(await ClaimWorld.Pending(Locales.SourceTag));
        var german = Render.Text(await ClaimWorld.Pending("de"));

        foreach (var id in Sayable("claim.").Where(i => Messages.HasOwn("de", i)))
        {
            var de = Render.Words(Messages.For("de", id));
            var en = Render.Words(Messages.For(Locales.SourceTag, id));

            if (string.Equals(de, en, StringComparison.Ordinal)
                || !english.Contains(en, StringComparison.Ordinal))
            {
                continue;
            }

            await Assert.That(german).Contains(de).Because($"{id} is not answered in German");
        }

        foreach (var id in Sayable("claim.").Where(i => !Messages.HasOwn("de", i)))
        {
            await Assert.That(german).DoesNotContain(id).Because($"{id} reached a reader as its id");
        }

        // Machine voice must be byte-identical in every language — a translated token could never verify.
        await Assert.That(german).Contains(ClaimWorld.Token);
        await Assert.That(german).Contains(ClaimTokenBeacon.MsspVariable);
        await Assert.That(german).Contains(ClaimTokenBeacon.ConnectScreenPrefix);

        foreach (var accepted in ClaimTokenBeacon.AcceptedMsspVariables)
        {
            await Assert.That(german).Contains(accepted);
        }
    }

    /// <summary>
    /// The states a reader of the demo site can actually reach, in the language they asked for.
    /// </summary>
    /// <remarks>No database behind the fixture, so claiming is absent rather than broken — a missing slug and an unclaimable game are different answers.</remarks>
    [Test]
    [Arguments(Locales.SourceTag)]
    [Arguments("qps-ploc")]
    [Arguments("de")]
    public async Task TheDegradedClaimPageAnswersInTheReadersLanguage(string tag)
    {
        var missing = Render.Text(await ClaimWorld.NoGame(tag));

        await Assert.That(missing).Contains(Render.Words(Messages.For(tag, "claim.noGame")));

        var unclaimable = Render.Text(await ClaimWorld.NoDatabase(tag));

        await Assert.That(unclaimable).Contains(Render.Words(Messages.For(tag, "claim.noDatabase")));

        await Assert.That(unclaimable).Contains(Render.Words(
            Messages.Say(tag, "claim.title", ("game", ClaimWorld.GameName))));
    }

    /// <summary>Ids under a prefix that can be rendered without arguments.</summary>
    private static IEnumerable<string> Sayable(string prefix) =>
        Messages.Ids
            .Where(i => i.StartsWith(prefix, StringComparison.Ordinal))
            .Where(i => !IcuMessage.Compile(Messages.Pattern(Locales.SourceTag, i)!).Arguments().Any());

    /// <summary>
    /// The claim page, in one locale, in each state it can be reached in.
    /// </summary>
    /// <remarks>At component level with an <see cref="HttpContext"/> cascaded in — §8.2's passkey-only sign-in means a loopback host can't produce a real session.</remarks>
    private static class ClaimWorld
    {
        public const string Slug = "ashen-court";

        public const string GameName = "Ashen Court";

        /// <summary>A token of the right shape. Machine voice, and identical in every language.</summary>
        public const string Token = "muidx-t3kn2wxy7q4m8dgh6prs";

        private static readonly DateTimeOffset Now = FixtureGameQueries.Now;

        private static readonly Guid GameId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000007");

        /// <summary>A signed-in operator with a token outstanding: the page an owner works from.</summary>
        public static Task<string> Pending(string tag) => RenderAsync(tag, withGame: true, withClaims: true);

        /// <summary>A slug that names nothing.</summary>
        public static Task<string> NoGame(string tag) => RenderAsync(tag, withGame: false, withClaims: true);

        /// <summary>A real game on a site with no database behind it — the demo fixture's state.</summary>
        public static Task<string> NoDatabase(string tag) =>
            RenderAsync(tag, withGame: true, withClaims: false);

        private static Task<string> RenderAsync(string tag, bool withGame, bool withClaims)
        {
            var user = new MuiUser { DisplayName = "corvid-admin", CreatedAt = Now };

            var claim = new GameClaim
            {
                Id = Guid.CreateVersion7(),
                GameId = GameId,
                UserId = user.Id,
                Token = Token,
                IssuedAt = Now.AddDays(-2),
                ExpiresAt = Now.AddDays(12),
            };

            return Render.ComponentAsync<MUI.Web.Components.Pages.Claim>(
                new Dictionary<string, object?> { ["Slug"] = Slug },
                services =>
                {
                    var games = new Games(withGame
                        ? new GameRecord(GameId, Slug, GameName, null, LifecycleState.Active, false, Now)
                        : null);

                    var claims = new Claims([claim]);

                    services.AddLogging();
                    services.AddSingleton<TimeProvider>(new Frozen(Now));
                    services.AddSingleton(new MUI.Web.Data.CatalogueSource(IsMeasured: withClaims));
                    services.AddSingleton<AntiforgeryStateProvider, NoAntiforgery>();
                    services.AddSingleton<IGameStore>(games);

                    // Registered together, matching the site: claiming exists only with a connection string.
                    if (withClaims)
                    {
                        services.AddSingleton<IClaimStore>(claims);
                        services.AddSingleton(new ClaimService(claims, games, new Frozen(Now)));
                    }

                    services.AddSingleton<IUserStore<MuiUser>>(new Accounts([user], Now));
                    services.AddIdentityCore<MuiUser>();

                    var context = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                            [new System.Security.Claims.Claim(
                                ClaimTypes.NameIdentifier, user.Id.ToString())],
                            "Test")),
                    };

                    context.Items[LocaleRouting.ItemKey] =
                        new LocaleContext(Locales.Find(tag)!, FromPath: tag != Locales.SourceTag);

                    services.AddCascadingValue(_ => context);
                });
        }

        private sealed class Games(GameRecord? game) : IGameStore
        {
            public Task<GameRecord?> ByIdAsync(Guid id, CancellationToken ct = default) =>
                Task.FromResult(game?.Id == id ? game : null);

            public Task<GameRecord?> BySlugAsync(string slug, CancellationToken ct = default) =>
                Task.FromResult(game?.Slug == slug ? game : null);

            public Task InsertAsync(GameRecord g, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task ExcludeAsync(Guid id, string reason, DateTimeOffset at, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task IncludeAsync(Guid id, DateTimeOffset at, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task UnlistAsync(Guid id, Guid by, DateTimeOffset at, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task RelistAsync(Guid id, DateTimeOffset at, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task SetStateAsync(Guid id, LifecycleState s, DateTimeOffset at, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task MarkReachableAsync(Guid id, DateTimeOffset at, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task<string?> RenameAsync(
                Guid id,
                string name,
                string slug,
                DateTimeOffset at,
                CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task SetClaimedAsync(Guid id, bool isClaimed, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task CorroborateAsync(
                Guid id,
                DateTimeOffset at,
                IReadOnlyList<string> signals,
                CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task<IReadOnlyList<GameRecord>> UnarchivedAsync(CancellationToken ct = default) =>
                throw new NotSupportedException();
        }

        private sealed class Claims(List<GameClaim> claims) : IClaimStore
        {
            public Task<GameClaim?> FindAsync(Guid id, CancellationToken ct = default) =>
                Task.FromResult(claims.FirstOrDefault(c => c.Id == id));

            public Task<IReadOnlyList<GameClaim>> ForGameAsync(Guid game, CancellationToken ct = default) =>
                Task.FromResult<IReadOnlyList<GameClaim>>([.. claims.Where(c => c.GameId == game)]);

            public Task<IReadOnlyList<GameClaim>> ForUserAsync(Guid user, CancellationToken ct = default) =>
                Task.FromResult<IReadOnlyList<GameClaim>>([.. claims.Where(c => c.UserId == user)]);

            public Task<GameClaim?> FindPendingByTokenAsync(Guid g, string t, CancellationToken ct = default) =>
                Task.FromResult<GameClaim?>(null);

            public Task InsertAsync(GameClaim claim, CancellationToken ct = default)
            {
                claims.Add(claim);
                return Task.CompletedTask;
            }

            public Task UpdateAsync(GameClaim claim, CancellationToken ct = default) => Task.CompletedTask;

            public Task RecordEventAsync(ClaimEvent e, CancellationToken ct = default) => Task.CompletedTask;

            public Task<IReadOnlyList<ClaimEvent>> EventsAsync(Guid claim, CancellationToken ct = default) =>
                Task.FromResult<IReadOnlyList<ClaimEvent>>([]);
        }

        private sealed class Frozen(DateTimeOffset at) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => at;
        }

        private sealed class NoAntiforgery : AntiforgeryStateProvider
        {
            public override AntiforgeryRequestToken? GetAntiforgeryToken() => null;
        }
    }

    /// <summary>
    /// A store <see cref="ClaimService"/> is constructed over but never actually asked —
    /// <see cref="Game.Claimable"/> only checks whether a <c>ClaimService</c> resolves from DI.
    /// </summary>
    private sealed class NullGameStore : IGameStore
    {
        public Task<GameRecord?> ByIdAsync(Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<GameRecord?> BySlugAsync(string slug, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task InsertAsync(GameRecord game, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task ExcludeAsync(Guid id, string reason, DateTimeOffset at, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task IncludeAsync(Guid id, DateTimeOffset at, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task UnlistAsync(Guid id, Guid byUserId, DateTimeOffset at, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task RelistAsync(Guid id, DateTimeOffset at, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SetStateAsync(Guid id, LifecycleState state, DateTimeOffset at, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task CorroborateAsync(
            Guid id, DateTimeOffset at, IReadOnlyList<string> signals, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task MarkReachableAsync(Guid id, DateTimeOffset at, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<string?> RenameAsync(
            Guid id, string name, string slug, DateTimeOffset at, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SetClaimedAsync(Guid id, bool isClaimed, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<GameRecord>> UnarchivedAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class NullClaimStore : IClaimStore
    {
        public Task<GameClaim?> FindAsync(Guid claimId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<GameClaim>> ForGameAsync(Guid gameId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<GameClaim>> ForUserAsync(Guid userId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<GameClaim?> FindPendingByTokenAsync(Guid gameId, string token, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task InsertAsync(GameClaim claim, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task UpdateAsync(GameClaim claim, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task RecordEventAsync(ClaimEvent claimEvent, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ClaimEvent>> EventsAsync(Guid claimId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
