namespace MUI.Catalog.Persistence;

/// <summary>One field a verified owner asked to set, as it arrived from a form.</summary>
/// <remarks>
/// The field name travels with the value rather than being implied by position: the gate below is on
/// the <em>name</em>, and position is the part an attacker-controlled form would rewrite.
/// </remarks>
public sealed record OwnerEdit(string Field, string Value);

/// <summary>What became of an owner's write.</summary>
/// <remarks>
/// Every refusal is a named member, not a null or a false — §8.5 requires a refused write to be
/// refused <em>out loud</em>; a silent no-op teaches an owner the site is broken.
/// </remarks>
public enum EnrichmentVerdict
{
    Applied,

    /// <summary>Nobody with a verified claim on this game asked. Nothing was written.</summary>
    NotAnOwner,

    /// <summary>
    /// A field outside the registry's owner-enrichable set — which is to say, a measurement.
    /// </summary>
    NotEnrichable,

    TooLong,

    /// <summary>
    /// A field that holds an address, and a value that is not one.
    /// </summary>
    /// <remarks>
    /// Refused at the door, not at render: a value stored and then silently declined by the page
    /// looks like success to the owner who typed it.
    /// </remarks>
    NotAnAddress,
}

/// <summary>
/// The verdict, the field that earned it, and what the write actually did.
/// </summary>
/// <remarks>
/// <see cref="Field"/> is populated only on a refusal — naming the field turns "nothing happened"
/// into a fixable typo.
/// </remarks>
public sealed record EnrichmentOutcome(
    EnrichmentVerdict Verdict,
    string? Field,
    FieldReconciliation Applied)
{
    public static readonly EnrichmentOutcome NotAnOwner =
        new(EnrichmentVerdict.NotAnOwner, null, FieldReconciliation.Nothing);

    public bool IsApplied => Verdict is EnrichmentVerdict.Applied;
}

/// <summary>
/// Applies a name a verified owner chose to the game's listing and its URL (spec §5.7, §8.5).
/// </summary>
/// <remarks>
/// <c>NAME</c> is the one writable field that isn't only a row: the listed name is a denormalised
/// column, and the URL minted from it is a promise to everyone holding the old one, so a write to it
/// needs this second step. Optional because <c>MUI.Catalog</c> must not know the minter in
/// <c>MUI.Crawler</c> exists, and a deployment with no minter should still store the name.
/// </remarks>
public interface IOwnerRenames
{
    /// <summary>
    /// Renames the game and re-mints its URL, retiring the old slug into the redirect table.
    /// </summary>
    Task RenameAsync(Guid gameId, string name, CancellationToken cancellationToken = default);
}

