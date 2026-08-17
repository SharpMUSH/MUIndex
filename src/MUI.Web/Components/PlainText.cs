using System.Text;
using MUI.Catalog;
using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>
/// The plain rendering of the site.
/// </summary>
/// <remarks>
/// <para>
/// Served at <c>?plain=1</c> and automatically to text browsers. It is not a courtesy: it is the
/// test of whether a fact is really being communicated. If something cannot survive here, its
/// graphic on the main page is decoration.
/// </para>
/// <para>
/// It renders from the same view models the graphical pages use, which is what bounds its
/// maintenance cost — the main pages are these with graphics added, not second documents that have
/// to be kept in step. No prose here is wider than 80 columns — addresses excepted, see
/// <see cref="Columns"/> — and every state is a word, never a glyph, a colour or a cell shape.
/// </para>
/// </remarks>
public static class PlainText
{
    /// <summary>
    /// No prose this renderer writes exceeds this, because text browsers are 80 wide.
    /// </summary>
    /// <remarks>
    /// <b>URLs are the one exception, and are exempt on purpose.</b> The cap used to be documented
    /// as covering everything, which was not true and could not be made true: an address is printed
    /// whole because a wrapped one is not clickable in the browsers this surface exists for, and a
    /// find query with six answers in it is longer than eighty columns on its own. The choice is
    /// between a line a reader can follow and a line a reader can use, and for an address it is the
    /// second. Everything that is not an address wraps — <see cref="Wrap"/> — and
    /// <c>NoPlainLineIsWiderThanEightyColumns</c> enforces exactly that split.
    /// </remarks>
    public const int Columns = 80;

    public static string Render(
        GamePage page,
        DateTimeOffset now,
        ReachSummary? reach = null,
        TrendSeries? trend = null,
        string tag = Locales.SourceTag)
    {
        var b = new StringBuilder();
        var s = page.Summary;

        b.Append(s.Name.ToUpperInvariant());
        // The states that withhold a game from the listing are named here rather than folded into
        // [archived], because this surface is the one a reader reaches with a script: a page that says
        // [archived] for a game whose owner asked to come out would be the wrong fact in the only
        // field a parser reads.
        b.Append(s.State switch
        {
            LifecycleState.Archived => " [archived]",
            LifecycleState.Excluded => " [excluded]",
            LifecycleState.Unlisted => " [unlisted]",
            _ => s.IsClaimed ? " [claimed]" : " [unclaimed]",
        });
        b.AppendLine();

        foreach (var e in page.Endpoints)
        {
            b.AppendLine($"telnet {e.Host} {e.Port}{(e.TlsMeasured ? " · tls measured" : string.Empty)}");
        }

        b.AppendLine();

        // Every state spelled as a word. "Unknown" is written out rather than left blank, because a
        // blank reads as zero to a human exactly as it does to a parser — and the count says how it
        // was obtained here as it does on the listing, or this page is the less honest of the two.
        b.AppendLine((s.PlayersNow is { } n
            ? $"{Say(tag, "game.plain.playersNow", ("count", n))}  {Label(tag, s.PlayersNowProvenance, now)}"
            : Say(tag, "game.plain.playersUnknown")).TrimEnd());

        if (page.ReachableFraction is { } r)
        {
            b.AppendLine(Say(
                tag,
                "reach.plain.fraction",
                ("percent", Wording.Percent(r)),
                ("days", ReachSeries.WindowDays)));
        }

        if (page.LongestOutage is { } o)
        {
            b.AppendLine(Say(tag, "reach.plain.longestOutage", ("duration", Wording.Duration(o))));
        }

        AppendActivity(b, page.Activity, tag);
        AppendTrend(b, trend, tag);
        AppendReachable(b, reach, tag);
        AppendCapabilities(b, page, tag);
        AppendDeclared(b, page, now, tag);
        AppendConnectScreen(b, page, tag);
        AppendChanges(b, page, tag);

        return b.ToString();
    }

    /// <summary>
    /// The heatmap in words. The sentence first — it is the answer — then a line per day, which is
    /// the same content the graphical page hides behind "read as text". The three states of spec
    /// §5.4 are three different words here and never share one.
    /// </summary>
    private static void AppendActivity(StringBuilder b, IReadOnlyList<ActivityCell> cells, string tag)
    {
        if (cells.Count == 0)
        {
            return;
        }

        Heading(b, Say(tag, "activity.plain.heading"));

        // The same threshold the graphical page draws on, and the same words. Below it there is no
        // grid there and no seven lines here: a week of prose about two measured hours would be this
        // surface describing a shape the measurements do not have.
        if (ActivitySummary.MeasuredDays(cells) < ActivitySummary.MeasuredDaysForGrid)
        {
            Wrap(b, ActivitySummary.Sparse(tag, cells));
            return;
        }

        Wrap(b, ActivitySummary.Sentence(tag, cells));
        b.AppendLine();

        foreach (var line in ActivitySummary.PerDay(tag, cells))
        {
            Wrap(b, line, "  ");
        }

        b.AppendLine();

        // The key. The three words on the left are the site's own — two of them the glossary's
        // locked ids — rather than a third spelling invented for this surface: "no data" said here
        // what "not measured" says everywhere else, which left a reader deciding whether the two
        // were one state. Wrapped rather than padded to a column, because a language whose word for
        // "uncounted" is four syllables must not push the line past eighty.
        Key(b, tag, "activity.key.counted", "activity.key.counted.meaning");
        Key(b, tag, "state.uncounted", "activity.key.uncounted.meaning");
        Key(b, tag, "state.notMeasured", "activity.key.notMeasured.meaning");
    }

