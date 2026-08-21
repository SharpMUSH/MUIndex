using Microsoft.AspNetCore.WebUtilities;

using MUI.Catalog;
using MUI.Web.Api;
using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>One answer a reader can give: what it is called, what choosing it returns, where it leads.</summary>
/// <remarks>
/// <see cref="Count"/> is never an estimate: it is read off a <see cref="FacetGroup"/> produced by
/// the same pass that produced the games it counts, never multiplied marginal ratios to guess an
/// intersection (wrong whenever two answers correlate, which is nearly always). <see cref="Href"/>
/// and not a form control, so an option applies with no script and no submit button.
/// </remarks>
public sealed record FindOption(string Label, int Count, bool IsChosen, string Href);

/// <summary>One question, its answers, and what dropping its answer would return.</summary>
/// <remarks>
/// <see cref="WithoutThis"/> is null where no real query produced it — an unknown figure is not
/// published, the same rule that stops an unreadable <c>WHO</c> becoming a zero.
/// </remarks>
public sealed record FindQuestion(
    string Key,
    string Text,
    FacetEvidence Evidence,
    FindOption? Any,
    IReadOnlyList<FindOption> Options,
    IReadOnlyList<FindOption> Tail,
    int? WithoutThis)
{
    /// <summary>The answer given here, or null where the reader has not answered this question.</summary>
    public FindOption? Answer =>
        Options.Concat(Tail).FirstOrDefault(o => o.IsChosen);
}

/// <summary>One given answer, drawn where the count is, with the address that clears it.</summary>
public sealed record FindChip(string Question, string Label, string ClearHref);

/// <summary>The one way out of a combination that returned almost nothing.</summary>
/// <remarks>
/// Not a diagnosis or a warning: the reader can see the count, but not which of six answers produced
/// it. <see cref="Count"/> is by construction the size of this listing with exactly this facet's own
/// selection lifted — see <see cref="FacetGroup.Total"/>.
/// </remarks>
public sealed record FindLoosen(string Question, string Label, int Count, string Href);

/// <summary>
/// Everything <c>/find</c> renders, built once from one querystring — and rendered twice.
/// </summary>
/// <remarks>
/// The rendered page and <c>?plain=1</c> are two renderers over this one object, so the two surfaces
/// cannot disagree about what can be asked. The querystring is the whole of the state — this page
/// used to be a form pointed at the listing, so an answered Find page could not be linked, survive
/// reload, or be counted at all; binding its own URL is what makes every number below possible.
/// </remarks>
public sealed class FindScreen
{
    /// <summary>How many answers a question shows before the rest go behind a disclosure.</summary>
    /// <remarks>
    /// Most genre values on the live catalogue match two games or fewer, and an option returning one
    /// result is trivia rather than a filter. Arrive commonest-first, so the tail behind the native
    /// <c>&lt;details&gt;</c> is exactly the part nobody was going to choose.
    /// </remarks>
    private const int OptionsShown = 6;

    /// <summary>Below this many results, the page offers the answer responsible.</summary>
    /// <remarks>
    /// Tuned for a catalogue of several hundred. Drawn only when some answer's removal actually
    /// returns more, so a small catalogue does not grow a control that cannot do anything.
    /// </remarks>
    private const int LoosenBelow = 10;

    private FindScreen(
        IReadOnlyList<FindQuestion> questions,
        IReadOnlyList<FindChip> answers,
        int matching,
        int listed,
        FindLoosen? loosen,
        string showHref,
        string clearHref,
        string? error)
    {
        Questions = questions;
        Answers = answers;
        Matching = matching;
        Listed = listed;
        Loosen = loosen;
        ShowHref = showHref;
        ClearHref = clearHref;
        Error = error;
    }

    /// <summary>The screen a page holds before it has asked the catalogue anything.</summary>
    /// <remarks>
    /// <c>ComponentBase</c> renders a frame from whatever the component's fields held before
    /// <c>OnParametersSetAsync</c> is awaited. Holding <c>null!</c> there only worked while the load
    /// finished synchronously — true of the fixture, false of Postgres, which is why <c>/find</c>
    /// returned 500 in production and passed every test. This renders nothing (zero counts, no
    /// questions), which every conditional on the page already treats as "say nothing yet" — the
    /// same idea as <c>GameListing.Empty</c>.
    /// </remarks>
    public static FindScreen Empty { get; } = new(
        questions: [],
        answers: [],
        matching: 0,
        listed: 0,
        loosen: null,
        showHref: "/games",
        clearHref: "/find",
        error: null);

