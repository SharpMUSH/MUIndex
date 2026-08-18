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
/// Two things are asserted here that nothing else can assert: that the form has no field a submitter
/// could put a claim into, and that every sentence it can say survives in plain text. The second is
/// §9's test of itself — a refusal a text browser cannot read is a refusal nobody was given.
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
    /// <b>This is the whole feature.</b> The moment there is a name box the site is taking somebody's
    /// word for something, and every argument on the front page about measured data has a hole in it
    /// shaped like a form field. Asserted by naming the fields that must not exist, because the way
    /// this regresses is somebody adding one helpfully.
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
    /// The pseudolocale brackets and accents anything that reached a reader through
    /// <see cref="Messages"/>, so an English sentence surviving here is one typed into the page or
    /// into <see cref="SubmitCopy"/>. The address is the exception and is asserted to survive
    /// intact: it is what a stranger typed, it is machine voice, and a form that handed back a
    /// translated version of somebody's hostname would be answering about a different address.
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
    /// <remarks>
    /// The rule CLAUDE.md is emphatic about, read off the surface: our own security policy may not
    /// appear anywhere as a fact about somebody's game. The page has to say no and say why the no is
    /// ours.
    /// </remarks>
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
    /// <remarks>
    /// Echoing the address back would make this form a free scan of whatever network the crawler runs
    /// inside — submit a name, read which internal address it landed on — which is the thing §7.2's
    /// gate exists to prevent, handed back through the error message.
    /// </remarks>
    [Test]
    public async Task ARefusalNeverNamesTheAddressItResolvedTo()
    {
        var answer = SubmitCopy.Answer(SubmissionOutcome.RefusedNotRoutable, "internal.example.org 4201");

        await Assert.That(answer!.Sentence).DoesNotContain("169.254");
        await Assert.That(answer.Sentence).DoesNotContain("10.0.0");

        // And the receipt's detail, which does name it, is not a thing the copy can reach: it takes
        // an outcome and an address string and has nowhere to put one.
        await Assert.That(typeof(SubmitAnswer).GetProperties().Select(p => p.Name))
            .IsEquivalentTo(new[] { "Heading", "Sentence", "Link" });
    }

    /// <summary>
    /// A stranger is never told which of the two scope answers happened.
    /// </summary>
    /// <remarks>
    /// <b>The two sentences were an internal-DNS oracle.</b> §7.2 keeps "did not resolve" and
    /// "resolved somewhere we will not go" apart because they are two facts, and our own record has
    /// to hold them apart — but handing the distinction to whoever asked turns a public form into a
    /// scanner. Submit <c>internal.corp.example</c>: one answer means it exists on our side of a
    /// split horizon and the other means it does not, and a few hundred guesses is a map of somebody
    /// else's network drawn from outside it.
    /// </remarks>
    [Test]
    public async Task TheRefusalsAreIndistinguishableToASubmitter()
    {
        // Every refusal a submitter can be given, whatever produced it: §7.2's scope gate, an
        // ordinary DNS failure, and §11's opt-out.
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

        // Including in the URL, which is where a script would look rather than at the prose.
        await Assert.That(refusals.Select(SubmitLinks.Token).Distinct().Count()).IsEqualTo(1);

        // And the page says out loud that it is refusing to say, rather than seeming vague.
        var words = Render.Words(await PageAsync("?result=undialable&host=internal.example.org&port=4201"));

        await Assert.That(words).Contains("We deliberately do not say which");

        // The facts still exist, and are still three, where they belong: in our own record. The enum
        // keeps every member and the table keeps every word; only the surface collapses them.
        await Assert.That(Enum.GetNames<SubmissionOutcome>())
            .Contains(nameof(SubmissionOutcome.RefusedNotRoutable))
            .And.Contains(nameof(SubmissionOutcome.Unresolvable))
            .And.Contains(nameof(SubmissionOutcome.RefusedOptOut));
    }

    /// <summary>
    /// An opt-out refusal is byte-identical to a scope refusal, as a whole rendered page.
    /// </summary>
    /// <remarks>
    /// The copy being equal is the assertion above; this is the one that would catch a difference
    /// arriving anywhere else — a class on the panel, an extra link, a hidden field. An opt-out is
    /// published publicly and there is nothing to conceal about it, but a second word for a second
    /// fact is a second thing a stranger can enumerate the crawler's view with, and that vocabulary
    /// was collapsed once already to close exactly that.
    /// </remarks>
    [Test]
    public async Task AnOptOutRefusalRendersByteIdenticallyToAScopeRefusal()
    {
        var query = "?result={0}&host=quiet.example.org&port=4201";

        var viaScope = await PageAsync(string.Format(query, SubmitLinks.Token(SubmissionOutcome.RefusedNotRoutable)));
        var viaDns = await PageAsync(string.Format(query, SubmitLinks.Token(SubmissionOutcome.Unresolvable)));
        var viaOptOut = await PageAsync(string.Format(query, SubmitLinks.Token(SubmissionOutcome.RefusedOptOut)));

        await Assert.That(viaOptOut).IsEqualTo(viaScope);
        await Assert.That(viaOptOut).IsEqualTo(viaDns);

        // Equality is the whole assertion — three identical pages cannot tell anybody apart. This is
        // the separate worry: the published record itself, which the gate has in hand and which no
        // amount of equality would stop appearing on all three.
        await Assert.That(viaOptOut).DoesNotContain("opt-out=");
        await Assert.That(viaOptOut).DoesNotContain(OptOutVocabulary.DnsLabel);
    }

    /// <summary>
    /// The form says an opt-out stops a submission, because an operator needs to know that.
    /// </summary>
    /// <remarks>
    /// Saying the three refusals exist tells nobody which one they got, and leaving them unlisted
    /// would make an honest refusal read as a malfunction. This is also the sentence that answers
    /// "can somebody else put my game back on your site" — which is the question §11 exists for.
    /// </remarks>
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

        // And how to get it listed, which is the question the answer raises.
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
    /// <remarks>
    /// Hidden-until-claimed is only a state and not a trap because there is a way out of it, and a
    /// person who has just told us the address is exactly who should be handed it. Before this the
    /// answer said "we have it, nobody has claimed it" and stopped — true, and a dead end.
    /// </remarks>
    [Test]
    public async Task ASubmissionOfAHeldBackGameOffersTheWayToClaimIt()
    {
        var page = await PageAsync(
            "?result=already-listed&host=mud.example.org&port=4201&g=tidewater-nights");

        await Assert.That(page).Contains("href=\"/g/tidewater-nights/claim\"");
        await Assert.That(Render.Words(page)).Contains("If that is you, this is the way in");

        // And not its listing, which does not exist.
        await Assert.That(page).DoesNotContain("href=\"/g/tidewater-nights\"");
    }

    /// <summary>
    /// A hand-made link cannot make this page link to a game the site is hiding.
    /// </summary>
    /// <remarks>
    /// The slug travels in a querystring, so it is looked up again before it is rendered. A slug that
    /// is not public comes back null, and the answer then says we have the address without linking
    /// anywhere — which is the hidden-until-claimed filter holding on the one page that talks about
    /// it.
    /// </remarks>
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
    /// <remarks>
    /// §9's own test: if a fact cannot survive here, its rendering on the main site was decoration.
    /// The form itself is not described in the plain block because two text boxes and a button
    /// already <em>are</em> plain text — a text browser posts them — so the page keeps the real form
    /// underneath rather than a paragraph about one.
    /// </remarks>
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
    /// <remarks>
    /// Not a strict round trip, and deliberately: the two scope outcomes share one token, so one of
    /// them reads back as the other. What has to hold is that the answer is unchanged either way —
    /// which is the property the shared token exists for.
    /// </remarks>
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
    [Test]
    public async Task TheSiteLinksToTheForm()
    {
        var home = await Render.PageAsync<Home>([]);

        await Assert.That(home).Contains("/submit");
    }

    /// <summary>
    /// The claim page finds a game the listing is holding back.
    /// </summary>
    /// <remarks>
    /// <b>The exit, at the surface.</b> Claiming looked its game up with
    /// <c>IGameQueries.FindAsync</c> — the same read that had just been taught to hide submitted
    /// games — so the one page that could end the hidden state answered "No such game" for exactly
    /// the games that needed it. Claiming reads the stored row instead, because a claim is about a
    /// game and not about whether we publish it yet.
    /// </remarks>
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