    private static void Key(StringBuilder b, string tag, string word, string meaning) =>
        Wrap(b, $"{Say(tag, word)} = {Say(tag, meaning)}", "  ");

    /// <summary>
    /// The trend in words: the direction first, then a line per week.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Weeks rather than days, from <see cref="TrendSeries.PerWeek"/> — the same lines the graphical
    /// page hides behind "read as text", so the two surfaces cannot drift into saying different
    /// things about one series. A quarter is ninety days and nobody reads ninety lines.
    /// </para>
    /// <para>
    /// The seek links are here too. A range is part of the address, and a text browser that could
    /// see the chart's window but not change it would have the graphic's navigation and none of its
    /// function — which is the decoration §9 is testing for.
    /// </para>
    /// </remarks>
    private static void AppendTrend(StringBuilder b, TrendSeries? trend, string tag)
    {
        if (trend is null || trend.Days.Count == 0)
        {
            return;
        }

        Heading(b, $"{Say(tag, "trend.plain.heading")} "
            + $"({Say(tag, "trend.plain.range", ("from", trend.From), ("to", trend.To))})");
        Wrap(b, trend.Sentence(tag));

        if (!trend.HasAnyCount)
        {
            return;
        }

        b.AppendLine();

        foreach (var line in trend.PerWeek(tag))
        {
            Wrap(b, line, "  ");
        }

        b.AppendLine();

        var range = new TrendRange(trend.From, trend.To);

        b.AppendLine($"  {Say(tag, "trend.plain.earlier")}: ?{range.Previous().Query}&plain=1");
        Wrap(b, Say(tag, "trend.plain.note"), "  ");
    }

    /// <summary>The 90-day strip in words: the summary, then every spell that was not reachable.</summary>
    private static void AppendReachable(StringBuilder b, ReachSummary? reach, string tag)
    {
        if (reach is null)
        {
            return;
        }

        Heading(b, $"{Say(tag, "reach.plain.heading")} "
            + $"({Say(tag, "reach.plain.window", ("days", reach.Window))})");
        Wrap(b, reach.Sentence(tag));

        var spells = reach.Spells(tag);
        if (spells.Count == 0)
        {
            return;
        }

        b.AppendLine();
        foreach (var spell in spells)
        {
            b.AppendLine($"  {spell}");
        }
    }

    private static void AppendCapabilities(StringBuilder b, GamePage page, string tag)
    {
        Heading(b, Say(
            tag,
            "game.plain.capabilities",
            ("disagreeing", page.DisagreementCount),
            ("total", page.Capabilities.Count)));

        // The same order the matrix uses: disagreements first, then measured-present, then absent,
        // then unknown. Two surfaces of one fact must not put it in two places.
        foreach (var c in page.Capabilities
            .OrderByDescending(c => c.Disagrees)
            .ThenBy(c => c.Measured switch
            {
                CapabilityState.Present => 0,
                CapabilityState.Absent => 1,
                _ => 2,
            })
            .ThenBy(c => c.Protocol, StringComparer.Ordinal))
        {
            var flag = c.Disagrees ? "  " + Say(tag, "game.plain.disagree") : string.Empty;
            b.AppendLine($"  {c.Protocol,-10} {Say(tag, "game.plain.measured")}: {Word(c.Measured),-7} "
                + $"{Say(tag, "game.plain.declared.column")}: {Word(c.Declared)}{flag}");
        }
    }

    private static void AppendDeclared(StringBuilder b, GamePage page, DateTimeOffset now, string tag)
    {
        if (page.Declared.Count == 0)
        {
            return;
        }

        Heading(b, Say(tag, "game.plain.declared"));

        foreach (var (name, chip) in page.Declared)
        {
            b.AppendLine($"  {name,-10} {chip.Value}  {Label(tag, chip, now)}");
        }
    }

