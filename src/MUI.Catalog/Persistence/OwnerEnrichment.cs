namespace MUI.Catalog.Persistence;

/// <summary>One field a verified owner asked to set, as it arrived from a form.</summary>
/// <remarks>
/// The field name travels with the value rather than being implied by a position, because the gate
/// below is on the <em>name</em>: a form that posted four values in a fixed order would have to trust
/// its own markup to say which field each one was, and the markup is the part an attacker rewrites.
/// </remarks>
public sealed record OwnerEdit(string Field, string Value);

/// <summary>What became of an owner's write.</summary>
/// <remarks>
/// Every refusal is a named member rather than a null or a false, because §8.5 requires a refused
/// write to be refused <em>out loud</em>: a silent no-op teaches an owner that the site is broken,
/// and a caller handed a bare <c>false</c> has nothing to put on the page.
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
    /// Refused at the door rather than at the point of rendering, for the reason every other refusal
    /// here is: a value stored and then silently declined by the page is a box an owner filled in
    /// correctly as far as they can tell. Naming the field and saying what the shape is turns
    /// "nothing happened" into a typo they can fix.
    /// </remarks>
    NotAnAddress,
}

/// <summary>
/// The verdict, the field that earned it, and what the write actually did.
/// </summary>
/// <remarks>
/// <see cref="Field"/> is populated only by a refusal, and it is populated <em>because</em> it is a
/// refusal: "that was not accepted" is not an answer an operator can act on, and naming the field is
/// the difference between a rule and an obstacle.
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
/// <para>
/// <b><c>NAME</c> is the one writable field that is not only a row.</b> Every other value an owner
/// sets is read back through the precedence ladder wherever it is rendered; the listed name is a
/// denormalised column, and the URL minted from it is a promise to everyone holding the old one. So a
/// write to <c>NAME</c> has a second half, and this is the seam it goes through.
/// </para>
/// <para>
/// An interface, and optional on <see cref="OwnerEnrichment"/>, for two reasons that both matter.
/// The writer that keeps §5.7's promise lives in <c>MUI.Crawler</c> and <c>MUI.Catalog</c> may not
/// know it exists — that arrow points one way and this is the shape that respects it. And a
/// deployment with no minter behind it should store the name and do less, rather than refuse the
/// write: the row is the fact, and the URL is a convenience built on it.
/// </para>
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
/// <para>
/// <b>An owner may never edit a measurement.</b> They can add <c>FANDOM</c> and they can answer
/// <c>GENRE</c> over their game's own MSSP; they cannot touch a player count, a capability or a
/// reachability history. The writable set <em>is</em> the field registry's
/// <see cref="FieldDefinition.OwnerWritable"/> property — not a list repeated here, which would be a
/// second spelling of the same rule and would drift the first time a field was added — and a write
/// to any other field is refused out loud rather than dropped. A successful one would make the whole
/// site a self-report with extra steps.
/// </para>
/// <para>
/// <b>An MSSP report is not a measurement, which is why the overridable half exists.</b> §5.1 draws
/// the line at who read the value: <c>handshake</c>, <c>who</c> and <c>banner</c> are things we
/// observed on a socket, <c>mssp</c> is a game filling in a self-description it maintains, and
/// <c>owner</c> is a person typing. The last two are the same kind of fact from the same person by a
/// different road, so the same person may take the shorter one.
/// </para>
/// <para>
/// <b>Every value written here is declared, and it never displaces anything.</b> It is stored under
/// <see cref="FieldSource.Owner"/>, which is a row of its own: <c>GameField</c> is keyed
/// <c>(game, field, source)</c> precisely so both sides can coexist, so an owner's <c>GENRE</c> sits
/// beside MSSP's rather than over it and the page shows both with their ages. It carries its source
/// onto every surface that renders it, and <see cref="FieldSources.IsMeasured"/> is untouched by any
/// of this — an override is <em>declared</em> on every chip, in the API and in the plain-text
/// surface. §5.1's ladder putting <c>owner</c> above <c>mssp</c> decides which one is shown first
/// and silences neither.
/// </para>
/// <para>
/// <b>Nothing is deleted here either.</b> Clearing a field writes an empty value, and restoring a
/// connect screen writes <c>false</c> — both are new values of a row that goes on existing, and both
/// reach the change feed the way a probe's change would. The one exception is a clear of a field that
/// was never set, which writes nothing at all rather than minting a blank row to say so.
/// </para>
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
    /// Spelled as MSSP spells it, because that is the row it shares a name with and the two are
    /// matched case-insensitively everywhere they meet.
    /// </remarks>
    private const string NameField = "NAME";

    /// <summary>
    /// How much of a hand-typed enrichment value we will store.
    /// </summary>
    /// <remarks>
    /// A bound rather than a truncation: silently keeping the first five hundred characters of what
    /// somebody wrote is the quiet lossiness this schema refuses everywhere else. These are one-line
    /// answers — a fandom, how an application works — and a value past this length is a paragraph
    /// that wanted the game's own description field.
    /// </remarks>
    public const int MaxValueLength = 500;

    /// <summary>
    /// Whether <paramref name="userId"/> may write to <paramref name="gameId"/> at all.
    /// </summary>
    /// <remarks>
    /// A verified claim, not a pending one. A pending claim is an account that has <em>asked</em>,
    /// and asking is not proving — the token still has to be published where a probe can read it
    /// (§8.1). A revoked claim is not one either.
    /// </remarks>
    public async Task<bool> OwnsAsync(Guid gameId, Guid userId, CancellationToken cancellationToken = default) =>
        (await claims.ForUserAsync(userId, cancellationToken))
            .Any(claim => claim.GameId == gameId && claim.IsVerified);

    /// <summary>
    /// What this game's owners have already declared, so a form can be filled in with it.
    /// </summary>
    /// <remarks>
    /// Owner rows only, and asked for as owner rows rather than filtered after the fact: reading
    /// every row of a game to keep at most four of them drags the connect screen across the wire
    /// once per claimed game per page load, to throw it away.
    ///
    /// The dashboard edits what owners wrote and must not offer a measurement back as though it
    /// were an editable draft of one.
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
    /// <b>Shown, never offered as a draft.</b> The boxes are filled from
    /// <see cref="DeclaredAsync"/> — what this owner wrote — and these appear beside them as text.
    /// Pre-filling an editable field with a game's own report would invite somebody to retype it into
    /// an override that says exactly the same thing, which is a second row, a second age and a second
    /// thing to keep current, in exchange for nothing.
    ///
    /// MSSP rows only. The handshake's answers are measurements and have no box to sit beside.
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
    /// All or nothing on purpose. A submission carrying one enrichable field and one measurement is
    /// not a partial success: applying the good half and reporting the bad one leaves an owner with a
    /// page that both worked and failed, and the interesting case — somebody posting a field the form
    /// never offered — is exactly the one that must not get half of what it asked for.
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
                // Refused out loud, and the rest of the submission with it. This is the line §8.5
                // draws: a player count, a capability and a reachability history are measurements,
                // and no form on this site may reach one.
                return new EnrichmentOutcome(
                    EnrichmentVerdict.NotEnrichable, edit.Field, FieldReconciliation.Nothing);
            }

            var value = OneLine(edit.Value);

            if (value.Length > MaxValueLength)
            {
                return new EnrichmentOutcome(
                    EnrichmentVerdict.TooLong, edit.Field, FieldReconciliation.Nothing);
            }

            // A field that holds an address gets one or nothing. The clear is exempt on purpose —
            // an empty box is a withdrawal, and "" is not a malformed URL, it is the absence of one.
            //
            // This is a gate on the WRITE and not only on the render, because the alternative is a
            // stored value that the page quietly declines to link: the owner sees their address in
            // the list below the fold, no icon beside the title, and nothing anywhere saying why.
            if (definition.Shape is not FieldShape.Text
                && value.Length > 0
                && !ExternalUrl.IsLinkable(value, definition.Shape))
            {
                return new EnrichmentOutcome(
                    EnrichmentVerdict.NotAnAddress, edit.Field, FieldReconciliation.Nothing);
            }

            // An empty box over an already-empty field is not a withdrawal of anything, so it is
            // skipped: minting a blank row for a field nobody ever set, or re-confirming one that is
            // already withdrawn, both record an event that did not happen. The second also walked
            // the row's age forward on every save, so a field withdrawn last year read as touched
            // this morning.
            if (value.Length == 0
                && (!declared.TryGetValue(edit.Field, out var withdrawn) || withdrawn.Value.Length == 0))
            {
                continue;
            }

            observations.Add(new FieldObservation(edit.Field, FieldSource.Owner, value));
        }

        var applied = await reconciler.ApplyAsync(
            gameId, observations, time.GetUtcNow(), cancellationToken);

        // The second half of a NAME write. It runs after the row is stored rather than before, so a
        // rename that cannot be minted — two games settling on one slug in the same moment — leaves
        // the name stored and the URL to be re-minted later, never the reverse.
        //
        // A withdrawal renames nothing. The empty row stops outranking the report, and the crawler's
        // own grace decides what the game is called from the next cycle: an owner giving up a name is
        // not an owner asking for a different one.
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
    /// <b>No questions asked.</b> There is no reason field, no review and no delay: the screen is
    /// displayed on the grounds that the server sends it unauthenticated to every anonymous
    /// connection, and the moment its owner would rather we did not, that ground is gone. It is a
    /// field like any other, so the decision carries an age and reaches the change feed — and it is
    /// <em>ours</em> rather than something the game said, which is why it is an
    /// <see cref="InternalFields"/> name and never appears in the panel of what a game says about
    /// itself.
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

        // Resuming writes "false" rather than removing the row. Nothing is ever deleted, and a
        // reader of the change feed should be able to see the decision go both ways.
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
    /// These are one-line answers rendered into a definition list, a JSON string and an eighty-column
    /// plain-text surface, and a pasted newline reads as three different things across those three.
    /// Nothing else is done to the text: it is escaped where it is rendered, and a value quietly
    /// rewritten beyond this would be a different fact from the one its owner typed.
    /// </remarks>
    private static string OneLine(string? value) =>
        value is null ? string.Empty : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
