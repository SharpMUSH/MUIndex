using System.Net;

using MUI.Web.Api;

namespace MUI.Web.Tests;

/// <summary>
/// A listing URL says what was asked for, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// The facet panel is a plain GET form (spec §9), and a browser submits every named control in one
/// — including the nine <c>&lt;select&gt;</c>s sitting on "any". So pressing <b>show</b> having
/// touched nothing produced
/// <c>/games?q=&amp;sort=players&amp;band=&amp;seen=&amp;charset=&amp;lineage=&amp;codebase=&amp;version=&amp;family=&amp;genre=&amp;language=</c>:
/// 117 characters to say "no filters", and that is the URL the reader then copies to somebody else.
/// <c>/find</c>'s wizard does the same through its "doesn't matter" radios, and both post here.
/// </para>
/// <para>
/// It cannot be fixed at the form — a browser with no script submits an empty control and there is
/// no markup that says otherwise — so it is fixed on arrival, by redirecting to the URL that means
/// the same thing.
/// </para>
/// <para>
/// <b>The claim that makes this safe is already in <c>GameFilterBinding</c>:</b> a blank parameter
/// is no selection. <c>Choice</c> returns null on blank, <c>TryBand</c> and <c>TryLastSeen</c> and
/// <c>TrySort</c> fall through to their defaults. So dropping the blanks provably cannot change
/// which games come back — and these are the tests that keep the two halves of that sentence
/// together.
/// </para>
/// </remarks>
public class CanonicalListingUrlTests
{
    /// <summary>The submission this whole thing is for.</summary>
    private const string UntouchedForm =
        "?q=&sort=players&band=&seen=&charset=&lineage=&codebase=&version=&family=&genre=&language=";

