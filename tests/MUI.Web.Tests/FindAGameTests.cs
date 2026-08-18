using System.Text.RegularExpressions;

using MUI.Catalog;
using MUI.Web.Api;
using MUI.Web.Components;
using MUI.Web.Components.Pages;
using MUI.Web.Fixtures;
using MUI.Web.Localization;

namespace MUI.Web.Tests;

/// <summary>
/// §9's find-a-game wizard, and the properties that make a page of counts safe to have.
/// </summary>
/// <remarks>
/// <para>
/// Two things are being guarded. The first is the old one: the page does not translate, so a control
/// whose name or value the listing's binding would refuse teaches a reader that the site does not
/// work. The second is new and larger — this page now publishes a number for a combination of
/// answers, and the whole product is that a number here came from a query rather than from
/// arithmetic. The prototype it was drawn from multiplied marginal ratios; these tests are what
/// stops that arriving by any route.
/// </para>
/// </remarks>
public class FindAGameTests
{
    private static Task<string> FindAsync(string query = "") =>
        Render.PageAsync<FindAGame>([], query);

    private static Task<FindScreen> ScreenAsync(string query = "") =>
        FindScreen.BuildAsync(new FixtureGameQueries(), query);

    /// <summary>The querystring an address on this page carries, which is empty on "ask nothing".</summary>
    private static string QueryOf(string href)
    {
        var mark = href.IndexOf('?', StringComparison.Ordinal);

        return mark < 0 ? string.Empty : href[mark..];
    }

    /// <summary>
    /// The page renders when the catalogue answers on a later turn, which is the only way it answers
    /// in production.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the whole of a live 500. <c>ComponentBase</c> calls <c>StateHasChanged</c>
    /// after starting <c>OnParametersSetAsync</c> and before awaiting it, so the page renders once
    /// with the fields it had before the load. Every other test on this page passes the fixture,
    /// whose tasks are already complete when that first render happens, so the first frame and the
    /// last are the same frame and no test had ever seen the page mid-load. Against Postgres they
    /// are different frames, and the first one dereferenced a screen that was not built yet.
    /// </para>
    /// <para>
    /// Asserting only that a heading came back, deliberately: what broke was not the markup but
    /// whether there was any. A test that checked the finished page here would have gone on passing.
    /// </para>
    /// </remarks>
    [Test]
    public async Task ThePageRendersBeforeItsScreenHasArrived()
    {
        foreach (var query in (string[])["", "?genre=Fantasy", "?nonsense=%zz"])
        {
            var html = await Render.PageAsync<FindAGame>([], query, yielding: true);

            await Assert.That(Render.Words(html)).Contains("Find a game");
        }
    }

    /// <summary>
    /// The unlock: an answered Find page is a page, not a moment between two form submissions.
    /// </summary>
    /// <remarks>
    /// It rendered every option unselected whatever the URL said, because it read no querystring at
    /// all — so an answered page could not be linked, did not survive reload, and, the reason it had
    /// to change, could not be counted: there was no server-side instant at which a set of answers
    /// existed.
    /// </remarks>
    [Test]
    public async Task AnAnsweredPageIsLinkable()
    {
        var html = await FindAsync("?genre=Fantasy");

        await Assert.That(html).Contains("aria-current=\"true\"");
        await Assert.That(Render.Words(html)).Contains("Fantasy");

        var screen = await ScreenAsync("?genre=Fantasy");
        var genre = screen.Questions.Single(q => q.Key == FacetKeys.Genre);

        await Assert.That(genre.Answer).IsNotNull();
        await Assert.That(genre.Answer!.Label).IsEqualTo("Fantasy");
    }

    /// <summary>
    /// The count is a count of games, not a product of the numbers beside the options.
    /// </summary>
    /// <remarks>
    /// Asserted against the listing the same answers produce, because that is the only definition of
    /// "right" that matters: the button says "show these N games" and the page it opens has to hold
    /// N of them. Multiplying marginal ratios — what the handoff's prototype does — gives 2 here and
    /// would give a plausible wrong number on every combination where two answers correlate.
    /// </remarks>
    [Test]
    public async Task TheCountIsTheListingTheAnswersProduce()
    {
        foreach (var query in (string[])["", "?genre=Fantasy", "?band=playersNow&language=English", "?genre=~unknown"])
        {
            GameFilterBinding.TryRead(query, out var bound, out _);

            var listing = await new FixtureGameQueries().SearchAsync(bound.Filter);
            var screen = await ScreenAsync(query);

            await Assert.That(screen.Matching)
                .IsEqualTo(listing.Games.Count)
                .Because($"{query} must count the games it would list");
        }
    }

