namespace MUI.Catalog.Persistence;

/// <summary>
/// Capability field names. A capability is stored twice under two names rather than once under one
/// (spec §5.1, §9): <c>capability.gmcp.measured</c> is what the telnet handshake offered, and
/// <c>capability.gmcp.declared</c> is what the game's MSSP claims.
/// </summary>
/// <remarks>
/// Keeping them apart under two names, each with its own source row, is what lets the game page say
/// "declared GMCP, not offered in 214 handshakes". The disagreement is the interesting fact and must
/// not be hidden, so the two values must never contend for one row.
/// </remarks>
public static class CapabilityFields
{
    public const string Prefix = "capability.";
    public const string MeasuredSuffix = ".measured";
    public const string DeclaredSuffix = ".declared";

    /// <summary>The capabilities worth carrying both sides of (spec §6.1 for the measured set).</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        "ANSI", "ATCP", "CHARSET", "EOR", "GMCP", "MCCP", "MCP", "MSDP", "MSP", "MSSP", "MXP",
        "NAWS", "NEW-ENVIRON", "PUEBLO", "TLS", "TTYPE", "UTF-8", "VT100",
        "XTERM 256 COLORS", "ZMP",
    ];

    /// <summary>
    /// Spellings that name a capability this list already carries under another name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two vocabularies meet here, and without this they produced two half-empty rows.</b> The
    /// handshake names what it negotiated — the telnet option is MCCP2 — and MSSP's variable is the
    /// version-less <c>MCCP</c>. So the dashboard showed <c>MCCP2</c> measured by 87 games and
    /// declared by none, above <c>MCCP</c> declared by 35 and never measured, which is one fact
    /// about compression split into two rows that each look like a finding of absence. The whole
    /// point of holding measured beside declared is the comparison, and a comparison cannot happen
    /// across two rows.
    /// </para>
    /// <para>
    /// <c>SSL</c> is the same shape between two MSSP variables rather than between two protocols:
    /// the specification names <c>SSL</c>, games also write <c>TLS</c>, and both mean "there is an
    /// encrypted port". 31 games said one and 6 said the other, in separate rows, and 6 said both.
    /// The word each game used is not lost — a value carrying more than a yes still writes its own
    /// descriptive row, so <c>SSL 4202</c> survives verbatim; it is only the boolean that merges.
    /// </para>
    /// <para>
    /// <b>Only spellings actually observed are here.</b> MCCP3 is a distinct telnet option that no
    /// game in this catalogue has offered, and adding it on the strength of the pattern would be
    /// compiling in a guess. If one appears it gets a row of its own, which is visible and
    /// correctable, rather than being folded somewhere by a rule nobody checked.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mccp2"] = "MCCP",
            ["ssl"] = "TLS",
        };

    /// <summary>
    /// The <see cref="Names"/> entry a protocol or MSSP variable names, or null for one we do not
    /// carry a column for.
    /// </summary>
    /// <remarks>
    /// Folded on case and on the separators the two vocabularies spell differently —
    /// TelnetNegotiationCore's <c>NEW-ENVIRON</c> against MSSP's <c>XTERM 256 COLORS</c> — and then
    /// through <see cref="Aliases"/>, so one capability is one field however it arrived. It lives
    /// beside the names rather than in the crawler because the read path has to agree with the write
    /// path about what a capability is, and only one of them observes a socket.
    /// </remarks>
    public static string? Canonical(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var folded = Fold(name);

        return Aliases.TryGetValue(folded, out var alias)
            ? alias
            : Names.FirstOrDefault(known => Fold(known) == folded);
    }

    private static string Fold(string name) =>
        name.Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');

    public static string Measured(string capability) => Prefix + Normalise(capability) + MeasuredSuffix;

    public static string Declared(string capability) => Prefix + Normalise(capability) + DeclaredSuffix;

    /// <summary>
    /// The capability a field name refers to, or null if it names no capability. The inverse of
    /// <see cref="Measured"/> and <see cref="Declared"/>, so a row read back can be put in the right
    /// column of the matrix without a second convention for parsing these names.
    /// </summary>
    public static string? CapabilityOf(string field)
    {
        if (SuffixOf(field) is not { } suffix)
        {
            return null;
        }

        var slug = field[Prefix.Length..^suffix.Length];

        return Names.FirstOrDefault(name => Normalise(name) == slug);
    }

    /// <summary>
    /// The capability a field names, spelled the way a reader should see it, or null if it names no
    /// capability at all.
    /// </summary>
    /// <remarks>
    /// <see cref="CapabilityOf"/> answers only for capabilities this class declares, which is the
    /// right answer where a column of the matrix is at stake. An aggregate over the whole catalogue
    /// needs the other one: a server naming a protocol nobody listed here is <em>still measured and
    /// still stored</em> — <c>FieldObservations</c> refuses to drop an observation because no column
    /// renders it — so a dashboard that knew only the declared set would silently discard real
    /// measurements. The name the server used is returned in that case, folded back out of the slug,
    /// so <c>capability.zmp3.measured</c> reads as <c>ZMP3</c>.
    /// <para>
    /// <b>It deliberately does not apply <see cref="Aliases"/>.</b> Aliasing happens once, on the
    /// way in, and <c>0017_merge_capability_aliases.sql</c> moved the rows already written under an
    /// alias — so every stored row is under its canonical name and folding again here would buy
    /// nothing. It would also cost something: the dashboard tallies games per field name, so two
    /// field names arriving at one capability would add rather than merge, and a game offering both
    /// spellings would be counted twice in its own share.
    /// </para>
    /// </remarks>
    public static string? NameOf(string field)
    {
        if (CapabilityOf(field) is { } known)
        {
            return known;
        }

        var suffix = SuffixOf(field);

        if (suffix is null)
        {
            return null;
        }

        var slug = field[Prefix.Length..^suffix.Length];

        return slug.Length == 0 ? null : slug.Replace('-', ' ').ToUpperInvariant();
    }

    public static bool IsMeasured(string field) =>
        field.StartsWith(Prefix, StringComparison.Ordinal)
        && field.EndsWith(MeasuredSuffix, StringComparison.Ordinal);

    /// <summary>Which side of the matrix a field name is on, or null if it is on neither.</summary>
    private static string? SuffixOf(string field)
    {
        if (!field.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        return field.EndsWith(MeasuredSuffix, StringComparison.Ordinal) ? MeasuredSuffix
            : field.EndsWith(DeclaredSuffix, StringComparison.Ordinal) ? DeclaredSuffix
            : null;
    }

    private static string Normalise(string capability) =>
        capability.Trim().ToLowerInvariant().Replace(' ', '-');
}