/// <summary>
/// What a verified owner may write, and the line it may not cross (spec §8.5, §11).
/// </summary>
/// <remarks>
/// An owner may never edit a measurement: the writable set <em>is</em> the field registry's
/// <see cref="FieldDefinition.OwnerWritable"/> (not repeated here, to avoid drift), and a write to
/// anything else is refused out loud rather than dropped.
/// An MSSP report isn't a measurement — §5.1 draws the line at who read the value, and a game filling
/// in its own MSSP is the same kind of fact as its owner typing, just a shorter road.
/// Every value written here is stored under <see cref="FieldSource.Owner"/>, a row of its own beside
/// MSSP's rather than over it (<c>GameField</c> is keyed <c>(game, field, source)</c> for exactly
/// this), and is declared on every surface that renders it — §5.1's ladder decides which is shown
/// first and silences neither.
/// Nothing is deleted here either: clearing a field writes an empty value, and restoring a connect
/// screen writes <c>false</c>. The one exception is clearing a field that was never set, which writes
/// nothing at all.
/// </remarks>
public sealed class OwnerEnrichment(
    IClaimStore claims,
    IGameFieldStore fields,
    IFieldReconciler reconciler,
    IFieldRegistry registry,
    TimeProvider time,
    IOwnerRenames? renames = null)
{
    /// <summary>The field whose write has a second half. See <see cref="IOwnerRenames"/>.</summary>
    /// <remarks>
    /// Spelled as MSSP spells it — the two are matched case-insensitively everywhere they meet.
    /// </remarks>
    private const string NameField = "NAME";

    /// <summary>
    /// How much of a hand-typed enrichment value we will store.
    /// </summary>
    /// <remarks>
    /// A bound, not a truncation — silently keeping the first 500 characters would be the quiet
    /// lossiness this schema refuses everywhere else.
    /// </remarks>
    public const int MaxValueLength = 500;

    /// <summary>
    /// Whether <paramref name="userId"/> may write to <paramref name="gameId"/> at all.
    /// </summary>
    /// <remarks>
    /// A verified claim, not a pending one — asking is not proving (§8.1). A revoked claim isn't one
    /// either.
    /// </remarks>
    public async Task<bool> OwnsAsync(Guid gameId, Guid userId, CancellationToken cancellationToken = default) =>
        (await claims.ForUserAsync(userId, cancellationToken))
            .Any(claim => claim.GameId == gameId && claim.IsVerified);

    /// <summary>
    /// What this game's owners have already declared, so a form can be filled in with it.
    /// </summary>
    /// <remarks>
    /// Owner rows only, asked for as owner rows rather than filtered after the fact, to avoid
    /// dragging every field of a claimed game across the wire per page load. Must never offer a
    /// measurement back as an editable draft.
    /// </remarks>
    public async Task<IReadOnlyDictionary<string, GameField>> DeclaredAsync(
        Guid gameId,
        CancellationToken cancellationToken = default) =>
        (await fields.ForGameAsync(gameId, FieldSource.Owner, cancellationToken))
            .ToDictionary(field => field.Field, StringComparer.Ordinal);

    /// <summary>
    /// What this game reports about itself, so the form can show it beside the box that overrides it.
    /// </summary>
    /// <remarks>
    /// Shown, never offered as a draft: pre-filling an editable box with the game's own report would
    /// invite retyping it into an override that says exactly the same thing. MSSP rows only — the
    /// handshake's answers are measurements with no box to sit beside.
    /// </remarks>
    public async Task<IReadOnlyDictionary<string, GameField>> ReportedAsync(
        Guid gameId,
        CancellationToken cancellationToken = default) =>
        (await fields.ForGameAsync(gameId, FieldSource.Mssp, cancellationToken))
            .ToDictionary(field => field.Field, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Applies a set of edits, or refuses the whole set and says which field did it.
    /// </summary>
    /// <remarks>
    /// All or nothing: applying the good half of a submission and reporting the bad one leaves an
    /// owner with a page that both worked and failed.
    /// </remarks>
    public async Task<EnrichmentOutcome> ApplyAsync(
        Guid gameId,
        Guid userId,
        IReadOnlyList<OwnerEdit> edits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edits);

        if (!await OwnsAsync(gameId, userId, cancellationToken))
        {
            return EnrichmentOutcome.NotAnOwner;
        }

        var declared = await DeclaredAsync(gameId, cancellationToken);
        var observations = new List<FieldObservation>(edits.Count);

        foreach (var edit in edits)
        {
            if (registry.Find(edit.Field) is not { OwnerWritable: not OwnerWritable.No } definition)
            {
                // Refused out loud: §8.5 draws this line — a player count, a capability and a
                // reachability history are measurements, and no form here may reach one.
                return new EnrichmentOutcome(
                    EnrichmentVerdict.NotEnrichable, edit.Field, FieldReconciliation.Nothing);
            }

            var value = OneLine(edit.Value);

            if (value.Length > MaxValueLength)
            {
                return new EnrichmentOutcome(
                    EnrichmentVerdict.TooLong, edit.Field, FieldReconciliation.Nothing);
            }

            // A field that holds an address gets one or nothing; a clear is exempt since "" is
            // absence, not a malformed URL. Gated on the write, not only the render, so a stored
            // value is never silently un-linked with nothing saying why.
            if (definition.Shape is not FieldShape.Text
                && value.Length > 0
                && !ExternalUrl.IsLinkable(value, definition.Shape))
            {
                return new EnrichmentOutcome(
                    EnrichmentVerdict.NotAnAddress, edit.Field, FieldReconciliation.Nothing);
            }

            // An empty box over an already-empty field isn't a withdrawal — skipped, so we don't mint
            // a blank row or walk an already-withdrawn field's age forward for nothing that happened.
            if (value.Length == 0
                && (!declared.TryGetValue(edit.Field, out var withdrawn) || withdrawn.Value.Length == 0))
            {
                continue;
            }

            observations.Add(new FieldObservation(edit.Field, FieldSource.Owner, value));
        }

        var applied = await reconciler.ApplyAsync(
            gameId, observations, time.GetUtcNow(), cancellationToken);

        // The second half of a NAME write. Runs after the row is stored, so a rename that can't be
        // minted (a slug collision) leaves the name stored for the URL to be re-minted later, never
        // the reverse. A withdrawal renames nothing — the crawler's own grace decides the name from
        // the next cycle.
        if (observations.FirstOrDefault(o => IsName(o.Field)) is { Value.Length: > 0 } named)
        {
            await (renames?.RenameAsync(gameId, named.Value, cancellationToken) ?? Task.CompletedTask);
        }

        return new EnrichmentOutcome(EnrichmentVerdict.Applied, null, applied);
    }

    private static bool IsName(string field) =>
        string.Equals(field, NameField, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Stops — or resumes — republishing this game's connect screen (spec §11).
    /// </summary>
    /// <remarks>
    /// No questions asked: the screen is shown only because the server sends it unauthenticated to
    /// every anonymous connection, so the moment its owner objects, that ground is gone. It's an
    /// <see cref="InternalFields"/> name — ours, not something the game said — so it never appears in
    /// the panel of what a game says about itself.
    /// </remarks>
    public async Task<EnrichmentOutcome> SetConnectScreenSuppressedAsync(
        Guid gameId,
        Guid userId,
        bool suppressed,
        CancellationToken cancellationToken = default)
    {
        if (!await OwnsAsync(gameId, userId, cancellationToken))
        {
            return EnrichmentOutcome.NotAnOwner;
        }

        // Resuming writes "false" rather than removing the row — nothing is ever deleted.
        FieldObservation[] observation =
        [
            new(InternalFields.ConnectScreenSuppressed, FieldSource.Owner, suppressed ? "true" : "false"),
        ];

        return new EnrichmentOutcome(
            EnrichmentVerdict.Applied,
            null,
            await reconciler.ApplyAsync(gameId, observation, time.GetUtcNow(), cancellationToken));
    }

    /// <summary>
    /// Trimmed, with every run of whitespace — newlines included — collapsed to one space.
    /// </summary>
    /// <remarks>
    /// Rendered into a definition list, a JSON string, and an 80-column plain-text surface, where a
    /// pasted newline would read as three different things. Nothing else is normalized.
    /// </remarks>
    private static string OneLine(string? value) =>
        value is null ? string.Empty : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