    /// <summary>
    /// Every option's number is what choosing it returns, with the other answers still applied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The failure this prevents is the page's original one in a subtler dress: a marginal count
    /// beside an option in a form that already has three other answers is a promise about a listing
    /// nobody will ever see. Checked by following the option's own link and counting what comes
    /// back — the same route a reader takes.
    /// </para>
    /// <para>
    /// A chosen option is the one case where the number and the link point different ways, and
    /// deliberately: its link <em>clears</em> the answer, because a control a reader cannot undo is
    /// a trap, while its number is still what the answer returns — which is the count in the panel.
    /// </para>
    /// </remarks>
    [Test]
    public async Task AnOptionPromisesWhatItsOwnLinkReturns()
    {
        foreach (var query in (string[])["?band=playersNow", "?genre=Fantasy&protocol=MSSP", ""])
        {
            var screen = await ScreenAsync(query);

            foreach (var question in screen.Questions)
            {
                var options = question.Options.Concat(question.Tail);

                foreach (var option in question.Any is null ? options : options.Append(question.Any))
                {
                    if (option.IsChosen)
                    {
                        await Assert.That(option.Count)
                            .IsEqualTo(screen.Matching)
                            .Because($"{question.Text} / {option.Label} is the answer in force");
                        continue;
                    }

                    GameFilterBinding.TryRead(QueryOf(option.Href), out var bound, out _);

                    var listing = await new FixtureGameQueries().SearchAsync(bound.Filter);

                    await Assert.That(option.Count)
                        .IsEqualTo(listing.Games.Count)
                        .Because($"{question.Text} / {option.Label} promises {option.Count} at {option.Href}");
                }
            }
        }
    }

    /// <summary>The figure on the loosen button is the listing that dropping the answer returns.</summary>
    /// <remarks>
    /// The handoff picks the answer with the smallest marginal count and shows what it thinks
    /// dropping it returns. Both halves are estimates; both are replaced here by a set the query
    /// already counted, and this is what proves it.
    /// </remarks>
    [Test]
    public async Task TheLoosenButtonCarriesACountedNumber()
    {
        var screen = await ScreenAsync("?genre=Historical&language=English");

        await Assert.That(screen.Loosen).IsNotNull();

        GameFilterBinding.TryRead(QueryOf(screen.Loosen!.Href), out var bound, out _);

        var listing = await new FixtureGameQueries().SearchAsync(bound.Filter);

        await Assert.That(screen.Loosen.Count).IsEqualTo(listing.Games.Count);
        await Assert.That(screen.Loosen.Count).IsGreaterThan(screen.Matching);
    }

    /// <summary>Nothing offers a way to see nothing.</summary>
    /// <remarks>
    /// A control that cannot do what it offers is worse than one that is not there, and at zero the
    /// affordance the reader needs is the answer responsible rather than a button onto an empty
    /// listing.
    /// </remarks>
    [Test]
    public async Task AtZeroThePageOffersTheWayOutAndNotTheEmptyListing()
    {
        var screen = await ScreenAsync("?genre=Historical&language=Swedish");

        await Assert.That(screen.Matching).IsEqualTo(0);
        await Assert.That(screen.Loosen).IsNotNull();

        var html = await FindAsync("?genre=Historical&language=Swedish");

        await Assert.That(html).DoesNotContain("find-go");
        await Assert.That(Render.Words(html)).Contains("clear all answers");
    }

    /// <summary>
    /// Every control is named for a facet the binding reads, and no control invents one.
    /// </summary>
    /// <remarks>
    /// Read off the rendered markup rather than off the source, because what matters is the name
    /// that reaches the browser. A control named for a key <see cref="FacetKeys"/> does not define
    /// would be dropped on the floor by the listing, and the reader would see a filter they set
    /// having no effect at all — the silent no-op this codebase refuses everywhere else. The one
    /// form left on this page is the name field, and the answers already given ride with it as
    /// hidden inputs, so this is also what stops a typed name silently discarding the other five.
    /// </remarks>
    [Test]
    public async Task EveryControlIsNamedForAFacetTheListingReads()
    {
        var html = await FindAsync("?genre=Fantasy&band=playersNow");

        var start = html.IndexOf("<form class=\"find-name\"", StringComparison.Ordinal);

        await Assert.That(start).IsGreaterThan(-1);

        var form = html[start..html.IndexOf("</form>", start, StringComparison.Ordinal)];

        var known = typeof(FacetKeys)
            .GetFields()
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        var named = Regex.Matches(form, @"name=""(?<name>[^""]+)""")
            .Select(m => m.Groups["name"].Value)
            .Distinct()
            .ToList();

        await Assert.That(named).Contains(FacetKeys.Text);
        await Assert.That(named).Contains(FacetKeys.Genre);
        await Assert.That(named).Contains(FacetKeys.Band);

        foreach (var name in named)
        {
            await Assert.That(known.Contains(name)).IsTrue().Because($"{name} is not a facet");
        }
    }

