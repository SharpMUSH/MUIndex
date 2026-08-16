using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Web.Accounts;
using MUI.Web.Components.Pages;
using MUI.Web.Fixtures;

namespace MUI.Web.Tests;

/// <summary>
/// What the claim surfaces do when there is no database, and what the token is made of.
/// </summary>
/// <remarks>
/// The demo fixture has no accounts, no claims and no games anyone can prove they run. These pin the
/// half of §8 that can be asserted without one — that the surfaces are <em>absent</em> rather than
/// present and broken, and that a token could not be guessed by somebody watching a connect screen.
/// </remarks>
public class ClaimSurfaceTests
{
    /// <summary>
    /// A game page over the fixture does not invite a claim it cannot process.
    /// </summary>
    /// <remarks>
    /// Half a claim flow over invented games is a worse answer than none: an operator following it
    /// would publish a token on a real server for a listing that is not their game and does not
    /// exist. So the invitation is gated on the service being registered, which happens only when a
    /// connection string does.
    /// </remarks>
    [Test]
    public async Task AGamePageOverTheFixtureDoesNotOfferToBeClaimed()
    {
        var page = await Render.PageAsync<Game>(new() { ["Slug"] = "m-u-s-h" });

        await Assert.That(page).Contains("Everything here was measured, not entered by an owner");
        await Assert.That(page).DoesNotContain("Claim this game");
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
    /// <remarks>
    /// It is published where anyone can read it, so it need not stay secret once verified — but it
    /// must be unguessable <em>until</em> the operator publishes it, or somebody watching a connect
    /// screen could publish theirs first. Hence randomness rather than a derivation of the game.
    /// </remarks>
    [Test]
    public async Task ATokenIsPrefixedUnguessableAndFreeOfLookalikeCharacters()
    {
        var minted = Enumerable.Range(0, 200).Select(_ => ClaimToken.Mint()).ToList();

        await Assert.That(minted.Distinct().Count()).IsEqualTo(minted.Count);

        foreach (var token in minted)
        {
            await Assert.That(ClaimToken.LooksLikeOne(token)).IsTrue();
            await Assert.That(token).StartsWith(ClaimToken.Prefix);

            // 0/o, 1/l/i and u/v each reduced to one member: the token is meant to be copied, but a
            // scheme that punishes whoever transcribes it is a support mail waiting to happen.
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
    /// <remarks>
    /// §8.3: a TXT record proves control of a hostname, and MU* hosting routinely puts many unrelated
    /// games on one domain separated only by port. Adding it to this enum without a port qualifier
    /// would let a host's operator claim every game on it.
    /// </remarks>
    [Test]
    public async Task OnlyChannelsAProbeCanReadAreClaimChannels()
    {
        await Assert.That(Enum.GetNames<ClaimChannel>()).IsEquivalentTo(new[] { "Mssp", "ConnectScreen" });
    }
}