    public IReadOnlyList<FindQuestion> Questions { get; }

    /// <summary>The answers given, in the order the questions ask them.</summary>
    public IReadOnlyList<FindChip> Answers { get; }

    /// <summary>How many games match every answer — the count, from the pass that would list them.</summary>
    public int Matching { get; }

    /// <summary>
    /// How many games the listing holds with nothing asked of it, which is what the count is "of".
    /// </summary>
    /// <remarks>
    /// The listing's own default and not the catalogue's size: archived games and games declaring
    /// adult content are out of it, so calling this "known" or "in the directory" would publish a
    /// smaller number than we hold under a word that claims otherwise.
    /// </remarks>
    public int Listed { get; }

    public FindLoosen? Loosen { get; }

    /// <summary>The listing, asked exactly what this page was asked.</summary>
    public string ShowHref { get; }

    public string ClearHref { get; }

    /// <summary>
    /// A querystring this page could not read, refused rather than ignored.
    /// </summary>
    /// <remarks>
    /// The same rule the listing follows: answering <c>?band=nonsense</c> with the unfiltered
    /// catalogue would present our own parse failure as the answer to somebody's question.
    /// </remarks>
    public string? Error { get; }

    /// <summary>
    /// Builds the page from its own URL.
    /// </summary>
    /// <remarks>
    /// Every number here costs a real query. The listing under the current answers gives the count
    /// and every choice question's option counts at once via <see cref="FacetedSearch"/> — a choice
    /// facet's values are already counted with that facet's own selection lifted, so an option's
    /// number is what clicking it returns. Three more passes are taken and no more: the listing with
    /// nothing asked (the denominator), the listing with the client answer lifted (a presence facet
    /// otherwise counts against the wrong denominator for a control that replaces its selection), and
    /// the listing with the archive switch flipped.
    /// </remarks>
    public static async Task<FindScreen> BuildAsync(
        IGameQueries queries,
        string? query,
        string? tag = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queries);

        // Built in rather than applied afterwards: the rendered page and the text mirror both read
        // their labels off this object, so a second translation step is a second place to disagree.
        var locale = tag ?? Locales.SourceTag;

        // Everything cleared except which surface the reader is on — "clear all answers" should not
        // also bounce a reader out of the plain rendering.
        var cleared = "/find" + ListingLinks.With(string.Empty, "plain", Plain(query) ? "1" : null);

        if (!GameFilterBinding.TryRead(query, out var bound, out var error))
        {
            return new FindScreen([], [], 0, 0, null, "/games", cleared, error);
        }

        var filter = bound.Filter;
        var answered = await queries.SearchAsync(filter, cancellationToken);

        // The denominator. Re-read through the binding rather than `new GameFilter()`, since a bare
        // filter counts the archive and adult declarations in — not the listing anybody lands on.
        GameFilterBinding.TryRead(string.Empty, out var unasked, out _);

        var listed = SameQuestion(filter, unasked.Filter)
            ? answered
            : await queries.SearchAsync(unasked.Filter, cancellationToken);

        // A presence facet counts against the games already on screen — right for an intersecting
        // checkbox, wrong for a single-choice option that replaces. Asked again with the client
        // answer lifted so the numbers become what clicking returns; skipped when nothing was asked.
        var loose = filter.MeasuredProtocols.Count > 0 || filter.Tls
            ? await queries.SearchAsync(
                filter with { MeasuredProtocols = [], Tls = false }, cancellationToken)
            : answered;

        // The last question's other answer. Its count is nowhere in the pass above: excluding the
        // archive happens before any facet is counted.
        var flipped = await queries.SearchAsync(
            filter with { IncludeArchived = !filter.IncludeArchived }, cancellationToken);

        var q = query ?? string.Empty;

        FindQuestion?[] asked =
        [
            Choice(answered, locale, q, FacetKeys.Band, "band", Bands),
            Choice(answered, locale, q, FacetKeys.Genre, "genre"),
            Choice(answered, locale, q, FacetKeys.Lineage, "lineage"),
            Choice(answered, locale, q, FacetKeys.Language, "language"),
            Client(loose, filter, locale, q),
            Dark(answered, flipped, filter, locale, q),
        ];

