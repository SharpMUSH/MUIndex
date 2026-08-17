using Microsoft.AspNetCore.WebUtilities;

using MUI.Catalog;
using MUI.Web.Api;
using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>One answer a reader can give: what it is called, what choosing it returns, where it leads.</summary>
/// <remarks>
/// <para>
/// <see cref="Count"/> is never an estimate and never arithmetic of ours. Every one of them is read
/// off a <see cref="FacetGroup"/> produced by the same pass that produced the games it counts, so a
/// chip cannot promise a listing the query will not deliver. The prototype this page was drawn from
/// multiplied marginal ratios to guess an intersection; that number is wrong whenever two answers
/// correlate, which is nearly always, and on the one site whose product is that it does not
/// fabricate it would have been the worst thing on the page.
/// </para>
/// <para>
/// <see cref="Href"/> and not a form control. An option that only applies when a submit button is
/// found is the defect the listing's own facet panel was rewritten to remove — and a link needs no
/// script, is a real keyboard target, opens in a new tab, and leaves the whole of the state in the
/// URL where this page has always said it lives.
/// </para>
/// </remarks>
public sealed record FindOption(string Label, int Count, bool IsChosen, string Href);

/// <summary>One question, its answers, and what dropping its answer would return.</summary>
/// <remarks>
/// <see cref="WithoutThis"/> is null where no real query produced it. It is the figure the
/// loosen button carries, and an unknown one is not published — the same rule that stops an
/// unreadable <c>WHO</c> becoming a zero.
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
/// Not a diagnosis and not a warning. The reader can see the count; what they cannot work out is
/// which of six answers produced it and what removing that one returns. <see cref="Count"/> is a
/// figure a query returned — see <see cref="FacetGroup.Total"/>, which is by construction the size
/// of this listing with exactly this facet's own selection lifted.
/// </remarks>
public sealed record FindLoosen(string Question, string Label, int Count, string Href);

/// <summary>
/// Everything <c>/find</c> renders, built once from one querystring — and rendered twice.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rendered page and <c>?plain=1</c> are two renderers over this one object.</b> They were
/// two different pages: the graphical one asked six questions and the text one dumped ten facet
/// groups, so the two surfaces disagreed about what could even be asked — plain offered the silent
/// bucket that the rendered page hid. A text mirror that shows a different set of facts is not a
/// mirror, and the guarantee it carries ("if it cannot survive here, its graphic is decoration") is
/// only worth something if one construction feeds both.
/// </para>
/// <para>
/// <b>The querystring is the whole of the state.</b> This page used to be a form pointed at the
/// listing and nothing else, so an answered Find page did not exist: it could not be linked, it did
/// not survive reload, and — the reason it matters here — nothing could be counted, because there
/// was no server-side moment at which a set of answers existed. Binding its own URL is what makes
/// every number below possible.
/// </para>
/// </remarks>
public sealed class FindScreen
{
    /// <summary>How many answers a question shows before the rest go behind a disclosure.</summary>
    /// <remarks>
    /// Nine of twelve genre values on the live catalogue match two games or fewer, and an option
    /// that returns one result is trivia rather than a filter. They arrive commonest-first, so the
    /// tail is exactly the part nobody was going to choose — and it is a native
    /// <c>&lt;details&gt;</c> holding the real values, never a bucket of its own. A submittable
    /// "3 more genres" would be Find offering a choice <c>/games</c> cannot express, which is the
    /// first thing this page's own header comment says would drift.
    /// </remarks>
    private const int OptionsShown = 6;

    /// <summary>Below this many results, the page offers the answer responsible.</summary>
    /// <remarks>
    /// Tuned for a catalogue of several hundred, where most plausible combinations return nothing.
    /// The button is drawn only when some answer's removal actually returns more, so a small
    /// catalogue does not grow a control that cannot do anything.
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
    /// <para>
    /// <b>Every number here costs a query, and each one is a real one.</b> The listing under the
    /// current answers is the count and every choice question's option counts at once — that is what
    /// <see cref="FacetedSearch"/> returns, and a choice facet's values are already counted with
    /// that facet's own selection lifted, so the number on an option is what clicking it returns
    /// rather than how many games hold that value in isolation. Three more passes are taken and no
    /// more: the listing with nothing asked (the denominator), the listing with the client answer
    /// lifted (only when one is given — a presence facet counts against the current results, which
    /// is the wrong denominator for a control that replaces its own selection), and the listing with
    /// the archive switch flipped (so both answers to the last question carry a count).
    /// </para>
    /// <para>
    /// The cost is the same order as the listing's own, taken three or four times on a page nobody
    /// reloads in a loop. The point at which it stops being affordable is aggregation in the
    /// database — and the counts would then need pinning against the listing rather than being the
    /// same arithmetic by construction, which is the trade <see cref="FacetedSearch"/> already
    /// names.
    /// </para>
    /// </remarks>
    public static async Task<FindScreen> BuildAsync(
        IGameQueries queries,
        string? query,
        string? tag = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queries);