    /// <summary>
    /// No answer is offered that the listing cannot apply.
    /// </summary>
    /// <remarks>
    /// Every link on the page is walked through the listing's own binding. A refused querystring is
    /// what a reader would meet as a 400 or, worse, as a filter that silently did nothing — and the
    /// long tail's disclosure makes this bigger than it looks, because a folded option that could
    /// not be applied would be invisible until somebody opened it.
    /// </remarks>
    [Test]
    public async Task NoAnswerIsOfferedThatTheListingCannotApply()
    {
        var screen = await ScreenAsync("?band=quiet");

        foreach (var question in screen.Questions)
        {
            foreach (var option in question.Options.Concat(question.Tail))
            {
                await Assert.That(GameFilterBinding.TryRead(QueryOf(option.Href), out _, out var error))
                    .IsTrue()
                    .Because($"{option.Label} produces {option.Href}: {error}");
            }
        }
    }

    /// <summary>
    /// The silent bucket is an option, and it keeps the word its own facet spells absence with.
    /// </summary>
    /// <remarks>
    /// The page filtered it out while the query layer answered it and the plain surface offered it,
    /// so two surfaces of one page disagreed about what could be asked. The handoff wants every one
    /// of them labelled <em>unknown</em>; they stay three sentences, because a genre nobody declared
    /// and a codebase we could not identify are a fact about the game and a fact about our reach, and
    /// that difference is the thing the site is for.
    /// </remarks>
    [Test]
    public async Task SilenceIsSelectableAndKeepsItsOwnWord()
    {
        var screen = await ScreenAsync();
        var genre = screen.Questions.Single(q => q.Key == FacetKeys.Genre);

        var silent = genre.Options.Single(o => o.Href.Contains(FacetChoice.UnknownToken, StringComparison.Ordinal));

        await Assert.That(silent.Label).IsEqualTo("not declared");
        await Assert.That(silent.Count).IsGreaterThan(0);
        await Assert.That(Render.Words(await FindAsync())).Contains("not declared");
    }

    /// <summary>
    /// Archiving removes a game from the default listing and from nothing else — including here.
    /// </summary>
    /// <remarks>
    /// The handoff asks for this question to default to <em>include them</em> so that "none of these
    /// questions are required" becomes true. Inverting it would give one reader two different result
    /// sets from two doors into one query. What was taken instead is the branch the handoff offers
    /// itself: keep the default, delete the claim, and say what is applied — which this page does by
    /// putting the number each answer returns on both of them.
    /// </remarks>
    [Test]
    public async Task TheDarkQuestionDefaultsTheSameWayTheListingDoes()
    {
        var screen = await ScreenAsync();
        var dark = screen.Questions.Single(q => q.Key == FacetKeys.Archived);

        var live = dark.Options[0];
        var all = dark.Options[1];

        await Assert.That(live.IsChosen).IsTrue();
        await Assert.That(all.IsChosen).IsFalse();

        // Both answers carry the number they return, which is how the page says a filter is applied
        // without a sentence that can go stale.
        await Assert.That(all.Count).IsGreaterThan(live.Count);

        var listing = await new FixtureGameQueries().SearchAsync(
            new GameFilter { IncludeArchived = true, IncludeAdult = false });

        await Assert.That(all.Count).IsEqualTo(listing.Games.Count);
    }

    /// <summary>TLS is one row, and the acronyms all read the same shape.</summary>
    /// <remarks>
    /// It reached the page twice — the dedicated <c>tls</c> facet and a <c>protocol=TLS</c> value
    /// falling through to the generic gloss — so one acronym named two controls with two meanings.
    /// And Razor ate the space after the conditional that wrote the name, which shipped
    /// <c>MSSP— server self-description</c> to a screen reader as one word.
    /// </remarks>
    [Test]
    public async Task TheClientQuestionNamesTlsOnceAndGlossesEveryAcronym()
    {
        var screen = await ScreenAsync();
        var client = screen.Questions.Single(q => q.Key == FacetKeys.Protocol);

        var labels = client.Options.Concat(client.Tail).Select(o => o.Label).ToList();

        await Assert.That(labels.Count(l => l.StartsWith("TLS", StringComparison.Ordinal))).IsEqualTo(1);

        foreach (var label in labels)
        {
            await Assert.That(label).Contains(" — ").Because($"{label} has no gloss");
            await Assert.That(label).DoesNotContain("measured in the handshake");
        }
    }