        List<FindQuestion> questions = [.. asked.OfType<FindQuestion>()];

        return new FindScreen(
            questions,
            [.. Chips(questions, filter, locale, q)],
            answered.Games.Count,
            listed.Games.Count,
            LoosenFor(questions, answered.Games.Count),
            "/games" + q,
            cleared,
            null);
    }

    /// <summary>
    /// Whether two filters ask the catalogue the same question.
    /// </summary>
    /// <remarks>
    /// Lets the commonest visit — asking nothing — take one pass rather than two. Uses the record's
    /// own equality so a filter growing a field cannot go stale here; the protocol list is compared
    /// by contents first since record equality treats two empty lists as different objects.
    /// </remarks>
    private static bool SameQuestion(GameFilter a, GameFilter b) =>
        a.MeasuredProtocols.SequenceEqual(b.MeasuredProtocols, StringComparer.OrdinalIgnoreCase)
        && (a with { MeasuredProtocols = b.MeasuredProtocols }) == b;

    /// <summary>
    /// The three bands worth offering here, spelled by the enum rather than by this file.
    /// </summary>
    /// <remarks>
    /// Archived is left out because the last question asks it directly; dark is left out because
    /// "nobody can reach" isn't what someone looking for a game to play is asking. Both stay
    /// reachable from the listing's own exhaustive panel.
    /// </remarks>
    private static readonly string[] Bands =
    [
        FacetTokens.Of(ActivityBand.PlayersNow),
        FacetTokens.Of(ActivityBand.ActiveThisWeek),
        FacetTokens.Of(ActivityBand.Quiet),
    ];

    /// <summary>
    /// One choice facet as a question.
    /// </summary>
    /// <remarks>
    /// Null where the catalogue holds no values for it — a question with one answer is not a
    /// question. Every option is read off the facet group rather than written down here, so this
    /// page cannot offer a genre no game has or claim a count the listing would not produce.
    /// <paramref name="id"/> names the question in the message bundle, since question wording is
    /// translated text and belongs where translated text lives.
    /// </remarks>
    private static FindQuestion? Choice(
        GameListing listing,
        string tag,
        string query,
        string key,
        string id,
        IReadOnlyList<string>? only = null)
    {
        if (Group(listing, key) is not { Values.Count: > 0 } group)
        {
            return null;
        }

        var values = only is null
            ? group.Values
            : [.. group.Values.Where(v => only.Contains(v.Token, StringComparer.Ordinal))];

        if (values.Count == 0)
        {
            return null;
        }

        var options = values
            .Select(v => new FindOption(
                FacetWords.Value(tag, key, v),
                v.Count,
                v.IsSelected,
                "/find" + ListingLinks.With(query, key, v.IsSelected ? null : v.Token)))
            .ToList();

        // A scale keeps its declared order, so it doesn't shuffle by size into reading as a ranking.
        // Everything else is alternatives, commonest first.
        var (shown, tail) = FacetWords.IsSingleChoice(key)
            ? (options, new List<FindOption>())
            : Split(options, values);

        return new FindQuestion(
            key,
            Messages.For(tag, "find.q." + id),
            group.Evidence,
            new FindOption(
                Messages.For(tag, "find.any." + id),
                group.Total,
                !group.IsFiltered,
                "/find" + ListingLinks.With(query, key, null)),
            shown,
            tail,

            // What dropping this answer returns, counted by FacetedSearch rather than derived from
            // the numbers above it.
            group.IsFiltered ? group.Total : null);
    }

    /// <summary>
    /// The commonest few, and the rest behind a disclosure.
    /// </summary>
    /// <remarks>
    /// The silent bucket ("nobody has classified") is never in the tail whatever it weighs — a
    /// popularity cut would otherwise fold away the value that makes silence filterable. A chosen
    /// answer is never in the tail either, since a reader's own selection scrolling out of sight
    /// would be a defect the disclosure introduced.
    /// </remarks>
    private static (List<FindOption> Shown, List<FindOption> Tail) Split(
        List<FindOption> options,
        IReadOnlyList<FacetValue> values)
    {
        var pairs = options.Zip(values).ToList();

        var named = pairs
            .Where(p => !p.Second.IsUnknown)
            .OrderByDescending(p => p.Second.Count)
            .ThenBy(p => p.First.Label, StringComparer.Ordinal)
            .ToList();

        var shown = named.Take(OptionsShown).Select(p => p.First).ToList();
        var tail = new List<FindOption>();

        foreach (var (option, _) in named.Skip(OptionsShown))
        {
            (option.IsChosen ? shown : tail).Add(option);
        }

        shown.AddRange(pairs.Where(p => p.Second.IsUnknown).Select(p => p.First));

        return (shown, tail);
    }

    /// <summary>
    /// "Anything your client needs?" — one measured capability at a time.
    /// </summary>
    /// <remarks>
    /// TLS appears once: it used to reach the page both via the dedicated <c>tls</c> facet and via a
    /// <c>protocol=TLS</c> row, so the one kept is the one whose evidence is the connection itself.
    /// Single choice rather than a set, unlike the listing's panel — the intersection of several
    /// capabilities is the combination most likely to return nothing on a catalogue this size, and
    /// nothing is lost since every one is still a checkbox on <c>/games</c>.
    /// </remarks>
    private static FindQuestion Client(GameListing loose, GameFilter filter, string tag, string query)
    {
        var chosen = filter.MeasuredProtocols.Count > 0 || filter.Tls;

        // Both keys cleared, then one set: choosing TLS has to un-choose MSSP or a single-choice
        // control would send an intersection the reader never asked for.
        var bare = ListingLinks.With(ListingLinks.With(query, FacetKeys.Protocol, null), FacetKeys.Tls, null);

        var options = new List<FindOption>();

        if (Group(loose, FacetKeys.Tls) is { Values.Count: > 0 } tls)
        {
            options.Add(new FindOption(
                Messages.For(tag, "find.protocol.tls"),
                tls.Values[0].Count,
                filter.Tls,
                "/find" + (filter.Tls ? bare : ListingLinks.With(bare, FacetKeys.Tls, "true"))));
        }

        foreach (var value in Group(loose, FacetKeys.Protocol)?.Values ?? [])
        {
            if (string.Equals(value.Token, "TLS", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var selected = filter.MeasuredProtocols.Contains(value.Token, StringComparer.OrdinalIgnoreCase);

            options.Add(new FindOption(
                Capability(tag, value.Token),
                value.Count,
                selected,
                "/find" + (selected ? bare : ListingLinks.With(bare, FacetKeys.Protocol, value.Token))));
        }

        var ordered = options.OrderByDescending(o => o.Count).ThenBy(o => o.Label, StringComparer.Ordinal).ToList();

        // Same "chosen answer promoted, never left in the tail" rule as Split, applied by hand since
        // this question doesn't go through Split. Without it, a low-ranked capability a reader had
        // chosen used to be dropped from both the shown list and the tail — visible nowhere, and
        // impossible to undo.
        var shown = ordered.Take(OptionsShown).ToList();
        var tail = new List<FindOption>();

        foreach (var option in ordered.Skip(OptionsShown))
        {
            (option.IsChosen ? shown : tail).Add(option);
        }

        return new FindQuestion(
            FacetKeys.Protocol,
            Messages.For(tag, "find.q.client"),
            FacetEvidence.Measured,
            new FindOption(
                Messages.For(tag, "find.any.client"), loose.Games.Count, !chosen, "/find" + bare),
            shown,
            tail,

            // The client answer lifted is a pass we already took, so this figure is a listing's own
            // size rather than a guess at one.
            chosen ? loose.Games.Count : null);
    }

    /// <summary>
    /// "Include games that have gone dark?" — and the reason its default is not inverted.
    /// </summary>
    /// <remarks>
    /// Defaults to excluding archived games, matching the listing's behaviour on every other door
    /// into the same query — inverting it here would give one reader two different result sets
    /// depending on which page they came through. Badged <em>derived</em> not <em>measured</em>: a
    /// game is archived because of what we measured, but the threshold and decision are ours.
    /// </remarks>
    private static FindQuestion Dark(
        GameListing answered,
        GameListing flipped,
        GameFilter filter,
        string tag,
        string query)
    {
        var live = filter.IncludeArchived ? flipped.Games.Count : answered.Games.Count;
        var all = filter.IncludeArchived ? answered.Games.Count : flipped.Games.Count;

        return new FindQuestion(
            FacetKeys.Archived,
            Messages.For(tag, "find.q.dark"),
            FacetEvidence.Derived,
            Any: null,
            [
                new FindOption(
                    Messages.For(tag, "find.dark.no"),
                    live,
                    !filter.IncludeArchived,
                    "/find" + ListingLinks.With(query, FacetKeys.Archived, null)),
                new FindOption(
                    Messages.For(tag, "find.dark.yes"),
                    all,
                    filter.IncludeArchived,
                    "/find" + ListingLinks.With(query, FacetKeys.Archived, "true")),
            ],
            [],

            // Deliberately never a loosen candidate: dropping this answer means returning to the
            // default, which narrows rather than widens.
            WithoutThis: null);
    }

    /// <summary>Every answer given, as a removable chip.</summary>
    /// <remarks>
    /// The archive switch is a chip only when the reader actually asked for it — drawn on every
    /// visit it would be an answer nobody gave.
    /// </remarks>
    private static IEnumerable<FindChip> Chips(
        IReadOnlyList<FindQuestion> questions,
        GameFilter filter,
        string tag,
        string query)
    {
        if (filter.Text is { Length: > 0 } text)
        {
            yield return new FindChip(
                Messages.For(tag, "find.name.label"),
                text,
                "/find" + ListingLinks.With(query, FacetKeys.Text, null));
        }

        foreach (var question in questions)
        {
            if (question.Key == FacetKeys.Archived)
            {
                if (filter.IncludeArchived)
                {
                    yield return new FindChip(
                        question.Text,
                        Messages.For(tag, "find.dark.chip"),
                        "/find" + ListingLinks.With(query, FacetKeys.Archived, null));
                }

                continue;
            }

            if (question.Answer is { } answer && question.Any is { } any)
            {
                yield return new FindChip(question.Text, answer.Label, any.Href);
            }
        }
    }

    /// <summary>
    /// The answer to drop, where dropping one would help.
    /// </summary>
    /// <remarks>
    /// The one that returns the most, real figure rather than the smallest-marginal-count proxy,
    /// which is wrong whenever two answers overlap.
    /// </remarks>
    private static FindLoosen? LoosenFor(IReadOnlyList<FindQuestion> questions, int matching)
    {
        if (matching >= LoosenBelow)
        {
            return null;
        }

        var best = questions
            .Where(q => q.WithoutThis is { } without && without > matching && q.Answer is not null)
            .OrderByDescending(q => q.WithoutThis)
            .ThenBy(q => q.Key, StringComparer.Ordinal)
            .FirstOrDefault();

        return best is null || best.Any is not { } any
            ? null
            : new FindLoosen(best.Text, best.Answer!.Label, best.WithoutThis!.Value, any.Href);
    }

    /// <summary>Whether this render is the text mirror, which every address it writes must stay in.</summary>
    private static bool Plain(string? query) => Truthy.Is(
        QueryHelpers.ParseQuery(query ?? string.Empty).TryGetValue("plain", out var flag)
            ? flag.ToString()
            : null);

    private static FacetGroup? Group(GameListing listing, string key) =>
        listing.Facets.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.Ordinal));

    /// <summary>
    /// A capability, named and glossed in three words.
    /// </summary>
    /// <remarks>
    /// One message per capability, not a name and gloss assembled in C# — that assembly is how the
    /// page once rendered <c>MSSP— server self-description</c> (Razor ate whitespace after a
    /// conditional block), and a language putting the gloss first has nowhere to say so if the two
    /// are glued together here. The fallback takes the token as an argument, since an unknown
    /// capability's row is where machine voice and translated voice genuinely meet.
    /// </remarks>
    private static string Capability(string tag, string token)
    {
        var known = token.ToUpperInvariant() switch
        {
            "MSSP" => "mssp",
            "MCCP" => "mccp",
            "MXP" => "mxp",
            "GMCP" => "gmcp",
            "MSDP" => "msdp",
            "CHARSET" => "charset",
            "UTF-8" => "utf8",
            "TTYPE" => "ttype",
            "ATCP" => "atcp",
            "MSP" => "msp",
            "EOR" => "eor",
            _ => null,
        };

        return known is null
            ? Messages.For(
                tag,
                "find.protocol.other",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["token"] = token.ToUpperInvariant(),
                })
            : Messages.For(tag, "find.protocol." + known);
    }
}
