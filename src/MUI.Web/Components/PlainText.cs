using System.Text;
using MUI.Catalog;

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
/// to be kept in step. Nothing here is wider than 80 columns and every state is a word, never a
/// glyph, a colour or a cell shape.
/// </para>
/// </remarks>
public static class PlainText
{
    /// <summary>Nothing this renderer writes exceeds this, because text browsers are 80 wide.</summary>
    public const int Columns = 80;

    public static string Render(GamePage page, DateTimeOffset now, ReachSummary? reach = null)
    {
        var b = new StringBuilder();
        var s = page.Summary;

        b.Append(s.Name.ToUpperInvariant());
        b.Append(s.State is LifecycleState.Archived ? " [archived]" : s.IsClaimed ? " [claimed]" : " [unclaimed]");
        b.AppendLine();

        foreach (var e in page.Endpoints)
        {
            b.AppendLine($"telnet {e.Host} {e.Port}{(e.TlsMeasured ? " · tls measured" : string.Empty)}");
        }

        b.AppendLine();

        // Every state spelled as a word. "Unknown" is written out rather than left blank, because a
        // blank reads as zero to a human exactly as it does to a parser.
        b.AppendLine(s.PlayersNow is { } n
            ? $"Players now: {n}"
            : "Players now: unknown (no count could be measured)");

        if (page.ReachableFraction is { } r)
        {
            b.AppendLine($"Reachable: {Wording.Percent(r)} of the last 90 days");
        }

        if (page.LongestOutage is { } o)
        {
            b.AppendLine($"Longest outage: {Wording.Duration(o)}");
        }

        AppendActivity(b, page.Activity);
        AppendReachable(b, reach);
        AppendCapabilities(b, page);
        AppendDeclared(b, page, now);
        AppendConnectScreen(b, page);
        AppendChanges(b, page);

        return b.ToString();
    }

    /// <summary>
    /// The heatmap in words. The sentence first — it is the answer — then a line per day, which is
    /// the same content the graphical page hides behind "read as text". The three states of spec
    /// §5.4 are three different words here and never share one.
    /// </summary>
    private static void AppendActivity(StringBuilder b, IReadOnlyList<ActivityCell> cells)
    {
        if (cells.Count == 0)
        {
            return;
        }

        Heading(b, "When people are on (UTC)");
        Wrap(b, ActivitySummary.Sentence(cells));
        b.AppendLine();

        foreach (var line in ActivitySummary.PerDay(cells))
        {
            Wrap(b, line, "  ");
        }

        b.AppendLine();
        b.AppendLine("  counted   = we got in and read a number, including a measured zero");
        b.AppendLine("  uncounted = we got in and no number could be read");
        b.AppendLine("  no data   = we have no measurement for that hour");
    }

    /// <summary>The 90-day strip in words: the summary, then every spell that was not reachable.</summary>
    private static void AppendReachable(StringBuilder b, ReachSummary? reach)
    {
        if (reach is null)
        {
            return;
        }

        Heading(b, $"Reachable (last {reach.Window} days)");
        Wrap(b, reach.Sentence);

        if (reach.Spells.Count == 0)
        {
            return;
        }

        b.AppendLine();
        foreach (var spell in reach.Spells)
        {
            b.AppendLine($"  {spell}");
        }
    }