/// <summary>
/// Fields the catalogue stores for its own machinery and never presents as something a game said.
/// </summary>
/// <remarks>
/// <para>
/// A <c>GameField</c> row is not by itself a public fact. Some are working state — a fingerprint the
/// de-duplicator compares on, the connect screen the page renders in its own frame — and putting
/// them in the game's self-description panel is wrong twice over: <c>banner_hash</c> is a 64-character
/// digest <em>we</em> computed rather than anything the game claims about itself, and it is
/// meaningless to a reader besides.
/// </para>
/// <para>
/// The check is a list rather than a naming convention because the alternative already failed here:
/// the panel filtered on a <c>connect_screen</c> prefix inlined at the call site, which is a rule
/// that only ever excludes the fields whoever wrote it happened to remember. <c>banner_hash</c> is
/// not in the field registry at all — an unregistered field is stored like any other by design — so
/// nothing else was going to catch it either.
/// </para>
/// </remarks>
public static class InternalFields
{
    /// <summary>A digest of the connect screen, compared by the identity matcher (spec §5.7).</summary>
    public const string BannerHash = "banner_hash";

    /// <summary>The connect screen itself. Rendered in its own frame, not as a self-report.</summary>
    public const string ConnectScreen = "connect_screen";

    /// <summary>
    /// Whether the owner asked us to stop republishing the screen (spec §11).
    /// </summary>
    /// <remarks>
    /// Machinery, and emphatically not something the game said: it is a decision of <em>ours</em>
    /// about what to display, taken on request and with no questions asked. It is written by
    /// <see cref="OwnerEnrichment"/> and read by every surface that would otherwise render the
    /// screen.
    /// </remarks>
    public const string ConnectScreenSuppressed = "connect_screen_suppressed";

    private static readonly HashSet<string> Names = new(StringComparer.Ordinal)
    {
        BannerHash,
        ConnectScreen,
        ConnectScreenSuppressed,
    };

    /// <summary>
    /// Whether a field is machinery. Capability rows are excluded elsewhere — they have a surface of
    /// their own — so this covers only fields with no public rendering at all.
    /// </summary>
    public static bool IsInternal(string field) =>
        Names.Contains(field) || field.StartsWith(ConnectScreen, StringComparison.Ordinal);