    [Test]
    public async Task AFormSubmittedWithNothingSetSaysNothing()
    {
        await Assert.That(ListingQuery.Canonical(UntouchedForm)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task TheOneFacetThatWasChosenSurvivesAndTheEmptyOnesGo()
    {
        await Assert.That(ListingQuery.Canonical(
            "?q=castle&sort=players&band=&codebase=PennMUSH&genre=&language="))
            .IsEqualTo("?q=castle&codebase=PennMUSH");
    }

    /// <summary>
    /// The default order is not a filter anybody set, so it does not ride along either.
    /// </summary>
    /// <remarks>
    /// The <c>&lt;select&gt;</c> always submits something, and on a form nobody touched that
    /// something is the default. An order the reader did choose stays, because that one they did.
    /// </remarks>
    [Test]
    [Arguments("?sort=players", "")]
    [Arguments("?sort=name", "?sort=name")]
    [Arguments("?q=castle&sort=players", "?q=castle")]
    public async Task TheDefaultOrderGoesAndAChosenOneStays(string query, string expected)
    {
        await Assert.That(ListingQuery.Canonical(query)).IsEqualTo(expected);
    }

    /// <summary>
    /// A repeated parameter is one answer with several values, and it is left exactly as it came.
    /// </summary>
    /// <remarks>
    /// <c>protocol</c> is repeatable and comma-separated both, because both are what people type.
    /// Rebuilding the query rather than filtering it would be the easy way to write this and would
    /// quietly decide which of those spellings survives.
    /// </remarks>
    [Test]
    public async Task RepeatedAndOrderedParametersComeThroughUnchanged()
    {
        await Assert.That(ListingQuery.Canonical("?protocol=gmcp&q=&protocol=mccp&tls=true"))
            .IsEqualTo("?protocol=gmcp&protocol=mccp&tls=true");
    }

    /// <summary>
    /// Values are never re-encoded, only dropped.
    /// </summary>
    /// <remarks>
    /// A canonicaliser that parsed and rebuilt would rewrite <c>%20</c> as <c>+</c> and redirect
    /// every URL carrying one — a redirect that changes nothing, on a URL somebody typed correctly.
    /// The output is a subsequence of the input, which is also what makes reaching a fixed point in
    /// one hop provable rather than hoped for.
    /// </remarks>
    [Test]
    public async Task AValueIsNeverRewrittenOnlyDropped()
    {
        await Assert.That(ListingQuery.Canonical("?q=a%20b&band="))
            .IsEqualTo("?q=a%20b");
    }

    /// <summary>A parameter that is only spaces is as empty as one that is nothing.</summary>
    /// <remarks>
    /// The binder reads it that way — <c>IsNullOrWhiteSpace</c>, not <c>Length is 0</c> — so leaving
    /// it in would leave a parameter in the URL that has no effect on the page.
    /// </remarks>
    [Test]
    [Arguments("?q=%20%20")]
    [Arguments("?q=+")]
    [Arguments("?q")]
    public async Task AParameterThatSelectsNothingIsDroppedHoweverItIsSpelled(string query)
    {
        await Assert.That(ListingQuery.Canonical(query)).IsEqualTo(string.Empty);
    }

    /// <summary>Anything we do not recognise, carrying a value, is left alone.</summary>
    /// <remarks>
    /// This function's business is emptiness, not vocabulary. The binder is what refuses a facet it
    /// cannot read, and it says so to the reader; a canonicaliser that silently deleted the
    /// parameter first would turn that refusal into an unfiltered catalogue presented as the answer.
    /// </remarks>
    [Test]
    public async Task AParameterWeDoNotKnowIsNotOursToDelete()
    {
        await Assert.That(ListingQuery.Canonical("?plain=1&limit=25&nonsense=x&q="))
            .IsEqualTo("?plain=1&limit=25&nonsense=x");
    }

    /// <summary>
    /// <c>?codebase=</c> is not empty in the sense the rest of this file means. It is a caller
    /// clearing the filter over a stale alias, and dropping it alone would re-apply what they
    /// cleared.
    /// </summary>
    /// <remarks>
    /// <b>The one case where "blank means unset" is false.</b> <c>GameFilterBinding</c> reads
    /// <c>codebase</c> by <em>presence</em>, so <c>?codebase=&amp;codebase-family=PennMUSH</c> means
    /// no codebase filter — the current spelling, blank, beats the old one. Drop the blank and the
    /// same URL suddenly filters by PennMUSH: a canonicalisation that changed the answer, which is
    /// the only way this whole idea could have been wrong. So the pair goes or stays together.
    /// </remarks>
    [Test]
    public async Task ClearingACodebaseOverAStaleAliasTakesTheAliasWithIt()
    {
        await Assert.That(ListingQuery.Canonical("?codebase=&codebase-family=PennMUSH"))
            .IsEqualTo(string.Empty);

        // And with nothing to clear, the alias is a filter like any other and survives untouched.
        await Assert.That(ListingQuery.Canonical("?codebase-family=PennMUSH&q="))
            .IsEqualTo("?codebase-family=PennMUSH");
    }

    /// <summary>
    /// One hop and no more. A canonical URL canonicalises to itself, which is what rules out a
    /// redirect loop rather than merely making one unlikely.
    /// </summary>
    [Test]
    [Arguments(UntouchedForm)]
    [Arguments("?q=a%20b&sort=name&protocol=gmcp&protocol=mccp&codebase=&codebase-family=Evennia")]
    [Arguments("?plain=1&q=+&sort=players&nonsense=")]
    public async Task CanonicalisingTwiceChangesNothingTheSecondTime(string query)
    {
        var once = ListingQuery.Canonical(query);

        await Assert.That(ListingQuery.Canonical(once)).IsEqualTo(once);
    }

    [Test]
    [Arguments("")]
    [Arguments("?")]
    [Arguments("?q=castle")]
    public async Task AQueryWithNothingToDropIsLeftExactlyAsItIs(string query)
    {
        await Assert.That(ListingQuery.Canonical(query)).IsEqualTo(query.TrimEnd('?'));
    }

    // ── and the same thing over HTTP ──────────────────────────────────────────────────────────

    [Test]
    public async Task TheListingSendsAFormSubmissionOnToTheUrlThatMeansTheSameThing()
    {
        await using var site = await SiteHost.StartAsync();

        var response = await site.Client.GetAsync("/games" + UntouchedForm);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Found);
        await Assert.That(response.Headers.Location?.OriginalString).IsEqualTo("/games");
    }

    /// <summary>Plain mode is a real second surface, so it travels with the redirect.</summary>
    [Test]
    public async Task TheRedirectKeepsTheSurfaceTheReaderAskedFor()
    {
        await using var site = await SiteHost.StartAsync();

        var response = await site.Client.GetAsync("/games?plain=1&q=&band=");

        await Assert.That(response.Headers.Location?.OriginalString).IsEqualTo("/games?plain=1");
    }

    /// <summary>The archive's own search box has the same empty box and gets the same answer.</summary>
    [Test]
    public async Task TheArchiveIsCanonicalisedToo()
    {
        await using var site = await SiteHost.StartAsync();

        var response = await site.Client.GetAsync("/archive?q=");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Found);
        await Assert.That(response.Headers.Location?.OriginalString).IsEqualTo("/archive");
    }

    /// <summary>A URL that already says only what was asked is answered, not bounced.</summary>
    [Test]
    [Arguments("/games")]
    [Arguments("/games?q=castle")]
    [Arguments("/games?sort=name")]
    [Arguments("/archive")]
    public async Task AUrlThatIsAlreadyCanonicalIsAnsweredWhereItStands(string url)
    {
        await using var site = await SiteHost.StartAsync();

        var response = await site.Client.GetAsync(url);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    /// <summary>
    /// The API is handed whatever URL its consumer built and answers it.
    /// </summary>
    /// <remarks>
    /// This exists for a form a browser submits. A client library following a redirect is a wasted
    /// round trip at best, and one that does not follow redirects — which is a perfectly ordinary
    /// way to write one — reads a 302 with no body as a failure. §10's surface answers for itself.
    /// </remarks>
    [Test]
    public async Task TheApiIsLeftAlone()
    {
        await using var site = await SiteHost.StartAsync();

        var response = await site.Client.GetAsync($"{ApiRoutes.Games}?band=&q=");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    /// <summary>
    /// A link checker asking whether a URL still works is answered the same way a reader is.
    /// </summary>
    [Test]
    public async Task AHeadRequestIsCanonicalisedLikeAGet()
    {
        await using var site = await SiteHost.StartAsync();

        var response = await site.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Head, "/games?q=&band="));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Found);
        await Assert.That(response.Headers.Location?.OriginalString).IsEqualTo("/games");
    }
}