    /// <summary>
    /// A provenance chip in words: how we know it, how old it is, and whether it has aged out.
    /// </summary>
    /// <remarks>
    /// The whole of what the rendered chip carries — glyph, relative age, amber — spelled out. One
    /// function because four surfaces print it: the listing's counts and codebases, the game page's
    /// count and its self-description, and the archive. Four spellings of "declared six years ago"
    /// would be four chances to say it four ways, and this comment claimed the archive before the
    /// archive did — which is how <c>/games</c> and <c>/archive</c> came to describe the same value
    /// two ways for a while. An absent chip prints nothing rather than inventing a source for a
    /// value nobody has labelled.
    /// <para>
    /// The word itself is <see cref="Provenance.How"/>'s, which is where it moved once the preview
    /// metadata needed it too — a rule spelled at one of five call sites is a rule the other four
    /// break, and a private spelling is one the fifth surface cannot reach even to obey.
    /// </para>
    /// </remarks>
    internal static string Label(string tag, ProvenanceChip? chip, DateTimeOffset now) => chip is null
        ? string.Empty
        : Messages.For(
            tag,
            chip.IsStale ? "chip.plain.stale" : "chip.plain",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["how"] = Provenance.How(tag, chip),
                ["age"] = Relative.Format(tag, now - chip.LastConfirmedAt),
            });

    /// <summary>
    /// The connect screen with its SGR stripped. Colour codes are never announced, and the cases the
    /// frame has — suppressed, absent, too small — are stated rather than left as an absence for the
    /// reader to interpret.
    /// </summary>
    private static void AppendConnectScreen(StringBuilder b, GamePage page, string tag)
    {
        var screen = Ansi.Parse(page.ConnectScreen, page.ConnectScreenSuppressed);

        Heading(b, Say(tag, "game.plain.connectScreen"));

        switch (screen.State)
        {
            case AnsiScreenState.Suppressed:
                Wrap(b, Say(tag, "ansi.suppressed"), "  ");
                return;

            case AnsiScreenState.Absent:
                Wrap(b, Say(tag, "ansi.absent"), "  ");
                return;

            case AnsiScreenState.TooSmall:
                Wrap(b, Say(tag, "ansi.tooSmall", ("count", screen.RowCount)), "  ");
                return;
        }

        // The same caption the figure carries, for the same reason: on a game whose bytes were not
        // UTF-8 this is how a reader learns which encoding they are looking at rather than blaming
        // their terminal. The two surfaces must not disagree about the screen.
        b.AppendLine("  [" + (page.ConnectScreenCharset is { Length: > 0 } read
            ? Say(tag, "ansi.plain.rows.readAs", ("count", screen.RowCount), ("charset", read))
            : Say(tag, "ansi.plain.rows", ("count", screen.RowCount))) + "]");
        b.AppendLine();
        foreach (var row in screen.Rows)
        {
            b.Append("  ").AppendLine(row.Text.TrimEnd());
        }
    }

    private static void AppendChanges(StringBuilder b, GamePage page, string tag)
    {
        if (page.Changes.Count == 0)
        {
            return;
        }

        Heading(b, Say(tag, "game.plain.whatChanged"));
        foreach (var change in page.Changes.OrderByDescending(c => c.At))
        {
            b.AppendLine($"  {change.At:yyyy-MM-dd}  {change.Summary}");
        }
    }

    /// <summary>
    /// The listing and its facets, with every state spelled and no column past 80.
    /// </summary>
    /// <remarks>
    /// The facets are here in full — every value, its count, and the parameter that selects it —
    /// because a text browser cannot operate a <c>&lt;select&gt;</c> but can perfectly well edit a
    /// URL. A panel that only worked as a widget would fail §9's own test of itself: if a fact
    /// cannot survive in plain text, its graphic on the main site is decoration.
    /// </remarks>
    public static string RenderListing(
        GameListing listing, GameFilter filter, DateTimeOffset now, string tag = Locales.SourceTag)
    {
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentNullException.ThrowIfNull(filter);

        var games = listing.Games;
        var b = new StringBuilder();

        b.AppendLine("GAMES");
        b.AppendLine($"{games.Count} game(s)"
            + (string.IsNullOrWhiteSpace(filter.Text) ? string.Empty : $" matching \"{filter.Text}\"")
            + (filter.IncludeArchived ? ", archived included" : ", archived excluded")

            // Both exclusions, both stated. A text browser cannot see the checkbox in the bar, so
            // the line that says what this listing is has to carry the same two facts the bar does.
            + (filter.IncludeAdult ? ", adult included" : ", adult excluded"));

        // The order, stated. A sorted list that does not say what it is sorted by is one a reader has
        // to reverse-engineer from the first few rows — and that is exactly how a tail of games
        // showing no number gets read as a tail of games with no players.
        b.AppendLine($"Sorted by {FacetWords.Sort(tag, filter.Sort)}");

        // And every order it could have been in, wrapped rather than run on: a text browser cannot
        // operate a <select> but can perfectly well edit a URL, and nine sort tokens on one line is
        // a hundred and thirty columns of it running off the right of the screen. Wrapped, because
        // the alternative — offering three of the nine and calling it the list — is a control the
        // plain surface has that the rendered one does not.
        Wrap(b, $"?{FacetKeys.Sort}={string.Join(" / ", FacetTokens.Sorts)}", "  ");

        AppendFacets(tag, b, listing.Facets);
        b.AppendLine();

        if (games.Count == 0)
        {
            b.AppendLine("Nothing matched. Try fewer words, or drop a filter.");
            return b.ToString();
        }

        var broken = false;

        foreach (var g in games)
        {
            // The same break the rendered listing draws, in the same place and for the same reason:
            // where the sort runs out of things it can rank, the list says so rather than letting the
            // rows that follow read as the bottom of the ranking.
            if (!broken && GameSorting.IsUnranked(g, filter.Sort))
            {
                broken = true;
                b.AppendLine($"-- from here: {FacetWords.Unranked(tag, filter.Sort)}");
                b.AppendLine();
            }

            // Archived and claimed are the two worth a mark; unclaimed is most of the catalogue and
            // is not one, here for the same reason it is not on the rendered row. This surface and
            // that one say the same things about a game or they are not two views of one listing.
            var mark = g.State is LifecycleState.Archived ? "  [archived]"
                : g.IsClaimed ? "  [claimed]"
                : string.Empty;
            b.AppendLine($"{g.Name}{mark}");
            b.AppendLine($"  /g/{g.Slug}");

            // How we know, and how old it is — the same two words and the same relative age the game
            // page uses, because two surfaces of one fact must not have two vocabularies. The word
            // was hard-coded here and said "(measured)" over every count including the ones a game
            // asserted about itself, which is rule 5 broken by a format string.
            b.AppendLine((g.PlayersNow is { } n
                ? $"  Players now: {n}   {Label(tag, g.PlayersNowProvenance, now)}"
                : "  Players now: unknown (no count could be measured)").TrimEnd());

            // What a window sort ranked this row on. Only where there is one, because a line reading
            // "over 7 days: —" on every row of an alphabetical listing is a column of nothing.
            if (g.PlayersOverWindow is { } window)
            {
                b.AppendLine($"  Ranked on:   {FacetWords.Window(tag, window, filter.Sort)}");
            }

            // Never blank. "We could not identify it" is a measurement and a missing line is not.
            b.AppendLine((g.Codebase is { } codebase
                ? $"  Codebase:    {codebase}  {Label(tag, g.CodebaseProvenance, now)}"
                : "  Codebase:    not identified").TrimEnd());

            b.AppendLine(g.MeasuredProtocols.Count > 0
                ? $"  Measured:    {string.Join(", ", g.MeasuredProtocols)}"
                : "  Measured:    nothing offered in the handshake");

            // The last-seen facet's own column. Never once reached is its own sentence rather than
            // the oldest bucket, because a game we have never got an answer from has no date and
            // inventing one from our first sighting would read as its outage.
            b.AppendLine(g.LastReachableAt is { } seen
                ? $"  Last reached: {Relative.Ago(tag, now - seen, AgeSense.Reached)}"
                : "  Last reached: never — no answer yet");

            if (g.Tagline is { } tagline)
            {
                Wrap(b, tagline, "  ");
            }

            b.AppendLine();
        }

        return b.ToString();
    }

    /// <summary>
    /// The facet panel in text: what each choice returns, and what to put in the URL to choose it.
    /// </summary>
    /// <remarks>
    /// The two sentences at the top are the same two the rendered panel carries, and they are not
    /// blurb. An unticked protocol is not a game declining a protocol, and a facet with no value for
    /// a game is not a no — those are the two readings this whole design exists to prevent, and a
    /// surface that leaves them to be inferred has left the important half out.
    /// </remarks>
    private static void AppendFacets(string tag, StringBuilder b, IReadOnlyList<FacetGroup> facets)
    {
        if (facets.Count == 0)
        {
            return;
        }

        Heading(b, "FILTERS");
        // Enumerated rather than spelled out, so a register added to the vocabulary cannot be
        // introduced on the rendered panel and quietly left out of the plain one — which is the
        // surface where the key is the only place the distinction is ever made.
        Wrap(b, "Each facet is marked "
            + string.Join(", ", Enum.GetValues<FacetEvidence>()
                .Select(e => $"{FacetWords.Evidence(tag, e)} ({FacetWords.EvidenceMeaning(tag, e)})"))
            + ".");
        b.AppendLine();
        Wrap(b, "Counts are exact, from the same query as the list below. A blank is a gap in our "
            + "measurement, never a \"no\": each facet spells its own. A measured zero is a count; "
            + "an unknown count is not a zero and never sorts as one.");

        foreach (var group in facets)
        {
            b.AppendLine();
            b.AppendLine($"  {FacetWords.Group(tag, group.Key)}"
                + $" — {FacetWords.Evidence(tag, group.Evidence)}  (?{group.Key}=…)");

            foreach (var value in group.Values)
            {
                var words = FacetWords.Value(tag, group.Key, value);
                var gloss = string.Equals(words, value.Token, StringComparison.Ordinal)
                    ? string.Empty
                    : "  " + words;

                // A star, not a colour: the selected value has to be visible where there is no ink.
                b.AppendLine($"  {(value.IsSelected ? '*' : ' ')} {value.Token,-24}{value.Count,5}{gloss}"
                    .TrimEnd());
            }
        }
    }

    /// <summary>
    /// The three liveness feeds. All three are the same shape here, because the register the
    /// graphical cards carry is a tone and a tone is not a fact — the words have to do the work.
    /// </summary>
    public static string RenderFeeds(string tag, LivenessFeeds feeds, DateTimeOffset now)
    {
        var b = new StringBuilder();

        Feed(b, tag, "NEWLY DISCOVERED", feeds.NewlyDiscovered, "Nothing new.", now);
        Feed(b, tag, "WENT DARK", feeds.WentDark, "Nothing went dark.", now);
        Feed(b, tag, "CAME BACK", feeds.CameBack, "Nothing came back. We keep knocking.", now);

        return b.ToString();

        static void Feed(
            StringBuilder b,
            string tag,
            string title,
            IReadOnlyList<FeedEntry> entries,
            string empty,
            DateTimeOffset now)
        {
            b.AppendLine(title);

            if (entries.Count == 0)
            {
                b.AppendLine($"  {empty}");
                b.AppendLine();
                return;
            }

            foreach (var e in entries)
            {
                b.AppendLine($"  {e.Name}  ({Relative.Ago(tag, now - e.At)})  /g/{e.Slug}");
                Wrap(b, e.Detail, "    ");
            }

            b.AppendLine();
        }
    }

    /// <summary>The home page: what we know, then what changed.</summary>
    public static string RenderHome(
        string tag,
        SiteCounts counts,
        LivenessFeeds feeds,
        CrawlerPulse pulse,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(pulse);

        var b = new StringBuilder();

        b.AppendLine("MU*INDEX");
        b.AppendLine();
        b.AppendLine($"{counts.Known} games known");
        b.AppendLine($"{counts.WithPlayersOn} connected now (measured)");
        b.AppendLine($"{counts.CountUnknown} answering, uncounted");
        b.AppendLine($"{counts.Archived} archived, still probed");

        // Same three facts as the rendered strip, in the same order and the same words, plus the
        // registry line the narrow page has no room for. Omitted entirely when there is nothing
        // measured to say, which is what the strip does and what the demo path yields.
        if (pulse.State(now) is not CrawlState.NotYet)
        {
            b.AppendLine();
            b.AppendLine(CrawlerCopy.State(tag, pulse, now));

            if (CrawlerCopy.LastCycle(pulse) is { } cycle)
            {
                b.AppendLine(cycle);
            }

            b.AppendLine(CrawlerCopy.Registry(pulse));
        }

        b.AppendLine();

        b.Append(RenderFeeds(tag, feeds, now));
        return b.ToString();
    }

    /// <summary>The archive. Past tense, no alarm, and the run of years given as a fact.</summary>
    /// <remarks>
    /// The label column is measured rather than hard-spaced. Four labels padded to seventeen
    /// columns is an arrangement that holds for exactly one language, and a locale whose word for
    /// "last reachable" is longer would have run its value into it.
    /// </remarks>
    public static string RenderArchive(
        IReadOnlyList<ArchiveEntry> entries,
        string? query,
        DateTimeOffset now,
        string tag = Locales.SourceTag)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var b = new StringBuilder();

        b.AppendLine(Say(tag, "archive.title").ToUpperInvariant());
        Wrap(b, Say(tag, "archive.lede"));
        b.AppendLine();
        b.AppendLine(string.IsNullOrWhiteSpace(query)
            ? Say(tag, "archive.plain.count", ("count", entries.Count))
            : Say(tag, "archive.plain.matching", ("count", entries.Count), ("query", query)));
        b.AppendLine();

        string[] labels =
        [
            Say(tag, "archive.plain.lastReachable"),
            Say(tag, "archive.plain.knownLive"),
            Say(tag, "archive.plain.run"),
            Say(tag, "archive.plain.codebase"),
        ];

        var width = Math.Max(17, labels.Max(l => l.Length) + 1);
        var badge = Say(tag, "archive.badge");

        foreach (var entry in entries)
        {
            b.AppendLine($"{entry.Summary.Name}  [{badge}]");
            b.AppendLine($"  /g/{entry.Summary.Slug}");
            b.AppendLine($"  {labels[0].PadRight(width)}{entry.LastAnswered(tag)} "
                + Say(tag, "archive.darkFor", ("age", entry.DarkFor(tag, now))));
            b.AppendLine($"  {labels[1].PadRight(width)}"
                + Say(tag, "archive.plain.knownLiveValue", ("value", entry.KnownLiveWording(tag))));

            if (entry.Run(tag) is { } run)
            {
                b.AppendLine($"  {labels[2].PadRight(width)}{run}");
            }

            // Labelled here above all. This is where a value is oldest — nobody has confirmed an
            // archived game's codebase since the day it stopped answering — and the archive read
            // "Codebase: PennMUSH 1.8.5" flat while the listing said the same value was three years
            // unconfirmed. Same fact, same words, whichever page a reader is on.
            if (entry.Summary.Codebase is { } codebase)
            {
                b.AppendLine(
                    $"  {labels[3].PadRight(width)}{codebase}  {Label(tag, entry.Summary.CodebaseProvenance, now)}"
                        .TrimEnd());
            }

            b.AppendLine();
        }

        if (entries.Count == 0)
        {
            b.AppendLine(Say(tag, "archive.empty"));
        }

        return b.ToString();
    }

    /// <summary>
    /// The about page. Prose, so the only thing the graphical version adds is the shape of it.
    /// </summary>
    /// <remarks>
    /// The attribution list is the part that has to survive here above all: it is what this project
    /// owes the directories it read, and an acknowledgement a text browser cannot render is an
    /// acknowledgement made to the layout rather than to anybody.
    /// </remarks>
    public static string RenderAbout(AboutPage page, string tag = Locales.SourceTag)
    {
        ArgumentNullException.ThrowIfNull(page);

        var b = new StringBuilder();

        // Upper-cased from the translated title rather than typed in capitals, so this surface keeps
        // its own shape in a language that has one. A locale whose script has no case is unchanged,
        // which is correct: the shape is English typography and the words are not.
        b.AppendLine(Say(tag, "about.title").ToUpperInvariant());
        b.AppendLine();
        Wrap(b, page.Lede);

        foreach (var section in page.Sections)
        {
            Heading(b, section.Heading);

            foreach (var point in section.Points)
            {
                b.AppendLine();
                Wrap(b, point.Sentence, "  ");
            }

            if (section.Identity is { } identity)
            {
                b.AppendLine();
                Wrap(b, identity.Wording(tag), "  ");
                b.AppendLine();
                Wrap(b, Say(tag, "about.identity.crawler.line", ("name", identity.Name)), "  ");
                Wrap(b, Say(tag, "about.identity.contact.line", ("url", identity.InfoUrl)), "  ");

                if (!identity.ContactConfigured)
                {
                    Wrap(b, Say(tag, "about.identity.placeholder.plain"), "  ");
                }
            }

            foreach (var source in section.Sources)
            {
                b.AppendLine();
                b.AppendLine($"  {source.Name} — {source.StatusWording(tag)}");
                b.AppendLine($"  {source.Url}");
                Wrap(b, source.Note, "    ");
            }

            if (section.Licence is { } licence)
            {
                b.AppendLine();
                // Every one of these goes through the wrapper rather than being laid out in columns:
                // a licence name and an attribution are both configuration, and a deployment that
                // sets a long one must not push a line off the side of a text browser.
                Wrap(b, Say(tag, "about.licence.code.line", ("licence", licence.CodeLicence)), "  ");
                Wrap(b, Say(tag, "about.licence.data.line", ("licence", licence.DataLicenceName)), "  ");

                if (licence.DataLicenceUrl is { } url)
                {
                    Wrap(b, url, "  ");
                }

                Wrap(b, Say(tag, "about.licence.deployment"), "  ");
                Wrap(b, Say(tag, "about.licence.credit.line", ("credit", licence.Attribution)), "  ");
                b.AppendLine();
                Wrap(b, licence.Notice, "  ");
            }
        }

        return b.ToString();
    }

    /// <summary>
    /// The ecosystem dashboard, which is the page whose graphic is most obviously an illustration.
    /// </summary>
    /// <remarks>
    /// Every bar on the rendered page illustrates a sentence that is complete without it: "PennMUSH —
    /// 122 of 310 (39.4%)" is the fact, and the bar is a way of seeing several of them at once.
    /// Nothing is lost here but the seeing-at-once, which is the test §9 sets for a graphic.
    /// </remarks>
    public static string RenderEcosystem(
        EcosystemDashboard dashboard, DateTimeOffset now, string tag = Locales.SourceTag)
    {
        ArgumentNullException.ThrowIfNull(dashboard);

        var b = new StringBuilder();

        b.AppendLine(Say(tag, "ecosystem.title").ToUpperInvariant());
        Wrap(b, EcosystemCopy.NoTotals(tag));
        b.AppendLine();
        Wrap(b, Say(tag, "ecosystem.plain.counts",
            ("listed", EcosystemCopy.Listed(tag, dashboard.ListedGames)),
            ("handshakes", EcosystemCopy.Handshakes(tag, dashboard.Handshakes)),
            ("mssp", EcosystemCopy.MsspReports(tag, dashboard.MsspReports))));

        if (dashboard.OldestHandshake is { } oldest)
        {
            Wrap(b, Say(tag, "ecosystem.plain.oldestHandshake",
                ("age", Relative.Format(tag, now - oldest))));
        }

        Heading(b, Say(tag, "ecosystem.codebases.title").ToUpperInvariant());
        Wrap(b, EcosystemCopy.CodebaseBasis(tag, dashboard.Codebases));
        b.AppendLine();

        foreach (var family in dashboard.Codebases.Shared)
        {
            b.AppendLine($"  {family.Label,-24} {EcosystemCopy.Share(tag, family)}");
        }

        if (dashboard.Codebases.Families.Count == 0)
        {
            b.AppendLine("  " + Say(tag, "ecosystem.codebases.none"));
        }

        // The graphic folds these behind a disclosure and this surface has none, so it prints them
        // outright. Both say the same sentence first, which is the point of EcosystemCopy: the
        // plain surface may be shorter than the page and it may not be more or less honest than it.
        if (dashboard.Codebases.SoleUse.Count > 0)
        {
            b.AppendLine();
            Wrap(b, EcosystemCopy.SoleUse(tag, dashboard.Codebases), "  ");
            b.AppendLine();

            foreach (var alone in dashboard.Codebases.SoleUse)
            {
                b.AppendLine($"    {alone.Label}");
            }
        }


        Heading(b, Say(tag, "ecosystem.lineages.title").ToUpperInvariant());
        Wrap(b, Say(tag, "ecosystem.plain.lineages",
            ("evidence", FacetWords.Evidence(tag, FacetEvidence.Derived)),
            ("meaning", FacetWords.EvidenceMeaning(tag, FacetEvidence.Derived))));
        b.AppendLine();

        foreach (var lineage in dashboard.Codebases.Lineages)
        {
            b.AppendLine($"  {lineage.Label,-24} {EcosystemCopy.Share(tag, lineage)}");
        }

        if (dashboard.Codebases.Lineages.Count == 0)
        {
            b.AppendLine("  " + Say(tag, "ecosystem.lineages.none"));
        }

        if (dashboard.Codebases.NotClassified > 0)
        {
            b.AppendLine();
            Wrap(b, Say(tag, "ecosystem.lineages.notClassified",
                ("count", dashboard.Codebases.NotClassified),
                ("family", EcosystemCopy.CustomFamily)), "  ");
        }

        Heading(b, Say(tag, "ecosystem.protocols.title").ToUpperInvariant());
        Wrap(b, EcosystemCopy.Floor(tag));

        if (dashboard.Mssp is { } mssp)
        {
            b.AppendLine();
            Wrap(b, EcosystemCopy.MsspBasis(tag, mssp, dashboard.MsspReports));
        }

        b.AppendLine();

        foreach (var protocol in dashboard.Protocols)
        {
            b.AppendLine($"  {protocol.Protocol}");
            Wrap(b, Say(tag, "ecosystem.plain.measured",
                ("value", EcosystemCopy.Measured(tag, protocol))), "    ");
            Wrap(b, Say(tag, "ecosystem.plain.declared",
                ("value", EcosystemCopy.Declared(tag, protocol))), "    ");
        }

        b.AppendLine();
        Wrap(b, Say(tag, "ecosystem.plain.denominators",
            ("measured", EcosystemCopy.Handshakes(tag, dashboard.Handshakes)),
            ("declared", EcosystemCopy.MsspReports(tag, dashboard.MsspReports))));

        Heading(b, Say(tag, "ecosystem.snapshot.title").ToUpperInvariant());
        Wrap(b, EcosystemCopy.NoCurve(tag));
        b.AppendLine();
        Wrap(b, EcosystemCopy.Transitions(tag, dashboard.CapabilityTransitions));

        return b.ToString();
    }

    /// <summary>
    /// The submission form's prose, and whatever it last answered.
    /// </summary>
    /// <remarks>
    /// <b>The form itself is not rendered here, and it is not missing either.</b> Two text boxes and
    /// a button already are plain text: a text browser posts them perfectly well, so the page keeps
    /// the real form beneath this block rather than describing one. What this renders is everything
    /// around it — what happens to an address, and what happened to the last one — which is the part
    /// that could otherwise have been carried by layout.
    /// </remarks>
    public static string RenderSubmit(
        SubmitAnswer? answer, bool hasCatalogue, string tag = Locales.SourceTag)
    {
        var b = new StringBuilder();

        b.AppendLine(SubmitCopy.Title(tag).ToUpperInvariant());
        b.AppendLine();
        Wrap(b, SubmitCopy.Lede(tag));

        if (answer is not null)
        {
            Heading(b, answer.Heading);
            Wrap(b, answer.Sentence);

            if (answer.Link is { } link)
            {
                b.AppendLine($"  {link.Label}: {link.Href}");
            }
        }

        if (!hasCatalogue)
        {
            Heading(b, Say(tag, "submit.notHere").ToUpperInvariant());
            Wrap(b, SubmitCopy.NoCatalogue(tag));
            return b.ToString();
        }

        Heading(b, Say(tag, "submit.what.heading").ToUpperInvariant());

        foreach (var point in SubmitCopy.Points(tag))
        {
            b.AppendLine();
            Wrap(b, point, "  ");
        }

        return b.ToString();
    }

    /// <summary>
    /// The find-a-game questions, as text — the same six, in the same words, with the same counts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Built from the same <see cref="FindScreen"/> the rendered page draws</b>, which is the
    /// whole of the fix here. This was a different page: it dumped ten facet groups as querystring
    /// recipes while the rendered page asked six questions, and the two disagreed about what could
    /// be asked at all — plain offered the silent bucket the rendered page hid. A text mirror
    /// showing a different set of facts is not a mirror.
    /// </para>
    /// <para>
    /// The addresses differ from the rendered page's only because the querystring they are built
    /// from carries <c>plain=1</c>, so following one stays in this surface. That falls out of the
    /// construction rather than being arranged: every link on both surfaces is the page's own URL
    /// with one parameter changed.
    /// </para>
    /// </remarks>
    public static string RenderFind(FindScreen screen, string? tag = null)
    {
        ArgumentNullException.ThrowIfNull(screen);

        var locale = tag ?? Locales.SourceTag;
        var b = new StringBuilder();

        // Upper-cased from the translated word rather than typed in capitals, so a locale that
        // has this page still gets the surface's own shape. Everything below is read off the
        // screen, which was built for this same locale — there is no second translation here.
        b.AppendLine(Say(locale, "find.title").ToUpperInvariant());

        if (screen.Error is { } problem)
        {
            // Refused rather than ignored, in the same words the rendered page refuses it with.
            Heading(b, Say(locale, "find.refused").ToUpperInvariant());
            Wrap(b, problem, "  ");
            b.AppendLine();
            b.AppendLine("  /find?plain=1");
            return b.ToString();
        }

        Heading(b, Say(locale, "find.kicker").ToUpperInvariant());
        Wrap(b, Say(locale, "find.matching", ("count", screen.Matching)), "  ");
        Wrap(b, Say(
            locale,
            "find.basis",
            ("listed", screen.Listed),
            ("answers", screen.Answers.Count)), "  ");

        if (screen.Matching > 0)
        {
            b.AppendLine();
            b.AppendLine("  " + Say(locale, "find.show", ("count", screen.Matching)));
            b.AppendLine("      " + screen.ShowHref);
        }

        if (screen.Loosen is { } loosen)
        {
            b.AppendLine();
            b.AppendLine("  " + Say(
                locale, "find.drop", ("answer", loosen.Label), ("count", loosen.Count)));
            b.AppendLine("      " + loosen.Href);
        }

        if (screen.Answers.Count > 0)
        {
            Heading(b, Say(locale, "find.answersGiven").ToUpperInvariant());

            foreach (var chip in screen.Answers)
            {
                b.AppendLine($"  {chip.Label}");
                b.AppendLine($"      {Say(locale, "find.clear", ("question", chip.Question))}");
                b.AppendLine($"      {chip.ClearHref}");
            }

            b.AppendLine();
            b.AppendLine("  " + Say(locale, "find.clearAll"));
            b.AppendLine("      " + screen.ClearHref);
        }

        foreach (var question in screen.Questions)
        {
            Heading(
                b,
                question.Text.ToUpperInvariant()
                    + $"  ({FacetWords.Evidence(locale, question.Evidence)})");

            if (question.Any is { } any)
            {
                Answer(b, any);
            }

            // The tail is written out in full. There is no folding here and there should not be:
            // a disclosure is a graphical economy, and the guarantee this surface carries is that
            // every option the page offers can be reached from it.
            foreach (var option in question.Options.Concat(question.Tail))
            {
                Answer(b, option);
            }
        }

        Heading(b, Say(locale, "find.wholeListing").ToUpperInvariant());
        b.AppendLine("  /games?plain=1");

        return b.ToString();
    }

    /// <summary>
    /// One answer: whether it is the one in force, what it is called, what choosing it returns.
    /// </summary>
    /// <remarks>
    /// The state is a pair of characters and never a colour or an indent, and the count is in the
    /// same parentheses the rendered page puts it in a column — so the two surfaces can be read
    /// against each other line for line.
    /// </remarks>
    private static void Answer(StringBuilder b, FindOption option)
    {
        var mark = $"  [{(option.IsChosen ? "x" : " ")}] ";
        var text = $"{option.Label} ({option.Count})";

        if (mark.Length + text.Length <= Columns)
        {
            b.AppendLine(mark + text);
        }
        else
        {
            // A label too long for the line keeps its checkbox on the first line and hangs its
            // continuation under the label rather than under the box, so a wrapped option cannot be
            // read as a second one. The labels that reach this are the catalogue's own — a genre, a
            // language, a lineage — and nothing bounds their length but the games.
            var wrapped = new StringBuilder();
            Wrap(wrapped, text, new string(' ', mark.Length));

            b.Append(mark).Append(wrapped.ToString().AsSpan(mark.Length));
        }

        // The address is never wrapped: see the note on Columns.
        b.AppendLine($"      {option.Href}");
    }

    private static string Say(string tag, string id, params (string Key, object? Value)[] args) =>
        Messages.For(tag, id, args.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal));

    /// <summary>The rankings, with every basis in the same words the rendered page uses.</summary>
    public static string RenderRankings(
        Rankings rankings, DateTimeOffset now, string tag = Locales.SourceTag)
    {
        ArgumentNullException.ThrowIfNull(rankings);

        var b = new StringBuilder();
        var days = (int)rankings.Window.TotalDays;

        b.AppendLine(Say(tag, "rankings.title").ToUpperInvariant());
        Wrap(b, EcosystemCopy.NoVote(tag));

        Heading(b, Say(tag, "rankings.plain.busiest", ("days", days)).ToUpperInvariant());
        Wrap(b, EcosystemCopy.BusiestBasis(tag, rankings));
        b.AppendLine();
        Wrap(b, EcosystemCopy.SpanChoice(tag));
        b.AppendLine();

        // The three windows as addresses, because a text browser cannot use a tab strip and the
        // choice must survive here or the graphical selector is decoration (spec §9). One per line:
        // the addresses do not fit on one inside eighty columns, and a wrapped URL is not clickable
        // in the browsers this surface exists for.
        b.AppendLine("  " + Say(tag, "rankings.plain.windows"));

        // The width the three labels are padded to is measured rather than assumed: "7 days" is
        // six columns and its German is nine, and a hard -8 would have run the address into it.
        var labels = RankingSpans.All.ToDictionary(s => s, s => EcosystemCopy.SpanLabel(tag, s));
        var width = labels.Values.Max(l => l.Length);
        var here = Say(tag, "rankings.plain.thisOne");

        foreach (var span in RankingSpans.All)
        {
            b.AppendLine(span == rankings.Span
                ? $"    [{labels[span].PadRight(width)}] {here}"
                : $"     {labels[span].PadRight(width)}  /rankings?window={span.Slug()}&plain=1");
        }

        b.AppendLine();

        if (rankings.Busiest.Count == 0)
        {
            Wrap(b, Say(tag, "rankings.busiest.empty"), "  ");
        }

        var place = 0;

        foreach (var game in rankings.Busiest)
        {
            place++;
            b.AppendLine($"  {place,3}  {game.Name}");
            Wrap(b, Say(tag, "rankings.plain.row",
                ("median", game.Median),
                ("peak", game.Peak),
                ("samples", game.Samples),
                ("days", game.Days),
                ("window", days)) + $" · /g/{game.Slug}", "       ");
        }

        Heading(b, Say(tag, "rankings.spells.title").ToUpperInvariant());
        Wrap(b, EcosystemCopy.SpellBasis(tag));
        b.AppendLine();

        if (rankings.LongestUnbroken.Count == 0)
        {
            Wrap(b, Say(tag, "rankings.spells.empty"), "  ");
        }

        place = 0;

        foreach (var spell in rankings.LongestUnbroken)
        {
            place++;
            b.AppendLine($"  {place,3}  {spell.Name}");
            Wrap(b, Say(tag, "rankings.plain.spellRow",
                ("date", Dates.Absolute(tag, spell.Since)),
                ("duration", Wording.Duration(spell.LengthAt(now)))) + $" · /g/{spell.Slug}", "       ");
        }

        return b.ToString();
    }

    private static void Heading(StringBuilder b, string title)
    {
        b.AppendLine();
        b.AppendLine(title);
    }

    /// <summary>Wraps to 80 columns, because that is the width a text browser has.</summary>
    internal static void Wrap(StringBuilder b, string text, string indent = "")
    {
        var width = Columns - indent.Length;
        var line = new StringBuilder();

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                b.Append(indent).AppendLine(line.ToString());
                line.Clear();
            }

            if (line.Length > 0)
            {
                line.Append(' ');
            }

            line.Append(word);
        }

        if (line.Length > 0)
        {
            b.Append(indent).AppendLine(line.ToString());
        }
    }

    /// <summary>
    /// Colour is never the only carrier of a state, and here there is no colour at all.
    /// </summary>
    /// <remarks>
    /// The one thing on this surface left in English on purpose. These are a two-column tabular
    /// token in a fixed-width table eighty columns wide, read the way a flag in <c>ls -l</c> is,
    /// and the row's own labels beside them <em>are</em> translated — so what a reader meets is a
    /// German sentence naming a machine token, which is the arrangement every other machine value
    /// on this site already has. A four-syllable translation would push the row past eighty.
    /// </remarks>
    private static string Word(CapabilityState state) => state switch
    {
        CapabilityState.Present => "yes",
        CapabilityState.Absent => "NO",
        _ => "-",
    };
}

/// <summary>
/// What the front page can honestly count. Every figure here is a count of games we measured, so
/// there is no "reachable this week" until the store can answer it — an unmeasured number is left
/// out rather than estimated.
/// </summary>
public sealed record SiteCounts(int Known, int WithPlayersOn, int CountUnknown, int Archived)
{
    public static SiteCounts From(IReadOnlyList<GameSummary> all) => new(
        all.Count,
        all.Count(g => g.PlayersNow > 0),
        all.Count(g => g.PlayersNow is null),
        all.Count(g => g.State is LifecycleState.Archived));
}