        // The locale is built in rather than applied afterwards, because the words are part of what
        // this object is: the rendered page and the text mirror both read their labels off it, and a
        // second translation step is a second place for the two to disagree.
        var locale = tag ?? Locales.SourceTag;

        // Everything cleared except which surface the reader is on. "Clear all answers" is about the
        // answers; taking somebody out of the plain rendering because they emptied the form would be
        // the one control on this page that undoes a choice it was never asked about.
        var cleared = "/find" + ListingLinks.With(string.Empty, "plain", Plain(query) ? "1" : null);

        if (!GameFilterBinding.TryRead(query, out var bound, out var error))
        {
            return new FindScreen([], [], 0, 0, null, "/games", cleared, error);
        }

        var filter = bound.Filter;
        var answered = await queries.SearchAsync(filter, cancellationToken);

        // The denominator. Re-read through the binding rather than built from `new GameFilter()`,
        // because "the listing with nothing asked of it" is a fact about an empty *querystring* and
        // the binding is where the listing's own defaults live — a bare filter counts the archive
        // and the adult declarations in, which is not the listing anybody lands on.
        GameFilterBinding.TryRead(string.Empty, out var unasked, out _);

        var listed = SameQuestion(filter, unasked.Filter)
            ? answered
            : await queries.SearchAsync(unasked.Filter, cancellationToken);

        // A presence facet counts each value against the games already on screen, which is exactly
        // right for a checkbox that intersects and exactly wrong for one option of a single choice
        // that replaces. Asked again with the client answer lifted, the numbers become what clicking
        // returns. Skipped entirely when nothing was asked, because then the two are the same pass.
        var loose = filter.MeasuredProtocols.Count > 0 || filter.Tls
            ? await queries.SearchAsync(
                filter with { MeasuredProtocols = [], Tls = false }, cancellationToken)
            : answered;

        // The last question's other answer. Its count exists nowhere in the pass above: excluding
        // the archive happens before any facet is counted, so nothing in `answered` knows how many
        // games it left out.
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
    /// Only so the commonest visit — somebody arriving having asked nothing — takes one pass rather
    /// than two. The record's own equality does the comparing, so a filter growing a field cannot
    /// leave this quietly answering about the fields it used to have; the protocol list is compared
    /// by its contents first, because two empty lists are not the same object and record equality
    /// would call them different.
    /// </remarks>
    private static bool SameQuestion(GameFilter a, GameFilter b) =>
        a.MeasuredProtocols.SequenceEqual(b.MeasuredProtocols, StringComparer.OrdinalIgnoreCase)
        && (a with { MeasuredProtocols = b.MeasuredProtocols }) == b;

