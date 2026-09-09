namespace MUI.Web.Components;

/// <summary>
/// The front page's prose, as message ids, listed once for the two surfaces that render it.
/// </summary>
/// <remarks>
/// <para>
/// The page above this copy is figures, feeds and a search box — legible to somebody who already
/// knows what a MUCK is, and to nobody else. These four passages and the list under them are the
/// answer for the other two readers: a newcomer, and a program summarising the site from whatever
/// text comes first in the body (which, on this site, was the demo banner).
/// </para>
/// <para>
/// <b>Ids rather than sentences, in one array rather than two.</b> <c>Home.razor</c> and
/// <c>PlainText.RenderHome</c> both walk it, so the text mirror cannot fall a passage behind the
/// page it mirrors — which is exactly what a second hand-kept list would do the first time either
/// side gained a paragraph.
/// </para>
/// </remarks>
public static class HomeCopy
{
    /// <summary>One passage: a heading and the paragraph under it.</summary>
    public sealed record Passage(string Title, string Body);

    /// <summary>One entry in the start list: where it goes, what the nav calls it, what is behind it.</summary>
    public sealed record Start(string Path, string Label, string Note);

    /// <summary>
    /// The prose, in page order.
    /// </summary>
    /// <remarks>
    /// The last passage carries a second sentence, <see cref="NoVote"/> — the site's standing
    /// promise about ranking, which the rankings page already states. Reused rather than reworded
    /// here: two sentences making the same promise in different words is how a promise drifts.
    /// </remarks>
    public static IReadOnlyList<Passage> Prose { get; } =
    [
        new("home.what.title", "home.what.body"),
        new("home.measured.title", "home.measured.body"),
        new("home.states.title", "home.states.body"),
        new("home.never.title", "home.never.body"),
    ];

    /// <summary>
    /// The one sentence saying there is no vote here, shared with the rankings page.
    /// </summary>
    /// <remarks>
    /// Named as an id rather than inlined so the surface guard in <c>PlainParityTests</c> can
    /// exempt exactly this sentence — the words it bans are the ones this sentence exists to
    /// disclaim, and a bare substring scan cannot tell a promise from an affordance.
    /// </remarks>
    public const string NoVote = "rankings.noVote";

    /// <summary>
    /// The surfaces the front page sends a reader to.
    /// </summary>
    /// <remarks>
    /// Labels are the header nav's own ids, so the two cannot drift into calling one page two
    /// things; the sentence beside each is written for this list, since the header has room for one
    /// word and this does not. <c>/games/random</c> is deliberately absent — it answers differently
    /// every time, which is why robots.txt excludes it.
    /// </remarks>
    public static IReadOnlyList<Start> Starts { get; } =
    [
        new("/games", "nav.games", "home.start.games"),
        new("/find", "nav.find", "home.start.find"),
        new("/rankings", "nav.rankings", "home.start.rankings"),
        new("/ecosystem", "nav.ecosystem", "home.start.ecosystem"),
        new("/reference", "nav.reference", "home.start.reference"),
        new("/archive", "nav.archive", "home.start.archive"),
        new("/crawler", "nav.crawler", "home.start.crawler"),
        new("/about", "nav.about", "home.start.about"),
        new(Api.ApiRoutes.Documentation, "nav.api", "home.start.api"),
    ];
}