    /// <summary>Six questions, one answer at a time, and each one a labelled group with a heading.</summary>
    /// <remarks>
    /// The drawing asks for six <c>fieldset</c>/<c>legend</c> pairs. Our options are links, and a
    /// legend is announced when focus enters a form control in its group — with no controls to
    /// enter it would name the group for nobody. A heading is announced, is navigable by a screen
    /// reader's own heading key, and is the substitution the handoff explicitly allows.
    /// </remarks>
    [Test]
    public async Task EveryQuestionIsALabelledGroupWithAHeading()
    {
        var screen = await ScreenAsync();

        await Assert.That(screen.Questions.Count).IsEqualTo(6);

        foreach (var question in screen.Questions)
        {
            var chosen = question.Options.Concat(question.Tail).Count(o => o.IsChosen);

            await Assert.That(chosen).IsLessThanOrEqualTo(1).Because($"{question.Text} is a single choice");
        }

        var html = await FindAsync();

        await Assert.That(Regex.Matches(html, @"role=""group""").Count).IsEqualTo(6);
        await Assert.That(Regex.Matches(html, @"aria-labelledby=""find-q\d""").Count).IsEqualTo(6);
        await Assert.That(Regex.Matches(html, @"<h2 id=""find-q\d""").Count).IsEqualTo(6);
    }

    /// <summary>Every question says what kind of statement its answers are.</summary>
    /// <remarks>
    /// "What kind of game?" is the load-bearing one: it asks the derived lineage facet rather than
    /// the declared <c>family</c> string, so the option a reader picks is a grouping of ours — and
    /// rule 5 is what the badge exists to satisfy, not decoration. The alternative the handoff
    /// proposes is a raw-string-to-group map authored in the web layer, which would be a second copy
    /// of a vocabulary the catalogue owns.
    /// </remarks>
    [Test]
    public async Task TheGroupedQuestionSaysTheGroupingIsOurs()
    {
        var screen = await ScreenAsync();

        // Selected by the facet key, not by its English. The question is identified by the facet it
        // asks — that is the fact this test is about — so an edit to the copy or a translated locale
        // must not make it fail, and must not make it fail pointing at lineage evidence.
        var kind = screen.Questions.Single(q => q.Key == FacetKeys.Lineage);

        await Assert.That(kind.Text).IsEqualTo(Messages.For(Locales.SourceTag, "find.q.lineage"));
        await Assert.That(kind.Evidence).IsEqualTo(FacetEvidence.Derived);

        var html = await FindAsync();

        await Assert.That(html).Contains("evidence derived");
        await Assert.That(html).Contains("evidence measured");
        await Assert.That(html).Contains("evidence declared");
    }

    /// <summary>No rating, no score, no recommendation — here least of all.</summary>
    /// <remarks>
    /// A page that asks somebody what they want is the most natural place on this site for a
    /// "best match" to appear, and the absence of one is the thing worth guarding. Rankings are
    /// computed from measured data; a wizard that scored games against a questionnaire would be the
    /// vote this project exists without.
    /// </remarks>
    [Test]
    public async Task TheWizardRecommendsNothing()
    {
        var words = Render.Words(await FindAsync("?genre=Fantasy"));

        foreach (var forbidden in (string[])["recommend", "best match", "top pick", "rating", "score"])
        {
            await Assert.That(words.Contains(forbidden, StringComparison.OrdinalIgnoreCase)).IsFalse();
        }
    }

    /// <summary>A querystring we cannot read is refused, on both surfaces.</summary>
    /// <remarks>
    /// The listing already refuses one. This page used to ignore its URL entirely, so it could not
    /// refuse anything — and now that it reads one, answering <c>?band=nonsense</c> with the
    /// unfiltered catalogue would present our own parse failure as somebody's answer.
    /// </remarks>
    [Test]
    public async Task AQueryWeCannotReadIsRefusedRatherThanIgnored()
    {
        var screen = await ScreenAsync("?band=nonsense");

        await Assert.That(screen.Error).IsNotNull();
        await Assert.That(screen.Questions).IsEmpty();

        await Assert.That(Render.Words(await FindAsync("?band=nonsense")))
            .Contains("is not an activity band");
        await Assert.That(PlainText.RenderFind(screen)).Contains("REFUSED");
    }

