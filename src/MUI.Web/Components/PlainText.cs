using System.Text;
using MUI.Catalog;
using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>
/// The plain rendering of the site.
/// </summary>
/// <remarks>
/// <para>
/// Served at <c>?plain=1</c> and automatically to text browsers. Not a courtesy: it's the test of
/// whether a fact is really being communicated. If something can't survive here, its graphic on the
/// main page is decoration.
/// </para>
/// <para>
/// Renders from the same view models the graphical pages use, so the main pages are these with
/// graphics added rather than a second document to keep in step. No prose here is wider than 80
/// columns — addresses excepted, see <see cref="Columns"/> — and every state is a word, never a
/// glyph, colour or cell shape.
/// </para>
/// </remarks>
public static class PlainText
{
    /// <summary>
    /// No prose this renderer writes exceeds this, because text browsers are 80 wide.
    /// </summary>
    /// <remarks>
    /// <b>URLs are the one exception, deliberately.</b> An address prints whole because a wrapped
    /// one isn't clickable in the browsers this surface exists for. Everything else wraps
    /// (<see cref="Wrap"/>); <c>NoPlainLineIsWiderThanEightyColumns</c> enforces exactly that split.
    /// </remarks>
    public const int Columns = 80;

    /// <summary>
    /// An address this surface prints, in the locale it is printing.
    /// </summary>
    /// <remarks>
    /// There are no anchors here, so a path is printed for a reader to type, follow or paste — which
    /// makes them links subject to the same rule: a German reader copying <c>/g/ashen-court</c> off
    /// <c>/de/games?plain=1</c> must not arrive in English.
    /// </remarks>
    private static string Path(string tag, string address) => LocaleRouting.Link(tag, address);

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
        // Named individually rather than folded into [archived]: a script parsing this page needs
        // the real reason a game is withheld, not a generic label.
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

        // Absence is written out, never left blank — a blank reads as zero to a parser just as it
        // does to a human. It names no cause: a null count covers an unmeasured game, an uncountable
        // probe, and a count older than the window alike, and this surface can't tell them apart
        // any better than the graphical hero can.
        b.AppendLine((s.PlayersNow is { } n
            ? $"{Say(tag, "game.plain.playersNow", ("count", n))}  {Label(tag, s.PlayersNowProvenance, now)}"
            : Say(tag, "game.plain.playersNoCount")).TrimEnd());

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

