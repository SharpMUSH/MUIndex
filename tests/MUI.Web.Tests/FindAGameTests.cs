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
/// Every count on this page must come from a real query, never from multiplying marginal ratios
/// (which is what the handoff's prototype does and gives plausible wrong numbers).
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
    /// <c>ComponentBase</c> renders once before <c>OnParametersSetAsync</c> is awaited; every other
    /// test here uses the fixture, whose tasks are already complete, so the mid-load frame (a screen
    /// field dereferenced before it's built) was never exercised.
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
    /// <remarks>Previously read no querystring at all, so an answered page couldn't be linked, survive reload, or be counted server-side.</remarks>
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
    /// <remarks>Asserted against the listing the same answers produce — the only definition of "right" that matters.</remarks>
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
    /// <remarks>Checked by following the option's own link and counting what comes back. A chosen option's link deliberately <em>clears</em> the answer, while its number stays what the answer returns.</remarks>
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

    /// <summary>The figure on the loosen button is the listing that dropping the answer returns, not an estimate.</summary>
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

    /// <summary>
    /// A typed name narrows the count and hands the same name on to the listing.
    /// </summary>
    /// <remarks>
    /// The free-text field was the one answer no test followed all the way through, which made
    /// <c>/find?q=…</c> look broken to anyone who read the page expecting the games themselves: this
    /// wizard answers in a count and a way through to <c>/games</c>, and never lists a game itself.
    /// Both halves are asserted here — the count is really filtered, and <c>ShowHref</c> carries the
    /// name on, so the number on the button is the number the listing then renders.
    /// </remarks>
    [Test]
    public async Task ATypedNameNarrowsTheCountAndReachesTheListing()
    {
        var screen = await ScreenAsync($"?{FacetKeys.Text}=Aardwolf");

        await Assert.That(screen.Matching).IsGreaterThan(0);
        await Assert.That(screen.Matching).IsLessThan(screen.Listed);

        // The button promises a listing of exactly this size, so it has to ask what this page asked.
        GameFilterBinding.TryRead(QueryOf(screen.ShowHref), out var bound, out _);

        var listing = await new FixtureGameQueries().SearchAsync(bound.Filter);

        await Assert.That(listing.Games.Count).IsEqualTo(screen.Matching);
        await Assert.That(listing.Games.Select(g => g.Name)).Contains("Aardwolf MUD");

        var html = await FindAsync($"?{FacetKeys.Text}=Aardwolf");

        await Assert.That(html).Contains("find-go");
        await Assert.That(html).Contains($"/games?{FacetKeys.Text}=Aardwolf");
    }

    /// <summary>A name nothing is called returns nothing, rather than the unfiltered catalogue.</summary>
    /// <remarks>The failure mode a dropped text filter would produce is 791 games, not zero — assert the zero.</remarks>
    [Test]
    public async Task ANameNothingIsCalledReturnsNothing()
    {
        var screen = await ScreenAsync($"?{FacetKeys.Text}=zzzznotagame");

        await Assert.That(screen.Matching).IsEqualTo(0);

        var html = await FindAsync($"?{FacetKeys.Text}=zzzznotagame");

        await Assert.That(html).DoesNotContain("find-go");
    }

    /// <summary>Nothing offers a way to see nothing — at zero the affordance is the answer responsible, not a button onto an empty listing.</summary>
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
    /// <remarks>Read off the rendered markup, not the source — a control named for a key <see cref="FacetKeys"/> doesn't define would be silently dropped by the listing.</remarks>
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
    /// <remarks>Every link is walked through the listing's own binding — a refused querystring would meet a reader as a 400 or a silent no-op.</remarks>
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
    /// <remarks>A genre nobody declared and a codebase we couldn't identify stay distinct words rather than one "unknown" — the difference is the thing the site is for.</remarks>
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
    /// <remarks>Keeps the listing's default (excluded) rather than inverting it, which would give one reader two different result sets from two doors into one query.</remarks>
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

    /// <summary>TLS is one row, and every acronym carries its gloss.</summary>
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

    /// <summary>
    /// Six questions, one answer at a time, each a labelled group with a heading — a heading rather
    /// than a <c>fieldset</c>/<c>legend</c>, since our options are links and a legend needs a form
    /// control in its group to be announced.
    /// </summary>
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

    /// <summary>
    /// Every question says what kind of statement its answers are. "What kind of game?" asks the
    /// derived lineage facet, not the declared <c>family</c> string — rule 5.
    /// </summary>
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

    /// <summary>No rating, no score, no recommendation — here least of all, since rankings are computed from measured data, never a questionnaire score.</summary>
    [Test]
    public async Task TheWizardRecommendsNothing()
    {
        var words = Render.Words(await FindAsync("?genre=Fantasy"));

        foreach (var forbidden in (string[])["recommend", "best match", "top pick", "rating", "score"])
        {
            await Assert.That(words.Contains(forbidden, StringComparison.OrdinalIgnoreCase)).IsFalse();
        }
    }

    /// <summary>A querystring we cannot read is refused, on both surfaces — answering with the unfiltered catalogue would present our own parse failure as somebody's answer.</summary>
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
    /// <remarks>Both surfaces are now one construction with two renderers; this walks the model to prove the text carries every option and every number.</remarks>
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
    /// Exercised over a built catalogue (the fixture can't produce a long tail) to prove what's behind
    /// the disclosure is real values, never a "3 more" bucket the listing can't express. The reader's
    /// own answer is never in the tail — folded out of sight it would be undoable.
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
    /// The client question doesn't go through <see cref="FindScreen"/>'s <c>Split</c> (its options
    /// weld two facets into one control), so "a chosen answer is never folded away" had to be carried
    /// across by hand. Exercised over a catalogue built to rank the chosen capability last.
    /// </remarks>
    [Test]
    public async Task AChosenCapabilityOutsideTheCommonestSixIsStillShownAndStillClearable()
    {
        var many = new ManyCapabilities();

        // The rarest, which is what the popularity cut pushes furthest out of sight.
        var rare = ManyCapabilities.Protocols[^1];

        var before = await FindScreen.BuildAsync(many, string.Empty);
        var offered = before.Questions.Single(q => q.Key == FacetKeys.Protocol);

        await Assert.That(offered.Options.Any(o => o.Label.StartsWith(rare, StringComparison.Ordinal)))
            .IsFalse();
        await Assert.That(offered.Tail.Any(o => o.Label.StartsWith(rare, StringComparison.Ordinal)))
            .IsTrue();

        var screen = await FindScreen.BuildAsync(many, $"?{FacetKeys.Protocol}={rare}");
        var client = screen.Questions.Single(q => q.Key == FacetKeys.Protocol);

        var chosen = client.Options.Single(o => o.IsChosen);

        await Assert.That(chosen.Label).StartsWith(rare);
        await Assert.That(client.Tail.Any(o => o.IsChosen)).IsFalse();

        // Clearable: its own link drops the answer rather than setting it again.
        await Assert.That(chosen.Href).DoesNotContain($"{FacetKeys.Protocol}={rare}");

        GameFilterBinding.TryRead(QueryOf(chosen.Href), out var cleared, out _);

        await Assert.That(cleared.Filter.MeasuredProtocols).IsEmpty();

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
    /// <remarks>Answered through <see cref="FacetedSearch"/>, the same arithmetic the database and fixture use.</remarks>
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

    /// <summary>
    /// The count and the noun it agrees with are one ICU message, never a number glued to an English
    /// fragment (which used to read "1 games") — plural rules vary by locale, so the branch can't be
    /// an <c>if</c>.
    /// </summary>
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
    /// <remarks>No script anywhere on this site — every answer is a plain link, and the one form left is a GET the browser submits on its own.</remarks>
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

        // The shared language switcher is excluded — it writes a cookie, not a catalogue question.
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
    /// Facet <em>values</em> are deliberately not checked here — those come from
    /// <see cref="FacetWords"/>, shared with the listing. Asserted by identity against the bundle
    /// rather than English text, so this keeps holding after the English copy is edited.
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

        // The acronym and its gloss are one message, not concatenated in C# — a language that puts
        // the gloss first needs to be able to.
        var client = screen.Questions.Single(q => q.Key == FacetKeys.Protocol);

        await Assert.That(client.Options.Concat(client.Tail).Select(o => o.Label))
            .Contains(Messages.For(en, "find.protocol.mssp"));

        await Assert.That(client.Options.Concat(client.Tail).Select(o => o.Label))
            .Contains(Messages.For(en, "find.protocol.tls"));
    }

    /// <summary>
    /// One locale reaches the words and the mirror alike: the locale is a parameter of the screen's
    /// construction, not applied after the fact, so the plain surface can't fall out of sync with the
    /// rendered page.
    /// </summary>
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
