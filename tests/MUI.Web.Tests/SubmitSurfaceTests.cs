using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Discovery;
using MUI.Web.Components;
using MUI.Web.Components.Pages;
using MUI.Web.Localization;

namespace MUI.Web.Tests;

/// <summary>
/// The submission form (spec §7.6, §9).
/// </summary>
/// <remarks>
/// Asserts two things nothing else does: the form has no field a submitter could put a claim into,
/// and every sentence it can say survives in plain text.
/// </remarks>
public class SubmitSurfaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A game the site publishes.</summary>
    private static readonly GameRecord Listed = new(
        Guid.CreateVersion7(), "m-u-s-h", "M*U*S*H", null, LifecycleState.Active, false, Now);

    /// <summary>A game somebody submitted and nobody has claimed — the hidden state.</summary>
    private static readonly GameRecord Hidden = new(
        Guid.CreateVersion7(), "tidewater-nights", "Tidewater Nights", null, LifecycleState.Active,
        false, Now, SubmittedAt: Now);

    private static Task<string> PageAsync(string query = "", bool measured = true) =>
        Render.PageAsync<Submit>([], query, measured, [Listed, Hidden]);

    /// <summary>
    /// A host, a port, and nothing a submitter could assert.
    /// </summary>
    /// <remarks>
    /// <b>This is the whole feature.</b> The moment there's a name box, the site is taking somebody's
    /// word for something. Asserted by naming the fields that must not exist, since this regresses
    /// via someone adding one helpfully.
    /// </remarks>
    [Test]
    public async Task TheFormTakesAnAddressAndNothingElse()
    {
        var page = await PageAsync();

        await Assert.That(page).Contains("name=\"host\"");
        await Assert.That(page).Contains("name=\"port\"");

        foreach (var field in new[] { "name", "title", "description", "tagline", "codebase", "genre", "players", "website", "email" })
        {
            await Assert.That(page).DoesNotContain($"name=\"{field}\"")
                .Because($"a {field} box would be a fact somebody asserted rather than one we measured");
        }
    }

    /// <summary>The form is a real POST with a token, and needs no script.</summary>
    [Test]
    public async Task TheFormPostsWithATokenAndNoScript()
    {
        var page = await PageAsync();

        await Assert.That(page).Contains("method=\"post\"");
        await Assert.That(page).Contains("action=\"/submit\"");
        await Assert.That(page).Contains("__RequestVerificationToken");
        await Assert.That(page).DoesNotContain("<script");
    }

    /// <summary>
    /// The page says a submission does not put a listing up, before anybody submits one.
    /// </summary>
    [Test]
    public async Task TheFormSaysASubmissionIsNotAListing()
    {
        var page = Render.Words(await PageAsync());

        await Assert.That(page).Contains("Nothing appears on the site until somebody proves they run it");
    }

    /// <summary>
    /// Every word this form says comes out of the message bundle.
    /// </summary>
    /// <remarks>
    /// The pseudolocale marks anything that passed through <see cref="Messages"/>; legible English
    /// here was hard-coded. The address is asserted to survive intact — it's what a stranger typed,
    /// and a "translated" hostname would answer about a different address.
    /// </remarks>
    [Test]
    public async Task EveryAnswerComesFromTheBundleAndTheAddressComesBackUntouched()
    {
        foreach (var outcome in Enum.GetValues<SubmissionOutcome>())
        {
            var english = SubmitCopy.Answer(outcome, "mud.example.org 4201")!;
            var pseudo = PlainText.RenderSubmit(
                SubmitCopy.Answer(outcome, "mud.example.org 4201", null, "qps-ploc"),
                hasCatalogue: true,
                "qps-ploc");

            await Assert.That(pseudo).Contains("⟦");
            await Assert.That(Render.Words(pseudo))
                .DoesNotContain(Render.Words(english.Heading))
                .Because($"the {outcome} heading never went through the message pipeline");
        }

        // The lede, the five points and the address, in one render.
        var form = Render.Words(PlainText.RenderSubmit(
            SubmitCopy.Answer(SubmissionOutcome.Accepted, "mud.example.org 4201", null, "qps-ploc"),
            hasCatalogue: true,
            "qps-ploc"));

        await Assert.That(form).Contains("mud.example.org 4201");
        await Assert.That(form).DoesNotContain(Render.Words(SubmitCopy.Lede()));

        foreach (var point in SubmitCopy.Points())
        {
            await Assert.That(form).DoesNotContain(Render.Words(point));
        }
    }

    /// <summary>Over the demo fixture the form is absent rather than present and doing nothing.</summary>
    [Test]
    public async Task OverTheFixtureThereIsNoFormAtAll()
    {
        var page = await PageAsync(measured: false);

        await Assert.That(page).DoesNotContain("method=\"post\"");
        await Assert.That(Render.Words(page)).Contains("running on the demo fixture");
    }

    /// <summary>
    /// A refusal is a sentence, and it says what we decided rather than what the game is.
    /// </summary>
    /// <remarks>Rule 5: our own security policy must never appear as a fact about somebody's game — the page has to say no and say the no is ours.</remarks>
    [Test]
    public async Task ARefusalIsOursAndSaysSo()
    {
        var words = Render.Words(await PageAsync("?result=undialable&host=internal.example.org&port=4201"));

        await Assert.That(words).Contains("We cannot dial that");
        await Assert.That(words).Contains("the decision was ours and it is filed as ours");

        // Never a claim about the far end.
        await Assert.That(words).DoesNotContain("offline");
    }

    /// <summary>
    /// A refusal never names what the host resolved to.
    /// </summary>
    /// <remarks>Echoing the resolved address back would make this form a free network scan — submit a name, read where it landed — which is what §7.2's gate exists to prevent.</remarks>
    [Test]
    public async Task ARefusalNeverNamesTheAddressItResolvedTo()
    {
        var answer = SubmitCopy.Answer(SubmissionOutcome.RefusedNotRoutable, "internal.example.org 4201");

        await Assert.That(answer!.Sentence).DoesNotContain("169.254");
        await Assert.That(answer.Sentence).DoesNotContain("10.0.0");

        await Assert.That(typeof(SubmitAnswer).GetProperties().Select(p => p.Name))
            .IsEquivalentTo(new[] { "Heading", "Sentence", "Link" });
    }

    /// <summary>
    /// A stranger is never told which of the two scope answers happened.
    /// </summary>
    /// <remarks>
    /// <b>The two sentences were an internal-DNS oracle.</b> §7.2 keeps "did not resolve" and
    /// "resolved somewhere we will not go" apart internally, but exposing the distinction turns a
    /// public form into a scanner: a few hundred submitted guesses map somebody else's split-horizon
    /// DNS from outside it.
    /// </remarks>
    [Test]
    public async Task TheRefusalsAreIndistinguishableToASubmitter()
    {
        // Every refusal a submitter can be given: §7.2's scope gate, an ordinary DNS failure, §11's opt-out.
        SubmissionOutcome[] refusals =
        [
            SubmissionOutcome.RefusedNotRoutable,
            SubmissionOutcome.Unresolvable,
            SubmissionOutcome.RefusedOptOut,
        ];

        var answers = refusals
            .Select(o => SubmitCopy.Answer(o, "internal.example.org 4201"))
            .ToList();

        await Assert.That(answers.Distinct().Count()).IsEqualTo(1);

        // Including in the URL, where a script would look rather than at the prose.
        await Assert.That(refusals.Select(SubmitLinks.Token).Distinct().Count()).IsEqualTo(1);

        var words = Render.Words(await PageAsync("?result=undialable&host=internal.example.org&port=4201"));

        await Assert.That(words).Contains("We deliberately do not say which");

        // The facts still exist, in our own record — only the surface collapses them.
        await Assert.That(Enum.GetNames<SubmissionOutcome>())
            .Contains(nameof(SubmissionOutcome.RefusedNotRoutable))
            .And.Contains(nameof(SubmissionOutcome.Unresolvable))
            .And.Contains(nameof(SubmissionOutcome.RefusedOptOut));
    }

    /// <summary>
    /// An opt-out refusal is byte-identical to a scope refusal, as a whole rendered page.
    /// </summary>
    /// <remarks>Catches a difference arriving anywhere else — a panel class, an extra link, a hidden field — not just in the copy asserted above.</remarks>
    [Test]
    public async Task AnOptOutRefusalRendersByteIdenticallyToAScopeRefusal()
    {
        var query = "?result={0}&host=quiet.example.org&port=4201";

        var viaScope = await PageAsync(string.Format(query, SubmitLinks.Token(SubmissionOutcome.RefusedNotRoutable)));
        var viaDns = await PageAsync(string.Format(query, SubmitLinks.Token(SubmissionOutcome.Unresolvable)));
        var viaOptOut = await PageAsync(string.Format(query, SubmitLinks.Token(SubmissionOutcome.RefusedOptOut)));

        await Assert.That(viaOptOut).IsEqualTo(viaScope);
        await Assert.That(viaOptOut).IsEqualTo(viaDns);

        await Assert.That(viaOptOut).DoesNotContain("opt-out=");
        await Assert.That(viaOptOut).DoesNotContain(OptOutVocabulary.DnsLabel);
    }

    /// <summary>
    /// The form says an opt-out stops a submission, because an operator needs to know that.
    /// </summary>
    /// <remarks>Answers "can somebody else put my game back on your site" — the question §11 exists for.</remarks>
    [Test]
    public async Task TheFormSaysAnOptOutStopsASubmission()
    {
        var words = Render.Words(await PageAsync());

        await Assert.That(words).Contains("A stranger cannot put your game back on this site");
    }

    /// <summary>An accepted submission says what will happen and what will not.</summary>
    [Test]
    public async Task AnAcceptedSubmissionSaysItWillNotBeListedYet()
    {
        var words = Render.Words(await PageAsync("?result=accepted&host=mud.example.org&port=4201"));

        await Assert.That(words).Contains("mud.example.org 4201");
        await Assert.That(words).Contains("will be dialled on the next crawl cycle");

        await Assert.That(words).Contains("It appears here once somebody proves they run it");
        await Assert.That(words).Contains("come back to this form with the same address");
    }

    /// <summary>
    /// A duplicate links to the game it collapsed onto, when there is a public one to link to.
    /// </summary>
    [Test]
    public async Task ADuplicateLinksToTheGameItCollapsedOnto()
    {
        var page = await PageAsync("?result=already-listed&host=mud.example.org&port=4201&g=m-u-s-h");

        await Assert.That(page).Contains("href=\"/g/m-u-s-h\"");
        await Assert.That(Render.Words(page)).Contains("We already have that one");
    }

    /// <summary>
    /// A hidden game's answer offers the claim page, which is the only exit it has.
    /// </summary>
    /// <remarks>Hidden-until-claimed is a state and not a trap only because there's a way out of it, handed to exactly the person who just told us the address.</remarks>
    [Test]
    public async Task ASubmissionOfAHeldBackGameOffersTheWayToClaimIt()
    {
        var page = await PageAsync(
            "?result=already-listed&host=mud.example.org&port=4201&g=tidewater-nights");

        await Assert.That(page).Contains("href=\"/g/tidewater-nights/claim\"");
        await Assert.That(Render.Words(page)).Contains("If that is you, this is the way in");

        await Assert.That(page).DoesNotContain("href=\"/g/tidewater-nights\"");
    }

    /// <summary>
    /// A hand-made link cannot make this page link to a game the site is hiding.
    /// </summary>
    /// <remarks>The slug travels in a querystring and is looked up again before rendering; a non-public slug comes back null and the page links nowhere.</remarks>
    [Test]
    public async Task ASlugThatNamesNoGameIsNotLinked()
    {
        var page = await PageAsync("?result=already-listed&host=mud.example.org&port=4201&g=not-a-game");

        await Assert.That(page).DoesNotContain("/g/not-a-game");
        await Assert.That(Render.Words(page)).Contains("Nothing was created and nothing was changed");
    }

    /// <summary>Anything that is not one of the outcome words says nothing at all.</summary>
    [Test]
    public async Task AnUnknownResultIsNotAnAnswer()
    {
        var page = Render.Words(await PageAsync("?result=whatever&host=mud.example.org&port=4201"));

        await Assert.That(page).DoesNotContain("In the registry");
        await Assert.That(page).DoesNotContain("We will not dial that");
    }

    /// <summary>
    /// Every sentence the form can say survives in plain text, and the form survives with it.
    /// </summary>
    /// <remarks>The form isn't described in the plain block because two text boxes and a button already are plain text; the page keeps the real form rather than a paragraph about one.</remarks>
    [Test]
    public async Task PlainModeCarriesEveryAnswerAndKeepsTheForm()
    {
        foreach (var outcome in Enum.GetValues<SubmissionOutcome>())
        {
            var expected = SubmitCopy.Answer(outcome, "mud.example.org 4201")!;
            var page = await PageAsync(
                $"?plain=1&result={SubmitLinks.Token(outcome)}&host=mud.example.org&port=4201");

            var words = Render.Words(page);

            await Assert.That(words).Contains(Render.Words(expected.Heading));
            await Assert.That(words).Contains(Render.Words(expected.Sentence));
            await Assert.That(page).Contains("method=\"post\"");
        }
    }

    /// <summary>Nothing the plain surface writes is wider than a text browser.</summary>
    [Test]
    public async Task PlainTextStaysInsideEightyColumns()
    {
        foreach (var outcome in Enum.GetValues<SubmissionOutcome>())
        {
            var text = PlainText.RenderSubmit(
                SubmitCopy.Answer(outcome, "mud.example.org 4201", SubmitLink.Claim("tidewater-nights")),
                hasCatalogue: true);

            foreach (var line in text.Split('\n'))
            {
                await Assert.That(line.TrimEnd().Length).IsLessThanOrEqualTo(PlainText.Columns);
            }
        }
    }

    /// <summary>
    /// Every outcome has words, and every token reads back as something the surface treats alike.
    /// </summary>
    /// <remarks>Not a strict round trip: the two scope outcomes deliberately share one token, so one reads back as the other. What must hold is that the answer is unchanged either way.</remarks>
    [Test]
    public async Task EveryOutcomeHasSomethingToSayAndAWordToSayItWith()
    {
        foreach (var outcome in Enum.GetValues<SubmissionOutcome>())
        {
            var answer = SubmitCopy.Answer(outcome, "mud.example.org 4201");

            await Assert.That(answer).IsNotNull();

            var roundTripped = SubmitLinks.Outcome(SubmitLinks.Token(outcome));

            await Assert.That(roundTripped).IsNotNull();
            await Assert.That(SubmitCopy.Answer(roundTripped, "mud.example.org 4201")).IsEqualTo(answer);
        }
    }

    /// <summary>The site says where the form is, or nobody finds it.</summary>
    /// <remarks>Asked of the bar rather than the home page — the link lives there now, not in a footer.</remarks>
    [Test]
    public async Task TheSiteLinksToTheForm()
    {
        await using var site = await SiteHost.StartAsync();

        await Assert.That(await site.Client.GetStringAsync("/")).Contains("href=\"/submit\"");
    }

    /// <summary>
    /// The claim page finds a game the listing is holding back.
    /// </summary>
    /// <remarks>Claiming reads the stored row directly rather than the listing's <c>IGameQueries.FindAsync</c>, which is taught to hide submitted games — a claim is about a game, not about whether we publish it yet.</remarks>
    [Test]
    public async Task TheClaimPageFindsAGameTheListingIsHoldingBack()
    {
        var page = await Render.PageAsync<Claim>(
            new() { ["Slug"] = "tidewater-nights" }, string.Empty, measured: true, games: [Listed, Hidden]);

        await Assert.That(page).DoesNotContain("No such game");
        await Assert.That(Render.Words(page)).Contains("Claim Tidewater Nights");
    }

    /// <summary>And still refuses a slug that names nothing.</summary>
    [Test]
    public async Task TheClaimPageStillRefusesASlugThatNamesNothing()
    {
        var page = await Render.PageAsync<Claim>(
            new() { ["Slug"] = "not-a-game" }, string.Empty, measured: true, games: [Listed, Hidden]);

        await Assert.That(page).Contains("No such game");
    }
}