        AppendReach(b, page, now, tag);
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
    /// The heatmap in words: the sentence first, then a line per day. The three states of spec
    /// §5.4 are three different words here and never share one.
    /// </summary>
    private static void AppendActivity(StringBuilder b, IReadOnlyList<ActivityCell> cells, string tag)
    {
        if (cells.Count == 0)
        {
            return;
        }

        Heading(b, Say(tag, "activity.plain.heading"));

        // Same threshold the graphical page draws on: below it, a week of prose about two measured
        // hours would describe a shape the measurements don't have.
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

        // The key. Reuses the site's own words rather than a third spelling invented for this
        // surface — "no data" used to say here what "not measured" says everywhere else. Wrapped
        // rather than padded to a column, since a language's word for "uncounted" might run long.
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
    /// Weeks rather than days, from <see cref="TrendSeries.PerWeek"/> — a quarter is ninety days and
    /// nobody reads ninety lines. The seek links are here too, since a range is part of the address:
    /// a text browser that saw the window but couldn't change it would have the graphic's navigation
    /// and none of its function, the decoration §9 tests for.
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

        // Same order as the matrix: disagreements first, then measured-present, absent, unknown.
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

    /// <summary>
    /// The links, as addresses. The graphical page draws nine icons; this is what they say.
    /// </summary>
    /// <remarks>
    /// <b>The stored value, not the normalised href.</b> A reader is handed what the game published;
    /// showing "https://discord.gg/x" where the row says "discord.gg/x" would be quietly repairing a
    /// fact. The rendered page links the normalised form because a browser needs one; text needs the
    /// truth.
    /// </remarks>
    private static void AppendReach(StringBuilder b, GamePage page, DateTimeOffset now, string tag)
    {
        if (page.Links.Count == 0)
        {
            return;
        }

        Heading(b, Say(tag, "links.plain.heading"));

        foreach (var link in page.Links)
        {
            b.AppendLine($"  {link.Field,-10} {link.Shown}  {Label(tag, new ProvenanceChip(link.Shown, link.Source, link.LastConfirmedAt, link.IsStale), now)}");
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
    /// One function because four surfaces print it — four spellings of "declared six years ago"
    /// would be four chances to disagree, and <c>/games</c> and <c>/archive</c> once did. An absent
    /// chip prints nothing rather than inventing a source for an unlabelled value.
    /// <para>
    /// The word itself is <see cref="Provenance.How"/>'s, so a fifth call site (the preview
    /// metadata) can reach it too.
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
    /// The connect screen with its SGR stripped. The frame's states — suppressed, absent, too small
    /// — are stated rather than left as an absence to interpret.
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

        // Same caption the figure carries: on a game whose bytes weren't UTF-8, this is how a
        // reader learns which encoding they're looking at instead of blaming their terminal.
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
    /// Facets are here in full — every value, its count, its selecting parameter — because a text
    /// browser can't operate a <c>&lt;select&gt;</c> but can edit a URL.
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

            // Both exclusions stated: a text browser can't see the checkbox in the bar.
            + (filter.IncludeAdult ? ", adult included" : ", adult excluded"));

        // Stated, since an unlabelled sort is one a reader has to reverse-engineer from the rows —
        // exactly how a tail of games with no number gets read as a tail with no players.
        b.AppendLine($"Sorted by {FacetWords.Sort(tag, filter.Sort)}");

        // Every order it could have been in, wrapped rather than run on: nine sort tokens on one
        // line would run 130 columns off the screen.
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
            // Same break the rendered listing draws: where the sort runs out of things to rank, say
            // so rather than let the rest read as the bottom of the ranking.
            if (!broken && GameSorting.IsUnranked(g, filter.Sort))
            {
                broken = true;
                b.AppendLine(
                    $"-- {Say(tag, "listing.plain.fromHere")}: {FacetWords.Unranked(tag, filter.Sort)}");
                b.AppendLine();
            }

            // Archived and claimed are the two worth a mark; unclaimed is most of the catalogue and
            // isn't marked here either, matching the rendered row.
            var mark = g.State is LifecycleState.Archived ? $"  [{Say(tag, "listing.plain.archived")}]"
                : g.IsClaimed ? $"  [{Say(tag, "listing.plain.claimed")}]"
                : string.Empty;
            b.AppendLine($"{g.Name}{mark}");
            b.AppendLine($"  {Path(tag, $"/g/{g.Slug}")}");

            // Same words and relative age the game page uses. This used to hard-code "(measured)"
            // over every count including declared ones — rule 5 broken by a format string.
            b.AppendLine((g.PlayersNow is { } n
                ? $"  Players now: {n}   {Label(tag, g.PlayersNowProvenance, now)}"
                : "  Players now: unknown (no count could be measured)").TrimEnd());

            // Only where there's a window sort; otherwise every row of an alphabetical listing
            // would print "Ranked on: —".
            if (g.PlayersOverWindow is { } window)
            {
                b.AppendLine($"  Ranked on:   {FacetWords.Window(tag, window, filter.Sort)}");
            }

            // Never blank: "we could not identify it" is a measurement, a missing line is not.
            b.AppendLine((g.Codebase is { } codebase
                ? $"  Codebase:    {codebase}  {Label(tag, g.CodebaseProvenance, now)}"
                : "  Codebase:    not identified").TrimEnd());

            b.AppendLine(g.MeasuredProtocols.Count > 0
                ? $"  Measured:    {string.Join(", ", g.MeasuredProtocols)}"
                : "  Measured:    nothing offered in the handshake");

            // "Never" is its own sentence rather than the oldest bucket: a game with no answer yet
            // has no date, and inventing one from first sighting would read as an outage.
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
    /// The two sentences at the top aren't blurb: an unticked protocol isn't a game declining it,
    /// and no value isn't a no — the two readings this design exists to prevent.
    /// </remarks>
    private static void AppendFacets(string tag, StringBuilder b, IReadOnlyList<FacetGroup> facets)
    {
        if (facets.Count == 0)
        {
            return;
        }

        Heading(b, "FILTERS");
        // Enumerated rather than spelled out, so a new register added elsewhere can't quietly be
        // left out here.
        Wrap(b, "Each facet is marked "
            + string.Join(", ", Enum.GetValues<FacetEvidence>()
                .Select(e => $"{FacetWords.Evidence(tag, e)} ({FacetWords.EvidenceMeaning(tag, e)})"))
            + ".");
        b.AppendLine();
        Wrap(b, "Counts are exact, from the same query as the list below. A blank is a gap in our "
            + "measurement, never a \"no\": each facet spells its own. A measured zero is a count; "
            + "an unknown count is not a zero and never sorts as one.");
        b.AppendLine();

        // Two of the three states are a filter the reader applied; drawing them the same way would
        // publish one as the other.
        Wrap(b, Say(tag, "facet.plain.marks"));

        foreach (var group in facets)
        {
            b.AppendLine();

            // The rendered panel gathers these two facets under one heading and note; here they're
            // two groups in a list of nine, so the note travels with the first of them rather than
            // living only on the graphical surface.
            if (string.Equals(group.Key, FacetKeys.Uncounted, StringComparison.Ordinal))
            {
                b.AppendLine($"  {Say(tag, "facet.group.measure").ToUpperInvariant()}");
                Wrap(b, Say(tag, "facet.measure.note"), "  ");
                b.AppendLine();
            }

            b.AppendLine($"  {FacetWords.Group(tag, group.Key)}"
                + $" — {FacetWords.Evidence(tag, group.Evidence)}  (?{group.Key}=…)");

            foreach (var value in group.Values)
            {
                var words = FacetWords.Value(tag, group.Key, value);
                var gloss = string.Equals(words, value.Token, StringComparison.Ordinal)
                    ? string.Empty
                    : "  " + words;

                // A mark, not a colour: the selected value has to be visible where there is no ink.
                // Three marks, because the facet has three states — a shared star for included and
                // excluded once drew "only these" and "anything but these" identically.
                var mark = value.State switch
                {
                    FacetState.Included => '*',
                    FacetState.Excluded => '-',
                    _ => ' ',
                };

                b.AppendLine($"  {mark} {value.Token,-24}{value.Count,5}{gloss}".TrimEnd());
            }
        }
    }

    /// <summary>
    /// The three liveness feeds. All three are the same shape here: the register the graphical
    /// cards carry is a tone, not a fact, so the words have to do the work.
    /// </summary>
    public static string RenderFeeds(string tag, LivenessFeeds feeds, DateTimeOffset now)
    {
        var b = new StringBuilder();

        // Empty states reuse the graphical cards' ids so the two surfaces can't disagree; headings
        // are plain-only ids, since the card's own kicker carries "— still probed" that lives
        // elsewhere on this surface.
        Feed(b, tag, "feed.plain.newlyDiscovered", feeds.NewlyDiscovered, "feed.nothingNew", now);
        Feed(b, tag, "feed.plain.wentDark", feeds.WentDark, "feed.nothingDark", now);
        Feed(b, tag, "feed.plain.cameBack", feeds.CameBack, "feed.nothingBack", now);

        return b.ToString();

        static void Feed(
            StringBuilder b,
            string tag,
            string title,
            IReadOnlyList<FeedEntry> entries,
            string empty,
            DateTimeOffset now)
        {
            // Uppercased here rather than in the bundle, like every other heading on this surface: a
            // script with no case is left exactly as its translator wrote it.
            b.AppendLine(Say(tag, title).ToUpperInvariant());

            if (entries.Count == 0)
            {
                b.AppendLine($"  {Say(tag, empty)}");
                b.AppendLine();
                return;
            }

            foreach (var e in entries)
            {
                b.AppendLine(
                    $"  {e.Name}  ({Relative.Ago(tag, now - e.At)})  {Path(tag, $"/g/{e.Slug}")}");
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

        // The wordmark stays out of the bundle, like a hostname — everything under it is a sentence
        // and goes through it.
        b.AppendLine("MU*INDEX");
        b.AppendLine();
        b.AppendLine(Say(tag, "home.plain.known", ("count", counts.Known)));
        b.AppendLine(Say(tag, "home.plain.connectedNow", ("count", counts.WithPlayersOn)));
        b.AppendLine(Say(tag, "home.plain.uncounted", ("count", counts.CountUnknown)));
        b.AppendLine(Say(tag, "home.plain.archived", ("count", counts.Archived)));

        // Same three facts as the rendered strip, plus the registry line it has no room for. Omitted
        // entirely when there's nothing measured to say, matching the demo path.
        if (pulse.State(now) is not CrawlState.NotYet)
        {
            b.AppendLine();
            b.AppendLine(CrawlerCopy.State(tag, pulse, now));

            if (CrawlerCopy.LastCycle(tag, pulse) is { } cycle)
            {
                b.AppendLine(cycle);
            }

            b.AppendLine(CrawlerCopy.Registry(tag, pulse));
        }

        b.AppendLine();

        b.Append(RenderFeeds(tag, feeds, now));
        return b.ToString();
    }

    /// <summary>The crawler status page: the same pulse the strip carries, plus the history it has no room for.</summary>
    public static string RenderCrawler(
        string tag,
        CrawlerPulse pulse,
        IReadOnlyList<CrawlCycleRecord> recent,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(pulse);
        ArgumentNullException.ThrowIfNull(recent);

        var b = new StringBuilder();

        b.AppendLine(Messages.For(tag, "crawler.page.title"));
        b.AppendLine();
        Wrap(b, Messages.For(tag, "crawler.page.lede"), string.Empty);
        b.AppendLine(Messages.For(tag, "crawler.page.aboutLink") + ": " + Path(tag, "/about"));
        b.AppendLine();

        if (pulse.State(now) is CrawlState.NotYet)
        {
            b.AppendLine(Messages.For(tag, "crawler.page.empty"));
            return b.ToString();
        }

        b.AppendLine(CrawlerCopy.State(tag, pulse, now));
        b.AppendLine(CrawlerCopy.Registry(tag, pulse));

        if (CrawlerCopy.FullCycle(tag, pulse) is { } cycle)
        {
            b.AppendLine(cycle);

            if (CrawlerCopy.CycleFinishedAt(tag, pulse) is { } finished)
            {
                b.AppendLine(finished);
            }
        }

        b.AppendLine();
        b.AppendLine(Messages.For(tag, "crawler.history.title"));

        if (recent.Count == 0)
        {
            b.AppendLine(Messages.For(tag, "crawler.history.empty"));
        }
        else
        {
            foreach (var past in recent)
            {
                b.AppendLine($"  {Dates.Stamp(tag, past.FinishedAt)} — {CrawlerCopy.Cycle(tag, past)}");
            }
        }

        return b.ToString();
    }

    /// <summary>The archive. Past tense, no alarm, and the run of years given as a fact.</summary>
    /// <remarks>
    /// The label column is measured rather than hard-spaced: a fixed width holds for exactly one
    /// language, and a locale with a longer word for "last reachable" would run into its value.
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
            b.AppendLine($"  {Path(tag, $"/g/{entry.Summary.Slug}")}");
            b.AppendLine($"  {labels[0].PadRight(width)}{entry.LastAnswered(tag)} "
                + Say(tag, "archive.darkFor", ("age", entry.DarkFor(tag, now))));
            b.AppendLine($"  {labels[1].PadRight(width)}"
                + Say(tag, "archive.plain.knownLiveValue", ("value", entry.KnownLiveWording(tag))));

            if (entry.Run(tag) is { } run)
            {
                b.AppendLine($"  {labels[2].PadRight(width)}{run}");
            }

            // Labelled here above all: nobody has confirmed an archived game's codebase since it
            // stopped answering, and this used to read "Codebase: PennMUSH 1.8.5" flat while the
            // listing called the same value three years unconfirmed.
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
    /// The attribution list has to survive here above all — it's what this project owes the
    /// directories it read, and an acknowledgement a text browser can't render isn't one.
    /// </remarks>
    public static string RenderAbout(AboutPage page, string tag = Locales.SourceTag)
    {
        ArgumentNullException.ThrowIfNull(page);

        var b = new StringBuilder();

        // Upper-cased from the translated title rather than typed in capitals: a script with no
        // case is left unchanged, since the shape is English typography and the words are not.
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

        }

        return b.ToString();
    }

    /// <summary>
    /// The ecosystem dashboard, which is the page whose graphic is most obviously an illustration.
    /// </summary>
    /// <remarks>
    /// Every bar illustrates a sentence complete without it: "PennMUSH — 122 of 310 (39.4%)" is the
    /// fact, the bar is a way to see several at once. Only the seeing-at-once is lost here.
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

        // The graphic folds these behind a disclosure; this surface has no disclosure, so it prints
        // them outright.
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
    /// The form itself isn't rendered here, and isn't missing either — two text boxes and a button
    /// are already plain text, so the page keeps the real form beneath this block. This renders what
    /// could otherwise have been carried by layout: what happens to an address, and what happened
    /// to the last one.
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
                b.AppendLine($"  {link.Label}: {Path(tag, link.Href)}");
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
    /// Built from the same <see cref="FindScreen"/> the rendered page draws. This used to be a
    /// different page that dumped ten facet groups while the rendered page asked six questions, and
    /// the two disagreed about what could be asked at all.
    /// <para>
    /// The addresses differ from the rendered page's only because the querystring they're built
    /// from carries <c>plain=1</c> — every link on both surfaces is the page's own URL with one
    /// parameter changed.
    /// </para>
    /// </remarks>
    public static string RenderFind(FindScreen screen, string? tag = null)
    {
        ArgumentNullException.ThrowIfNull(screen);

        var locale = tag ?? Locales.SourceTag;
        var b = new StringBuilder();

        // Upper-cased from the translated word, so a locale with this page still gets the surface's
        // own shape.
        b.AppendLine(Say(locale, "find.title").ToUpperInvariant());

        if (screen.Error is { } problem)
        {
            // Refused rather than ignored, in the same words the rendered page uses.
            Heading(b, Say(locale, "find.refused").ToUpperInvariant());
            Wrap(b, problem, "  ");
            b.AppendLine();
            b.AppendLine("  " + Path(locale, "/find?plain=1"));
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
            b.AppendLine("      " + Path(locale, screen.ShowHref));
        }

        if (screen.Loosen is { } loosen)
        {
            b.AppendLine();
            b.AppendLine("  " + Say(
                locale, "find.drop", ("answer", loosen.Label), ("count", loosen.Count)));
            b.AppendLine("      " + Path(locale, loosen.Href));
        }

        if (screen.Answers.Count > 0)
        {
            Heading(b, Say(locale, "find.answersGiven").ToUpperInvariant());

            foreach (var chip in screen.Answers)
            {
                b.AppendLine($"  {chip.Label}");
                b.AppendLine($"      {Say(locale, "find.clear", ("question", chip.Question))}");
                b.AppendLine($"      {Path(locale, chip.ClearHref)}");
            }

            b.AppendLine();
            b.AppendLine("  " + Say(locale, "find.clearAll"));
            b.AppendLine("      " + Path(locale, screen.ClearHref));
        }

        foreach (var question in screen.Questions)
        {
            Heading(
                b,
                question.Text.ToUpperInvariant()
                    + $"  ({FacetWords.Evidence(locale, question.Evidence)})");

            if (question.Any is { } any)
            {
                Answer(b, locale, any);
            }

            // The tail is written out in full, unfolded: every option the page offers must be
            // reachable from this surface.
            foreach (var option in question.Options.Concat(question.Tail))
            {
                Answer(b, locale, option);
            }
        }

        Heading(b, Say(locale, "find.wholeListing").ToUpperInvariant());
        b.AppendLine("  " + Path(locale, "/games?plain=1"));

        return b.ToString();
    }

    /// <summary>
    /// One answer: whether it is the one in force, what it is called, what choosing it returns.
    /// </summary>
    /// <remarks>
    /// The state is a pair of characters, never a colour or indent, and the count is in the same
    /// parentheses the rendered page uses — so the two surfaces read against each other line for
    /// line.
    /// </remarks>
    private static void Answer(StringBuilder b, string tag, FindOption option)
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
            // continuation under the label, so a wrapped option can't be read as a second one.
            var wrapped = new StringBuilder();
            Wrap(wrapped, text, new string(' ', mark.Length));

            b.Append(mark).Append(wrapped.ToString().AsSpan(mark.Length));
        }

        // The address is never wrapped: see the note on Columns.
        b.AppendLine($"      {Path(tag, option.Href)}");
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

        // The three windows as addresses: a text browser can't use a tab strip, so the choice must
        // survive here (spec §9). One per line — the addresses don't fit on one inside eighty
        // columns.
        b.AppendLine("  " + Say(tag, "rankings.plain.windows"));

        // Padding width is measured rather than assumed: "7 days" is six columns, its German nine.
        var labels = RankingSpans.All.ToDictionary(s => s, s => EcosystemCopy.SpanLabel(tag, s));
        var width = labels.Values.Max(l => l.Length);
        var here = Say(tag, "rankings.plain.thisOne");

        foreach (var span in RankingSpans.All)
        {
            b.AppendLine(span == rankings.Span
                ? $"    [{labels[span].PadRight(width)}] {here}"
                : $"     {labels[span].PadRight(width)}  {Path(tag, $"/rankings?window={span.Slug()}&plain=1")}");
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
                ("window", days)) + $" · {Path(tag, $"/g/{game.Slug}")}", "       ");
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
                ("duration", Wording.Duration(spell.LengthAt(now)))) + $" · {Path(tag, $"/g/{spell.Slug}")}", "       ");
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
    /// Left in English on purpose — a machine token read like a flag in <c>ls -l</c>, in a
    /// fixed-width table where a four-syllable translation would push the row past eighty columns.
    /// The row's own labels beside it are still translated.
    /// </remarks>
    private static string Word(CapabilityState state) => state switch
    {
        CapabilityState.Present => "yes",
        CapabilityState.Absent => "NO",
        _ => "-",
    };
}

/// <summary>
/// What the front page can honestly count: every figure is a count of games we measured, so an
/// unmeasured number (like "reachable this week") is left out rather than estimated.
/// </summary>
public sealed record SiteCounts(int Known, int WithPlayersOn, int CountUnknown, int Archived)
{
    public static SiteCounts From(IReadOnlyList<GameSummary> all) => new(
        all.Count,
        all.Count(g => g.PlayersNow > 0),
        all.Count(g => g.PlayersNow is null),
        all.Count(g => g.State is LifecycleState.Archived));
}