    /// <summary>
    /// The exact names, for a caller that has to apply this rule somewhere other than in C#.
    /// </summary>
    /// <remarks>
    /// The change feed is filtered in SQL rather than after the read, because the query is limited
    /// and a filter applied afterwards would spend that limit on rows nobody may see — a game whose
    /// owner toggled their connect screen twenty times would have shown an empty feed. Exposed
    /// rather than restated so the two halves of <see cref="IsInternal"/> keep one spelling; the
    /// other half is a prefix on <see cref="ConnectScreen"/>.
    /// </remarks>
    public static IReadOnlyList<string> ExactNames { get; } = [.. Names];
}

/// <summary>
/// Every descriptive field this site stores, and how long each may go unconfirmed (spec §5.6).
/// </summary>
/// <remarks>
/// <para>
/// The windows are calibrated against the two anchors the spec argues from: a player count is stale
/// in hours, and a hand-typed <c>GENRE</c> is unremarkable at six months and notable at six years.
/// Everything else is placed relative to those on one question — does the codebase fill this in
/// automatically (short window, because a stale one means something went wrong at our end) or did a
/// human type it into <c>mush.cnf</c> once (long window, because sitting still is its normal state)?
/// </para>
/// <para>
/// The window lives here rather than in a front-end conditional because the API, the plain-text
/// surface and the rendered page must all agree on when a value has aged out, and only one of them is
/// a front end.
/// </para>
/// </remarks>
public sealed class FieldRegistry : IFieldRegistry
{
    /// <summary>A count moves constantly, so anything measured hourly is stale within hours.</summary>
    public static readonly TimeSpan Volatile = TimeSpan.FromHours(2);

    /// <summary>Re-measured on every probe; a day unconfirmed means our crawler, not the game.</summary>
    public static readonly TimeSpan Measured = TimeSpan.FromDays(1);

    /// <summary>Auto-filled by the codebase, and expected to change only on a move or an upgrade.</summary>
    public static readonly TimeSpan Automatic = TimeSpan.FromDays(30);

    /// <summary>Hand-typed contact details: worth chasing at a quarter, not at a month.</summary>
    public static readonly TimeSpan Contactable = TimeSpan.FromDays(90);

    /// <summary>
    /// Hand-typed description. Six months is unremarkable and six years is notable, so the window is
    /// a year: the first anchor sits inside it and the second is six times past it.
    /// </summary>
    public static readonly TimeSpan HandTyped = TimeSpan.FromDays(365);

    /// <summary>The registry is a lookup table with no state; one instance serves every caller.</summary>
    public static FieldRegistry Instance { get; } = new();

    public static IReadOnlyList<FieldDefinition> All { get; } = Build();

    /// <summary>
    /// The fields only an owner could answer (spec §3.2, §8.5), in the order the dashboard offers them.
    /// </summary>
    /// <remarks>
    /// Derived from the definition rather than listed again, so the form an owner sees and the gate
    /// <see cref="OwnerEnrichment"/> applies are the same set by construction. A field marked below
    /// appears on the dashboard and becomes writable in one edit; there is nowhere for the two to
    /// disagree.
    /// </remarks>
    public static IReadOnlyList<FieldDefinition> OwnerEnrichable { get; } =
        [.. All.Where(definition => definition.OwnerWritable is OwnerWritable.Enrichment)];

    /// <summary>
    /// The fields an owner may answer over their game's own MSSP report (spec §8.5).
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="OwnerEnrichable"/> for the dashboard's sake and for no other:
    /// these have a value to show beside the box and those do not. Both are writable, and
    /// <see cref="OwnerEnrichment"/> asks the definition rather than either list.
    /// </remarks>
    public static IReadOnlyList<FieldDefinition> OwnerOverridable { get; } =
        [.. All.Where(definition => definition.OwnerWritable is OwnerWritable.Override)];

    private static readonly Dictionary<string, FieldDefinition> Index =
        All.ToDictionary(definition => definition.Name, StringComparer.Ordinal);

    /// <summary>
    /// The definition for a field, or null for one nobody declared. A game may emit any unofficial
    /// MSSP variable it likes, and the registry is not a gate on ingestion — an undeclared field is
    /// stored like any other; we simply decline to judge its age.
    /// </summary>
    public FieldDefinition? Find(string field) =>
        Index.TryGetValue(field, out var known) ? known : null;

    /// <summary>
    /// Whether a value has aged past its own window. An unknown field is never stale: we cannot judge
    /// a field we do not define, and guessing would put an invented fact on a public page.
    /// </summary>
    public bool IsStale(string field, DateTimeOffset lastConfirmedAt, DateTimeOffset now) =>
        Find(field) is { } definition && now - lastConfirmedAt > definition.ExpectedRefresh;

