using MUI.Discovery;
using MUI.Web.Components;
using MUI.Web.Components.Pages;

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
    private static Task<string> PageAsync(string query = "", bool measured = true) =>
        Render.PageAsync<Submit>([], query, measured);

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

        await Assert.That(page).Contains("Nothing about it appears on this site until somebody proves they run it");
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
        var words = Render.Words(await PageAsync("?result=refused&host=internal.example.org&port=4201"));

        await Assert.That(words).Contains("We will not dial that");
        await Assert.That(words).Contains("This is our policy about our own socket");
        await Assert.That(words).Contains("recorded nowhere as though it did");

        // Never the word people reach for, and never a claim about the far end.
        await Assert.That(words).DoesNotContain("down");
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
        var answer = SubmitCopy.Answer(SubmissionOutcome.RefusedNotRoutable, "internal.example.org 4201", null);

        await Assert.That(answer!.Sentence).DoesNotContain("169.254");
        await Assert.That(answer.Sentence).DoesNotContain("10.0.0");

        // And the receipt's detail, which does name it, is not a thing the copy can reach: it takes
        // an outcome and an address string and has nowhere to put one.
        await Assert.That(typeof(SubmitAnswer).GetProperties().Select(p => p.Name))
            .IsEquivalentTo(new[] { "Heading", "Sentence", "GameSlug" });
    }

    /// <summary>"Could not resolve" and "will not dial" read as two different things.</summary>
    [Test]
    public async Task DnsFailureAndRefusalAreTwoDifferentSentences()
    {
        var refused = Render.Words(await PageAsync("?result=refused&host=a.example.org&port=4201"));
        var missing = Render.Words(await PageAsync("?result=unresolvable&host=a.example.org&port=4201"));

        await Assert.That(missing).Contains("a fact about the world rather than a decision of ours");
        await Assert.That(refused).DoesNotContain("a fact about the world");
    }

    /// <summary>An accepted submission says what will happen and what will not.</summary>
    [Test]
    public async Task AnAcceptedSubmissionSaysItWillNotBeListedYet()
    {
        var words = Render.Words(await PageAsync("?result=accepted&host=mud.example.org&port=4201"));

        await Assert.That(words).Contains("mud.example.org 4201");
        await Assert.That(words).Contains("will be dialled on the next crawl cycle");
        await Assert.That(words).Contains("until somebody claims it");
    }

    /// <summary>
    /// A duplicate links to the game it collapsed onto, when there is a public one to link to.
    /// </summary>
    [Test]
    public async Task ADuplicateLinksToTheGameItCollapsedOnto()
    {
        var words = await PageAsync("?result=already-listed&host=mud.example.org&port=4201&g=m-u-s-h");

        await Assert.That(words).Contains("/g/m-u-s-h");
        await Assert.That(Render.Words(words)).Contains("We already have that one");
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
    public async Task ASlugThatIsNotPublicIsNotLinked()
    {
        var page = await PageAsync("?result=already-listed&host=mud.example.org&port=4201&g=not-a-game");

        await Assert.That(page).DoesNotContain("/g/not-a-game");
        await Assert.That(Render.Words(page)).Contains("is already known to us");
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
            var expected = SubmitCopy.Answer(outcome, "mud.example.org 4201", null)!;
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
                SubmitCopy.Answer(outcome, "mud.example.org 4201", "m-u-s-h"), hasCatalogue: true);

            foreach (var line in text.Split('\n'))
            {
                await Assert.That(line.TrimEnd().Length).IsLessThanOrEqualTo(PlainText.Columns);
            }
        }
    }

    /// <summary>Every outcome has words, and every token round-trips.</summary>
    [Test]
    public async Task EveryOutcomeHasSomethingToSayAndAWordToSayItWith()
    {
        foreach (var outcome in Enum.GetValues<SubmissionOutcome>())
        {
            await Assert.That(SubmitCopy.Answer(outcome, "mud.example.org 4201", null)).IsNotNull();
            await Assert.That(SubmitLinks.Outcome(SubmitLinks.Token(outcome))).IsEqualTo(outcome);
        }
    }

    /// <summary>The site says where the form is, or nobody finds it.</summary>
    [Test]
    public async Task TheSiteLinksToTheForm()
    {
        var home = await Render.PageAsync<Home>([]);

        await Assert.That(home).Contains("/submit");
    }
}