    /// <summary>
    /// The plain surface is the same page: same questions, same options, same counts, same words.
    /// </summary>
    /// <remarks>
    /// It was a different page. Plain dumped ten facet groups as querystring recipes while the
    /// rendered page asked six questions, and the two disagreed about what could be asked at all —
    /// plain offered the silent bucket the rendered page hid. Both are now one construction with two
    /// renderers, and this walks the model to prove the text carries every option and every number.
    /// </remarks>
    [Test]
    public async Task ThePlainSurfaceCarriesEveryQuestionEveryOptionAndTheCount()
    {
        var screen = await ScreenAsync("?genre=Fantasy&plain=1");
        var plain = PlainText.RenderFind(screen);

        await Assert.That(plain).Contains("FIND A GAME");
        await Assert.That(plain).Contains("MATCHING ALL ANSWERS");
        await Assert.That(plain).Contains("/games?plain=1");

        foreach (var question in screen.Questions)
        {
            await Assert.That(plain).Contains(question.Text.ToUpperInvariant());

            foreach (var option in question.Options.Concat(question.Tail))
            {
                await Assert.That(plain).Contains($"{option.Label} ({option.Count})");
                await Assert.That(plain).Contains(option.Href);
            }
        }

        // Nothing here is wider than a text browser.
        foreach (var line in plain.Split('\n'))
        {
            await Assert.That(line.TrimEnd().Length)
                .IsLessThanOrEqualTo(PlainText.Columns)
                .Because($"'{line}' is wider than {PlainText.Columns} columns");
        }
    }

    /// <summary>Following a link on the plain surface stays on the plain surface.</summary>
    /// <remarks>
    /// It falls out of the construction rather than being arranged — every address on both surfaces
    /// is this page's own URL with one parameter changed — and it is worth asserting because a
    /// reader who lands back on the rendered page after answering one question has lost the surface
    /// they chose.
    /// </remarks>
    [Test]
    public async Task ThePlainSurfaceLinksStayPlain()
    {
        var screen = await ScreenAsync("?plain=1");

        foreach (var question in screen.Questions)
        {
            foreach (var option in question.Options.Concat(question.Tail))
            {
                await Assert.That(option.Href).Contains("plain=1");
            }
        }

        await Assert.That(screen.ShowHref).Contains("plain=1");

        // Including the one control that clears everything: emptying the form is about the answers,
        // and throwing a reader out of the surface they chose is not something they asked for.
        var answered = await ScreenAsync("?genre=Fantasy&plain=1");

        await Assert.That(answered.ClearHref).IsEqualTo("/find?plain=1");
        await Assert.That(answered.Answers[0].ClearHref).Contains("plain=1");
    }

    /// <summary>
    /// The long tail folds, and every option in it is still a real answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The catalogue this is drawn from has nine of twelve genres matching two games or fewer, and an
    /// option that returns one result is a piece of trivia in a list somebody has to read. The
    /// fixture is six games and cannot produce that shape, so the tail is exercised over a catalogue
    /// made for it — which is also the only way to prove the thing that matters: what is behind the
    /// disclosure is the values themselves, never a "3 more genres" bucket of its own. A submittable
    /// bucket would be Find offering a choice the listing cannot express.
    /// </para>
    /// <para>
    /// The reader's own answer is never in the tail. A selection folded out of sight is the defect
    /// the disclosure would introduce, and it is the one option they need to be able to undo.
    /// </para>
    /// </remarks>
    [Test]
    public async Task TheLongTailFoldsAndHoldsRealAnswers()
    {
        var wide = new WideCatalogue();

        var screen = await FindScreen.BuildAsync(wide, string.Empty);
        var genre = screen.Questions.Single(q => q.Key == FacetKeys.Genre);

        await Assert.That(genre.Options.Count).IsLessThan(WideCatalogue.Genres.Length);
        await Assert.That(genre.Tail).IsNotEmpty();

        foreach (var option in genre.Tail)
        {
            GameFilterBinding.TryRead(QueryOf(option.Href), out var bound, out _);

            var listing = await wide.SearchAsync(bound.Filter);

            await Assert.That(option.Count).IsEqualTo(listing.Games.Count);
            await Assert.That(option.Count).IsGreaterThan(0);
        }

        // The silent bucket stays in the open however little it weighs: it is the option that makes
        // "show me the games nobody has classified" askable at all.
        await Assert.That(genre.Options.Any(o => o.Label == "not declared")).IsTrue();

        var chosen = genre.Tail[^1];

        var answered = await FindScreen.BuildAsync(wide, QueryOf(chosen.Href));

        await Assert.That(answered.Questions.Single(q => q.Key == FacetKeys.Genre).Options
            .Any(o => o.IsChosen)).IsTrue();
    }

