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
        "NAWS", "NEW-ENVIRON", "PUEBLO", "SSL", "TLS", "TTYPE", "UTF-8", "VT100",
        "XTERM 256 COLORS", "ZMP",
    ];

    public static string Measured(string capability) => Prefix + Normalise(capability) + MeasuredSuffix;

    public static string Declared(string capability) => Prefix + Normalise(capability) + DeclaredSuffix;

    /// <summary>
    /// The capability a field name refers to, or null if it names no capability. The inverse of
    /// <see cref="Measured"/> and <see cref="Declared"/>, so a row read back can be put in the right
    /// column of the matrix without a second convention for parsing these names.
    /// </summary>
    public static string? CapabilityOf(string field)
    {
        if (!field.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var suffix = field.EndsWith(MeasuredSuffix, StringComparison.Ordinal) ? MeasuredSuffix
            : field.EndsWith(DeclaredSuffix, StringComparison.Ordinal) ? DeclaredSuffix
            : null;

        if (suffix is null)
        {
            return null;
        }

        var slug = field[Prefix.Length..^suffix.Length];

        return Names.FirstOrDefault(name => Normalise(name) == slug);
    }

    public static bool IsMeasured(string field) =>
        field.StartsWith(Prefix, StringComparison.Ordinal)
        && field.EndsWith(MeasuredSuffix, StringComparison.Ordinal);

    private static string Normalise(string capability) =>
        capability.Trim().ToLowerInvariant().Replace(' ', '-');
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

        void Add(string name, TimeSpan window, bool ownerEnrichable = false) =>
            fields.Add(new FieldDefinition(name, window, ownerEnrichable));

        // The required MSSP trio. PLAYERS is declared here because it is the staleness anchor §5.6
        // argues from — but it is NEVER stored as a GameField: the count lives in §5.2's presence
        // series, where `who` outranks `mssp`. FieldReconciler skips it, and skips UPTIME with it.
        Add("NAME", Automatic);
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
        Add("CONTACT", Contactable);
        Add("WEBSITE", Contactable);
        Add("DISCORD", Contactable);
        Add("ICON", HandTyped);
        Add("CREATED", HandTyped);
        Add("LANGUAGE", HandTyped);
        Add("LOCATION", HandTyped);
        Add("MINIMUM AGE", HandTyped);

        // The categorisation set — hand-typed once, at install, and then left alone. This is the set
        // §3.1 warns about: a crawler presenting these with the same confidence as the handshake is
        // publishing a 2017 answer as a live one.
        Add("FAMILY", Automatic);
        Add("GENRE", HandTyped);
        Add("GAMEPLAY", HandTyped);
        Add("GAMESYSTEM", HandTyped);
        Add("SUBGENRE", HandTyped);
        Add("STATUS", HandTyped);
        Add("INTERMUD", HandTyped);
        Add("DESCRIPTION", HandTyped);

        // Our own non-MSSP fields are lower-case and namespaced, so an MSSP variable and one of ours
        // can never collide however unofficial the variable.
        Add("connect_screen", Automatic);
        Add("connect_screen_suppressed", Automatic);

        // Capabilities, both sides. Measured is re-observed on every probe; declared is hand-typed.
        foreach (var capability in CapabilityFields.Names)
        {
            Add(CapabilityFields.Measured(capability), Measured);
            Add(CapabilityFields.Declared(capability), Automatic);
        }

        // Owner enrichment — spec §3.2 names exactly these as genuinely absent from MSSP. SUBGENRE
        // cannot say "Marvel" or "Exalted", and nothing in the taxonomy expresses how a character
        // application works, how RP is enforced, or what consent tooling exists.
        Add("FANDOM", HandTyped, ownerEnrichable: true);
        Add("APPLICATION PROCESS", HandTyped, ownerEnrichable: true);
        Add("RP ENFORCEMENT", HandTyped, ownerEnrichable: true);
        Add("CONSENT TOOLS", HandTyped, ownerEnrichable: true);

        return fields;
    }
}