    /// <summary>
    /// The three bands worth offering here, spelled by the enum rather than by this file.
    /// </summary>
    /// <remarks>
    /// Archived is left out because the last question asks it directly, and dark because "show me
    /// games nobody can reach" is not what somebody looking for a game to play is asking. Both stay
    /// reachable from the listing's own panel, which is exhaustive by design.
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
    /// Null where the catalogue holds no values for it, because a question with one answer is not a
    /// question. Every option is read off the facet group rather than written down here — the same
    /// rule from the other end as the form's: this page cannot offer a genre no game has, and it
    /// cannot claim a count the listing would not produce.
    /// <para>
    /// <paramref name="id"/> names the question in the message bundle, not on the page. The wording
    /// of the six questions is the one thing here written in a reader's language rather than the
    /// catalogue's, so it is translated text and it lives where translated text lives.
    /// </para>
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
                FacetWords.Value(key, v),
                v.Count,
                v.IsSelected,
                "/find" + ListingLinks.With(query, key, v.IsSelected ? null : v.Token)))
            .ToList();

        // A scale keeps its declared order — "connected now" is a narrower window than "active this
        // week" and shuffling the two by size would make the ladder read as a ranking. Everything
        // else is alternatives, and the commonest first is the order somebody scans.
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

            // What dropping this answer returns: this facet's own selection lifted, every other
            // answer still applied. It is not arithmetic on the numbers above it — FacetedSearch
            // counts that set to produce the group in the first place.
            group.IsFiltered ? group.Total : null);
    }

    /// <summary>
    /// The commonest few, and the rest behind a disclosure.
    /// </summary>
    /// <remarks>
    /// The silent bucket is never in the tail whatever it weighs. It is the option that makes the
    /// silence filterable rather than merely disclosed — "show me games nobody has classified" is
    /// three quarters of the directory on some questions — and it is exactly the value a popularity
    /// cut would fold away on a well-covered catalogue. A chosen answer is never in the tail either:
    /// a reader's own selection scrolled out of sight is the defect the disclosure would introduce.
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
    /// <para>
    /// <b>TLS appears once.</b> It reached the page twice: the dedicated <c>tls</c> facet, whose
    /// value is a handshake we completed, and a <c>protocol=TLS</c> row falling through to the
    /// generic gloss beside it. Two controls, two meanings and one acronym is worse than either
    /// alone, and the one kept is the one whose evidence is the connection rather than a name in a
    /// list.
    /// </para>
    /// <para>
    /// Single choice rather than a set, which the listing's panel offers and this does not: a
    /// question flow asks one thing at a time, and the intersection of four capabilities is the
    /// combination most likely to return nothing on a catalogue this size. Nothing is lost — every
    /// one of them is still a checkbox on <c>/games</c>, which is where this page's submit lands.
    /// </para>
    /// </remarks>
    private static FindQuestion Client(GameListing loose, GameFilter filter, string tag, string query)
    {
        var chosen = filter.MeasuredProtocols.Count > 0 || filter.Tls;

        // Both keys cleared, then one set: choosing TLS has to un-choose MSSP, and a single-choice
        // control that leaves the other key standing would send the listing an intersection the
        // reader never asked for.
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

        return new FindQuestion(
            FacetKeys.Protocol,
            Messages.For(tag, "find.q.client"),
            FacetEvidence.Measured,
            new FindOption(
                Messages.For(tag, "find.any.client"), loose.Games.Count, !chosen, "/find" + bare),
            [.. ordered.Take(OptionsShown)],
            [.. ordered.Skip(OptionsShown).Where(o => !o.IsChosen)],

            // The client answer lifted is a pass we already took, so this figure is a listing's own
            // size rather than a guess at one.
            chosen ? loose.Games.Count : null);
    }

    /// <summary>
    /// "Include games that have gone dark?" — and the reason its default is not inverted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handoff asks for <c>include them</c> as the default, so that "none of these questions are
    /// required" becomes true. Archiving removes a game from the default listing and from nothing
    /// else, which is the whole of the rule and is the listing's behaviour on every other door into
    /// the same query — inverting it here would hand one reader two different result sets depending
    /// on which page they came through, and the drift would be invisible. The handoff offers the
    /// compatible branch itself: keep the default, and do not also claim nothing is filtered. The
    /// sentence making that claim is deleted, and this question says what is applied in the one way
    /// that cannot go stale — both answers carry the number they return.
    /// </para>
    /// <para>
    /// Badged <em>derived</em> and not <em>measured</em>. A game is archived because of what we
    /// measured, but the threshold and the decision are ours, and a reader has to be able to see
    /// which part of the page is us.
    /// </para>
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

            // Deliberately never a loosen candidate. Dropping this answer means returning to the
            // default, which narrows rather than widens, and the widening move is one the reader
            // never made — a button offering to "drop" it would be naming an answer nobody gave.
            WithoutThis: null);
    }

    /// <summary>Every answer given, as a removable chip.</summary>
    /// <remarks>
    /// The archive switch is a chip only when it is doing something the reader asked for. Drawn on
    /// every visit it would be an answer nobody gave, sitting in a list headed by how many answers
    /// they have given.
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
    /// The one that returns the most, and only where that is more than the reader has now. The
    /// handoff picks the answer with the smallest marginal count instead, which is a proxy for this
    /// and is wrong whenever two answers overlap — and the proxy is unnecessary here, because the
    /// real figure was already counted.
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
    /// <para>
    /// One message per capability, carrying the acronym and the gloss together. Deliberately not the
    /// reference article's summary: those are a sentence each and this is an option label. And
    /// deliberately not a name and a gloss assembled here, which is how the rendered page came to
    /// read <c>MSSP— server self-description</c>: Razor ate the whitespace after a conditional
    /// block, nothing that reads markup was going to notice, and a language that puts the gloss
    /// before the acronym has nowhere to say so if the two are glued together in C#.
    /// </para>
    /// <para>
    /// The fallback takes the token as an argument rather than being written round it, because the
    /// row for a capability this list has never heard of is the one row where the machine voice and
    /// the translated voice genuinely do meet.
    /// </para>
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