    /// <summary>
    /// A chosen capability outside the commonest six is still on the page, and can still be undone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client question does not go through <see cref="FindScreen"/>'s <c>Split</c> — its options
    /// are two facets welded into one control — so the rule that a reader's own answer is never
    /// folded away had to be carried across by hand, and was not. The shown list was the first six
    /// by popularity and the tail was the remainder with every chosen option filtered out, so a
    /// capability ranking seventh or lower was in neither: the answer in force was invisible and the
    /// only affordance that clears it went with it. A single-choice control that can enter a state
    /// it cannot leave is the worst shape a question on this page can have.
    /// </para>
    /// <para>
    /// Exercised over a catalogue built to rank the chosen capability last, because the six-game
    /// fixture measures one protocol and cannot produce a seventh option at all.
    /// </para>
    /// </remarks>
    [Test]
    public async Task AChosenCapabilityOutsideTheCommonestSixIsStillShownAndStillClearable()
    {
        var many = new ManyCapabilities();

        // The rarest, which is what the popularity cut pushes furthest out of sight.
        var rare = ManyCapabilities.Protocols[^1];

        var before = await FindScreen.BuildAsync(many, string.Empty);
        var offered = before.Questions.Single(q => q.Key == FacetKeys.Protocol);

        // The premise: it really is in the tail before anybody chooses it.
        await Assert.That(offered.Options.Any(o => o.Label.StartsWith(rare, StringComparison.Ordinal)))
            .IsFalse();
        await Assert.That(offered.Tail.Any(o => o.Label.StartsWith(rare, StringComparison.Ordinal)))
            .IsTrue();

        var screen = await FindScreen.BuildAsync(many, $"?{FacetKeys.Protocol}={rare}");
        var client = screen.Questions.Single(q => q.Key == FacetKeys.Protocol);

        // Shown, not folded: promoted into the open list exactly as Split promotes one elsewhere.
        var chosen = client.Options.Single(o => o.IsChosen);

        await Assert.That(chosen.Label).StartsWith(rare);
        await Assert.That(client.Tail.Any(o => o.IsChosen)).IsFalse();

        // And clearable: its own link drops the answer rather than setting it again, so the control
        // is reachable in both directions with no second affordance.
        await Assert.That(chosen.Href).DoesNotContain($"{FacetKeys.Protocol}={rare}");

        GameFilterBinding.TryRead(QueryOf(chosen.Href), out var cleared, out _);

        await Assert.That(cleared.Filter.MeasuredProtocols).IsEmpty();

        // The plain surface carries it too, or the graphical fix is half a fix (§9).
        await Assert.That(PlainText.RenderFind(screen)).Contains($"[x] {rare}");
    }

    /// <summary>
    /// A catalogue measuring more capabilities than the client question shows, ranked so the last is
    /// well outside the cut.
    /// </summary>
    private sealed class ManyCapabilities : IGameQueries
    {
        internal static readonly string[] Protocols =
        [
            "MSSP", "MCCP", "GMCP", "MXP", "MSDP", "TTYPE", "ATCP", "MSP", "EOR",
        ];

        // Nine games offer the first, one offers the last: a strict popularity order with no ties,
        // so which options fall outside the shown six is a fact rather than a sort artefact.
        private static readonly IReadOnlyList<GameFacetRow> Rows =
        [
            .. Protocols.SelectMany((protocol, rank) => Enumerable
                .Range(0, Protocols.Length - rank)
                .Select(n => Row($"{protocol}-{n}", protocol))),
        ];

        public Task<GameListing> SearchAsync(GameFilter filter, CancellationToken ct = default) =>
            Task.FromResult(FacetedSearch.Search(Rows, filter));