    private static void AppendCapabilities(StringBuilder b, GamePage page)
    {
        Heading(b, $"Capabilities ({page.DisagreementCount} of {page.Capabilities.Count} disagree)");

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
            var flag = c.Disagrees ? "  ** disagree" : string.Empty;
            b.AppendLine($"  {c.Protocol,-10} measured: {Word(c.Measured),-7} declared: {Word(c.Declared)}{flag}");
        }
    }

    private static void AppendDeclared(StringBuilder b, GamePage page, DateTimeOffset now)
    {
        if (page.Declared.Count == 0)
        {
            return;
        }

        Heading(b, "What the game says about itself");

        foreach (var (name, chip) in page.Declared)
        {
            var age = Relative.Format(now - chip.LastConfirmedAt);
            var how = chip.IsMeasured ? "measured" : "declared";
            b.AppendLine($"  {name,-10} {chip.Value}  ({how}, {age}{(chip.IsStale ? ", stale" : string.Empty)})");
        }
    }

    /// <summary>
    /// The connect screen with its SGR stripped. Colour codes are never announced, and the three
    /// cases the frame has — suppressed, too small, oversized — are stated rather than left as an
    /// absence for the reader to interpret.
    /// </summary>
    private static void AppendConnectScreen(StringBuilder b, GamePage page)
    {
        var screen = Ansi.Parse(page.ConnectScreen, page.ConnectScreenSuppressed);

        Heading(b, "What you see when you connect");

        switch (screen.State)
        {
            case AnsiScreenState.Suppressed:
                b.AppendLine("  The owner asked us not to republish this game's connect screen.");
                return;

            case AnsiScreenState.Absent:
                b.AppendLine("  No connect screen has been captured from this game.");
                return;

            case AnsiScreenState.TooSmall:
                b.AppendLine($"  Only {screen.RowCount} row(s) came back — too little to show.");
                return;
        }

        b.AppendLine($"  [connect screen: {screen.RowCount} lines, text only]");
        if (screen.IsOversized)
        {
            b.AppendLine($"  Unusually long; the graphical page shows the first {Ansi.CropRows}.");
        }

        b.AppendLine();
        foreach (var row in screen.Rows)
        {
            b.Append("  ").AppendLine(row.Text.TrimEnd());
        }
    }

    private static void AppendChanges(StringBuilder b, GamePage page)
    {
        if (page.Changes.Count == 0)
        {
            return;
        }

        Heading(b, "What changed");
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
    public static string RenderListing(GameListing listing, GameFilter filter, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentNullException.ThrowIfNull(filter);

        var games = listing.Games;
        var b = new StringBuilder();

        b.AppendLine("GAMES");
        b.AppendLine($"{games.Count} game(s)"
            + (string.IsNullOrWhiteSpace(filter.Text) ? string.Empty : $" matching \"{filter.Text}\"")
            + (filter.IncludeArchived ? ", archived included" : ", archived excluded"));

        AppendFacets(b, listing.Facets);
        b.AppendLine();

        if (games.Count == 0)
        {
            b.AppendLine("Nothing matched. Nothing is ever deleted here, so a name that once");
            b.AppendLine("worked still does — try fewer words, or drop a filter.");
            return b.ToString();
        }

        foreach (var g in games)
        {
            var mark = g.State is LifecycleState.Archived ? "[archived]" : g.IsClaimed ? "[claimed]" : "[unclaimed]";
            b.AppendLine($"{g.Name}  {mark}");
            b.AppendLine($"  /g/{g.Slug}");
            b.AppendLine(g.PlayersNow is { } n
                ? $"  Players now: {n}   (measured)"
                : "  Players now: unknown (no count could be measured)");

            // Never blank. "We could not identify it" is a measurement and a missing line is not.
            b.AppendLine(g.Codebase is { } codebase
                ? $"  Codebase:    {codebase}"
                : "  Codebase:    not identified");

            b.AppendLine(g.MeasuredProtocols.Count > 0
                ? $"  Measured:    {string.Join(", ", g.MeasuredProtocols)}"
                : "  Measured:    nothing offered in the handshake");

            // The last-seen facet's own column. Never once reached is its own sentence rather than
            // the oldest bucket, because a game we have never got an answer from has no date and
            // inventing one from our first sighting would read as its outage.
            b.AppendLine(g.LastReachableAt is { } seen
                ? $"  Last reached: {Relative.Format(now - seen)} ago"
                : "  Last reached: never — we have not once got an answer from it");

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
    private static void AppendFacets(StringBuilder b, IReadOnlyList<FacetGroup> facets)
    {
        if (facets.Count == 0)
        {
            return;
        }

        Heading(b, "FILTERS");
        Wrap(b, "Each count is what choosing that value returns, from the same query as the list "
            + "below. A protocol is listed when we saw a game offer it, so a game missing from one "
            + "may simply never have been measured for it and is never a \"no\". Where a facet has "
            + "no value for a game it says so in its own words, and that is not a no either.");

        foreach (var group in facets)
        {
            b.AppendLine();
            b.AppendLine($"  {FacetWords.Group(group.Key)}"
                + $" — {FacetWords.Evidence(group.Evidence)}  (?{group.Key}=…)");

            foreach (var value in group.Values)
            {
                var words = FacetWords.Value(group.Key, value);
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
    public static string RenderFeeds(LivenessFeeds feeds, DateTimeOffset now)
    {
        var b = new StringBuilder();

        Feed(b, "NEWLY DISCOVERED", feeds.NewlyDiscovered, "Nothing new since we last looked.", now);
        Feed(b, "WENT DARK", feeds.WentDark, "Nothing has stopped answering.", now);
        Feed(b, "CAME BACK", feeds.CameBack, "Nothing has come back yet. We keep knocking.", now);

        return b.ToString();

        static void Feed(StringBuilder b, string title, IReadOnlyList<FeedEntry> entries, string empty, DateTimeOffset now)
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
                b.AppendLine($"  {e.Name}  ({Relative.Format(now - e.At)} ago)  /g/{e.Slug}");
                Wrap(b, e.Detail, "    ");
            }

            b.AppendLine();
        }
    }

    /// <summary>The home page: what we know, then what changed.</summary>
    public static string RenderHome(SiteCounts counts, LivenessFeeds feeds, DateTimeOffset now)
    {
        var b = new StringBuilder();

        b.AppendLine("MU*INDEX");
        b.AppendLine("Every game here was checked by a machine, and every fact says when.");
        b.AppendLine();
        b.AppendLine($"{counts.Known} games known");
        b.AppendLine($"{counts.WithPlayersOn} with players on right now (measured)");
        b.AppendLine($"{counts.CountUnknown} answering with nothing we can count");
        b.AppendLine($"{counts.Archived} archived — still probed, still addressable");
        b.AppendLine();

        b.Append(RenderFeeds(feeds, now));
        return b.ToString();
    }

    /// <summary>The archive. Past tense, no alarm, and the run of years given as a fact.</summary>
    public static string RenderArchive(IReadOnlyList<ArchiveEntry> entries, string? query, DateTimeOffset now)
    {
        var b = new StringBuilder();

        b.AppendLine("THE ARCHIVE");
        Wrap(b, "Games that have stopped answering. Nothing here was deleted: every page, URL and "
            + "series survives, we still try the door every week, and one successful probe puts a "
            + "game back in the listing.");
        b.AppendLine();
        b.AppendLine($"{entries.Count} game(s)"
            + (string.IsNullOrWhiteSpace(query) ? string.Empty : $" matching \"{query}\""));
        b.AppendLine();

        foreach (var entry in entries)
        {
            b.AppendLine($"{entry.Summary.Name}  [archived]");
            b.AppendLine($"  /g/{entry.Summary.Slug}");
            b.AppendLine($"  Last reachable:  {entry.LastAnswered} ({entry.DarkFor(now)} ago)");
            b.AppendLine($"  Known live:      {entry.KnownLiveWording} of measured reachable time");

            if (entry.Run is { } run)
            {
                b.AppendLine($"  Run:             {run}");
            }

            if (entry.Summary.Codebase is { } codebase)
            {
                b.AppendLine($"  Codebase:        {codebase}");
            }

            b.AppendLine();
        }

        if (entries.Count == 0)
        {
            b.AppendLine("Nothing matched.");
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
    public static string RenderAbout(AboutPage page)
    {
        var b = new StringBuilder();

        b.AppendLine("ABOUT MU*INDEX");
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
                Wrap(b, identity.Wording, "  ");
                b.AppendLine();
                Wrap(b, $"Crawler: {identity.Name}", "  ");
                Wrap(b, $"Contact: {identity.InfoUrl}", "  ");

                if (!identity.ContactConfigured)
                {
                    Wrap(b, "This deployment has not set a contact address, so the one above is "
                        + "the built-in placeholder and answers nobody.", "  ");
                }
            }

            foreach (var source in section.Sources)
            {
                b.AppendLine();
                b.AppendLine($"  {source.Name} — {source.StatusWording}");
                b.AppendLine($"  {source.Url}");
                Wrap(b, source.Note, "    ");
            }

            if (section.Licence is { } licence)
            {
                b.AppendLine();
                // Every one of these goes through the wrapper rather than being laid out in columns:
                // a licence name and an attribution are both configuration, and a deployment that
                // sets a long one must not push a line off the side of a text browser.
                Wrap(b, $"Code: {licence.CodeLicence}", "  ");
                Wrap(b, $"Data: {licence.DataLicenceName}", "  ");

                if (licence.DataLicenceUrl is { } url)
                {
                    Wrap(b, url, "  ");
                }

                Wrap(b, "(what this deployment serves. The project's own answer is still open.)", "  ");
                Wrap(b, $"Credit as: {licence.Attribution}", "  ");
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
    public static string RenderEcosystem(EcosystemDashboard dashboard, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(dashboard);

        var b = new StringBuilder();

        b.AppendLine("THE ECOSYSTEM");
        Wrap(b, EcosystemCopy.NoTotals);
        b.AppendLine();
        Wrap(b, $"{dashboard.ListedGames} games listed · "
            + $"{EcosystemCopy.Handshakes(dashboard.Handshakes)} · "
            + $"{EcosystemCopy.MsspReports(dashboard.MsspReports)}.");

        if (dashboard.OldestHandshake is { } oldest)
        {
            Wrap(b, "The oldest handshake in this picture was last confirmed "
                + $"{Relative.Format(now - oldest)} ago.");
        }

        Heading(b, "CODEBASES");
        Wrap(b, $"Share of the {dashboard.Codebases.Identified} listed games that told us what they "
            + "run. A game whose codebase we could not read is counted as nothing at all, and never "
            + "as something else.");
        b.AppendLine();

        foreach (var family in dashboard.Codebases.Families)
        {
            b.AppendLine($"  {family.Label,-24} {EcosystemCopy.Share(family)}");
        }

        if (dashboard.Codebases.Families.Count == 0)
        {
            b.AppendLine("  No listed game has told us its codebase yet.");
        }

        if (dashboard.Codebases.NotIdentified > 0)
        {
            b.AppendLine();
            b.AppendLine($"  {dashboard.Codebases.NotIdentified} listed game(s) have not told us one.");
        }

        Heading(b, "PROTOCOLS");
        Wrap(b, EcosystemCopy.Floor);
        b.AppendLine();

        foreach (var protocol in dashboard.Protocols)
        {
            b.AppendLine($"  {protocol.Protocol}");
            Wrap(b, $"measured: {EcosystemCopy.Measured(protocol)}", "    ");
            Wrap(b, $"declared: {EcosystemCopy.Declared(protocol)}", "    ");
        }

        b.AppendLine();
        Wrap(b, $"Measured is of {EcosystemCopy.Handshakes(dashboard.Handshakes)}; declared is of "
            + $"{EcosystemCopy.MsspReports(dashboard.MsspReports)}. Two denominators, because they "
            + "are two different sets of games.");

        Heading(b, "WHY THIS IS A SNAPSHOT AND NOT A CURVE");
        Wrap(b, EcosystemCopy.NoCurve);
        b.AppendLine();
        Wrap(b, EcosystemCopy.Transitions(dashboard.CapabilityTransitions));

        return b.ToString();
    }

    /// <summary>The rankings, with every basis in the same words the rendered page uses.</summary>
    public static string RenderRankings(Rankings rankings, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(rankings);

        var b = new StringBuilder();

        b.AppendLine("RANKINGS");
        Wrap(b, EcosystemCopy.NoVote);

        Heading(b, $"BUSIEST — median measured players, last {(int)rankings.Window.TotalDays} days");
        Wrap(b, EcosystemCopy.BusiestBasis(rankings));
        b.AppendLine();

        if (rankings.Busiest.Count == 0)
        {
            Wrap(b, "No listed game has produced enough counted samples to be ranked yet. That is a "
                + "statement about how long we have been measuring and not about how busy anybody "
                + "is.", "  ");
        }

        var place = 0;

        foreach (var game in rankings.Busiest)
        {
            place++;
            b.AppendLine($"  {place,3}  {game.Name}");
            b.AppendLine($"       median {game.Median} · peak {game.Peak} · "
                + $"{game.Samples} counted samples · /g/{game.Slug}");
        }

        Heading(b, "LONGEST UNBROKEN REACHABLE SPELL");
        Wrap(b, EcosystemCopy.SpellBasis);
        b.AppendLine();

        if (rankings.LongestUnbroken.Count == 0)
        {
            Wrap(b, "No listed game is in an unbroken reachable spell right now.", "  ");
        }

        place = 0;

        foreach (var spell in rankings.LongestUnbroken)
        {
            place++;
            b.AppendLine($"  {place,3}  {spell.Name}");
            b.AppendLine($"       reachable on every probe since {spell.Since:d MMMM yyyy} · "
                + $"{Wording.Duration(spell.LengthAt(now))} · /g/{spell.Slug}");
        }

        return b.ToString();
    }

    private static void Heading(StringBuilder b, string title)
    {
        b.AppendLine();
        b.AppendLine(title);
    }

    /// <summary>Wraps to 80 columns, because that is the width a text browser has.</summary>
    private static void Wrap(StringBuilder b, string text, string indent = "")
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

    /// <summary>Colour is never the only carrier of a state, and here there is no colour at all.</summary>
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