    private static IReadOnlyList<FieldDefinition> Build()
    {
        var fields = new List<FieldDefinition>();

        void Add(string name, TimeSpan window, OwnerWritable writable = OwnerWritable.No) =>
            fields.Add(new FieldDefinition(name, window, writable));

        // ── What a verified owner may answer, and the line it does not cross ──────────────────────
        //
        // §8.5's rule is that an owner may never edit a MEASUREMENT, and the ceiling now sits where
        // that argument puts it rather than four fields short of it. An MSSP report is not a
        // measurement: §5.1 calls `mssp` a game filling in a structured self-description it
        // maintains, and `owner` a person typing. Same kind of fact, same person, different road —
        // so the same person may take the shorter road, and both rows go on existing side by side.
        //
        // The half marked Override is what a person types into mush.cnf. The half left alone is what
        // the codebase fills in about the connection: a hand-typed HOSTNAME and a measured one would
        // mean genuinely different things under one name, and nothing good comes of that.

        // The required MSSP trio. PLAYERS is declared here because it is the staleness anchor §5.6
        // argues from — but it is NEVER stored as a GameField: the count lives in §5.2's presence
        // series, where `who` outranks `mssp`. FieldReconciler skips it, and skips UPTIME with it.
        Add("NAME", Automatic, OwnerWritable.Override);
        Add("PLAYERS", Volatile);
        Add("UPTIME", Volatile);

        // The generic set — mostly auto-filled by the codebase.
        Add("CRAWL DELAY", Automatic);
        Add("HOSTNAME", Automatic);
        Add("PORT", Automatic);
        Add("CODEBASE", Automatic);
        Add("IP", Automatic);
        Add("IPV6", Automatic);
        Add("CHARSET", Automatic);
        Add("CONTACT", Contactable, OwnerWritable.Override);
        Add("WEBSITE", Contactable, OwnerWritable.Override);
        Add("DISCORD", Contactable, OwnerWritable.Override);
        Add("ICON", HandTyped, OwnerWritable.Override);
        Add("CREATED", HandTyped, OwnerWritable.Override);
        Add("LANGUAGE", HandTyped, OwnerWritable.Override);
        Add("LOCATION", HandTyped, OwnerWritable.Override);
        Add("MINIMUM AGE", HandTyped, OwnerWritable.Override);

        // The categorisation set — hand-typed once, at install, and then left alone. This is the set
        // §3.1 warns about: a crawler presenting these with the same confidence as the handshake is
        // publishing a 2017 answer as a live one. It is also the set an owner is most likely to want
        // to correct, and FAMILY is the one exception: the codebase derives it, nobody types it.
        Add("FAMILY", Automatic);
        Add("GENRE", HandTyped, OwnerWritable.Override);
        Add("GAMEPLAY", HandTyped, OwnerWritable.Override);
        Add("GAMESYSTEM", HandTyped, OwnerWritable.Override);
        Add("SUBGENRE", HandTyped, OwnerWritable.Override);
        Add("STATUS", HandTyped, OwnerWritable.Override);
        Add("INTERMUD", HandTyped, OwnerWritable.Override);
        Add("DESCRIPTION", HandTyped, OwnerWritable.Override);

        // Our own non-MSSP fields are lower-case and namespaced, so an MSSP variable and one of ours
        // can never collide however unofficial the variable.
        Add(InternalFields.ConnectScreen, Automatic);
        Add(InternalFields.ConnectScreenSuppressed, Automatic);

        // Capabilities, both sides. Measured is re-observed on every probe; declared is hand-typed.
        //
        // Neither side is writable, and the declared side is the interesting refusal: it is
        // hand-typed, so it would otherwise qualify. The matrix's whole job is to show what a game
        // claims beside what its handshake offered — "declared GMCP, not offered in 214 handshakes"
        // — and an owner editing the claimed column is editing one half of a comparison about
        // themselves.
        foreach (var capability in CapabilityFields.Names)
        {
            Add(CapabilityFields.Measured(capability), Measured);
            Add(CapabilityFields.Declared(capability), Automatic);
        }

        // Owner enrichment — spec §3.2 names exactly these as genuinely absent from MSSP. SUBGENRE
        // cannot say "Marvel" or "Exalted", and nothing in the taxonomy expresses how a character
        // application works, how RP is enforced, or what consent tooling exists.
        Add("FANDOM", HandTyped, OwnerWritable.Enrichment);
        Add("APPLICATION PROCESS", HandTyped, OwnerWritable.Enrichment);
        Add("RP ENFORCEMENT", HandTyped, OwnerWritable.Enrichment);
        Add("CONSENT TOOLS", HandTyped, OwnerWritable.Enrichment);

        return fields;
    }
}