        public Task<IReadOnlyList<GameSummary>> ListAsync(GameFilter filter, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<GamePage?> FindAsync(string slug, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<GamePage?> FindAsync(Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<GameSummary?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<LivenessFeeds> FeedsAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<EcosystemDashboard> EcosystemAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Rankings> RankingsAsync(RankingSpan span, CancellationToken ct = default) =>
            throw new NotSupportedException();

        private static GameFacetRow Row(string slug, string protocol) => new(
            new GameSummary(
                Guid.NewGuid(), slug, slug, null, LifecycleState.Active, IsClaimed: false,
                PlayersNow: 1, Codebase: "PennMUSH", MeasuredProtocols: [protocol]),
            ActivityBand.PlayersNow,
            LastSeenBand.Day,
            TlsMeasured: false,
            Charset: "UTF-8",
            Language: "English",
            Codebase: "PennMUSH",
            Family: "TinyMUD",
            Genre: "Fantasy",
            IsAdult: false,
            Uncounted: false,
            Unreachable: false);
    }

    /// <summary>
    /// A catalogue with a long tail, which the six-game fixture has no way to have.
    /// </summary>
    /// <remarks>
    /// Built out of <see cref="GameFacetRow"/> and answered through <see cref="FacetedSearch"/> —
    /// the same arithmetic the database and the fixture both go through — so this exercises the real
    /// counting rather than a hand-written listing that could agree with nothing.
    /// </remarks>
    private sealed class WideCatalogue : IGameQueries
    {
        internal static readonly string[] Genres =
        [
            "Fantasy", "Science Fiction", "Adventure", "Horror", "Historical",
            "Social", "Multitheme", "Cyberpunk", "Western", "Noir",
        ];

        private static readonly IReadOnlyList<GameFacetRow> Rows =
        [
            .. Genres.SelectMany((genre, rank) => Enumerable
                .Range(0, Genres.Length - rank)
                .Select(n => Row($"{genre}-{n}", genre))),

            // One game nobody classified, so the silent bucket has something in it.
            Row("silent", null),
        ];

        public Task<GameListing> SearchAsync(GameFilter filter, CancellationToken ct = default) =>
            Task.FromResult(FacetedSearch.Search(Rows, filter));

        public Task<IReadOnlyList<GameSummary>> ListAsync(GameFilter filter, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<GamePage?> FindAsync(string slug, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<GamePage?> FindAsync(Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<GameSummary?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<LivenessFeeds> FeedsAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<EcosystemDashboard> EcosystemAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Rankings> RankingsAsync(RankingSpan span, CancellationToken ct = default) =>
            throw new NotSupportedException();

        private static GameFacetRow Row(string slug, string? genre) => new(
            new GameSummary(
                Guid.NewGuid(), slug, slug, null, LifecycleState.Active, IsClaimed: false,
                PlayersNow: 1, Codebase: "PennMUSH", MeasuredProtocols: ["MSSP"]),
            ActivityBand.PlayersNow,
            LastSeenBand.Day,
            TlsMeasured: false,
            Charset: "UTF-8",
            Language: "English",
            Codebase: "PennMUSH",
            Family: "TinyMUD",
            Genre: genre,
            IsAdult: false,
            Uncounted: false,
            Unreachable: false);
    }

    /// <summary>The count and the noun it agrees with are one message, never two strings.</summary>
    /// <remarks>
    /// The page these replace read "1 games" in the client question, on every protocol whose count
    /// happened to be one. English is nearly the only language that would have survived even that,
    /// and a number glued to an English fragment in English word order is exactly what the i18n
    /// review named. "0 games" is not the same fault and is correct English — zero takes the plural
    /// here and the singular in French — which is the reason the branch is a message and not an
    /// <c>if</c>.
    /// </remarks>
    [Test]
    public async Task NoCountIsGluedToAnEnglishNoun()
    {
        foreach (var query in (string[])["", "?genre=Historical", "?protocol=MSDP"])
        {
            var words = Render.Words(await FindAsync(query));

            await Assert.That(words).DoesNotContain("1 games");
            await Assert.That(words).DoesNotContain("1 listed games");
            await Assert.That(words).DoesNotContain("1 answers given");
        }
    }

    /// <summary>
    /// Nothing here needs script, which is the constraint the whole design bends around.
    /// </summary>
    /// <remarks>
    /// The drawing this page comes from is a debounced <c>aria-live</c> region recomputing a count
    /// as answers change. There is no script on this site, so the count is computed on the server
    /// and every answer is an address: a link applies on click with nothing listening, and the one
    /// form left is the name field, which a browser submits as a GET on its own. Asserted rather
    /// than assumed because "it happens to work without JS today" and "it cannot stop working
    /// without JS" are different properties, and only the second one is the design.
    /// </remarks>
    [Test]
    public async Task NothingOnThisPageNeedsScript()
    {
        foreach (var query in (string[])["?genre=Fantasy&band=playersNow", "?plain=1"])
        {
            var html = await FindAsync(query);

            await Assert.That(html).DoesNotContain("<script");
            await Assert.That(html).DoesNotContain("onclick");
            await Assert.That(html).DoesNotContain("javascript:");
        }

        var rendered = await FindAsync("?genre=Fantasy");

        // Every form this page owns is a GET, and every answer is an anchor: a POST would be a
        // change of state with no address, which is the one thing this page has never had. The
        // shared language switcher is deliberately not one of them — choosing a language writes a
        // cookie rather than asking the catalogue a question, and it belongs to the chrome.
        var forms = Regex.Matches(rendered, "<form[^>]*>")
            .Select(m => m.Value)
            .Where(f => !f.Contains("class=\"locale\"", StringComparison.Ordinal))
            .ToList();

        await Assert.That(forms).IsNotEmpty();

        foreach (var form in forms)
        {
            await Assert.That(form).Contains("method=\"get\"").Because($"{form} is not a GET");
            await Assert.That(form).Contains("action=\"/find\"").Because($"{form} leaves the page");
        }
    }

    /// <summary>Every word this page owns is in the bundle, questions included.</summary>
    /// <remarks>
    /// <para>
    /// This is the one surface written in a reader's language rather than in the catalogue's — six
    /// questions, the answer that un-asks each one, and a three-word gloss per capability — so it is
    /// the page with the most prose and the least of it reachable by a translator if it is spelled
    /// in C#. The facet <em>values</em> are deliberately not checked here: those come from
    /// <see cref="FacetWords"/>, which the listing shares, and a second vocabulary for them on this
    /// page is the drift the page's own header comment warns about.
    /// </para>
    /// <para>
    /// Asserted by identity against the bundle rather than against English text, so the test says
    /// "this string came from that id" and keeps saying it after somebody edits the English.
    /// </para>
    /// </remarks>
    [Test]
    public async Task EveryWordThisPageOwnsComesFromTheMessageBundle()
    {
        var screen = await ScreenAsync();
        var en = Locales.SourceTag;

        string[] questions =
        [
            "find.q.band", "find.q.genre", "find.q.lineage",
            "find.q.language", "find.q.client", "find.q.dark",
        ];

        await Assert.That(screen.Questions.Select(q => q.Text))
            .IsEquivalentTo(questions.Select(id => Messages.For(en, id)).ToArray());

        string[] anys = ["find.any.band", "find.any.genre", "find.any.lineage", "find.any.language"];

        await Assert.That(screen.Questions.Where(q => q.Any is not null).Select(q => q.Any!.Label))
            .Contains(Messages.For(en, "find.any.client"));

        foreach (var id in anys)
        {
            await Assert.That(screen.Questions.Select(q => q.Any?.Label))
                .Contains(Messages.For(en, id))
                .Because($"{id} is what un-asks its question");
        }

        // The acronym and its gloss are one message and not a name with three words glued to it:
        // "MSSP— server self-description" shipped from exactly that concatenation, and a language
        // that puts the gloss first has nowhere to say so if the two are joined in C#.
        var client = screen.Questions.Single(q => q.Key == FacetKeys.Protocol);

        await Assert.That(client.Options.Concat(client.Tail).Select(o => o.Label))
            .Contains(Messages.For(en, "find.protocol.mssp"));

        await Assert.That(client.Options.Concat(client.Tail).Select(o => o.Label))
            .Contains(Messages.For(en, "find.protocol.tls"));
    }

    /// <summary>
    /// One locale reaches the words and the mirror alike, because it is built into the screen.
    /// </summary>
    /// <remarks>
    /// The locale is a parameter of the construction rather than something applied to the result:
    /// the plain surface reads its question texts and its option labels straight off the screen, so
    /// a translation applied after the fact would reach the rendered page and not this one — the
    /// same split, in the same place, that this page was rebuilt to close.
    /// </remarks>
    [Test]
    public async Task TheTextMirrorSpeaksTheLocaleTheScreenWasBuiltIn()
    {
        foreach (var tag in (string[])[Locales.SourceTag, "de", "ja"])
        {
            var screen = await FindScreen.BuildAsync(new FixtureGameQueries(), "?plain=1", tag);
            var plain = PlainText.RenderFind(screen, tag);

            await Assert.That(plain).Contains(Messages.For(tag, "find.title").ToUpperInvariant());

            foreach (var question in screen.Questions)
            {
                await Assert.That(plain)
                    .Contains(question.Text.ToUpperInvariant())
                    .Because($"{tag} asks its questions in one language on both surfaces");
            }
        }
    }
}
