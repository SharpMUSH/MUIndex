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
/// What a verified owner may write, and the line it may not cross (spec §8.5, §11).
/// </summary>
/// <remarks>
/// <para>
/// <b>An owner may never edit a measurement.</b> They can add <c>FANDOM</c>; they cannot touch a
/// player count, a capability or a reachability history. The writable set <em>is</em> the field
/// registry's <see cref="FieldDefinition.OwnerEnrichable"/> flag — not a list repeated here, which
/// would be a second spelling of the same rule and would drift the first time a field was added — and
/// a write to any other field is refused out loud rather than dropped. A successful one would make
/// the whole site a self-report with extra steps.
/// </para>
/// <para>
/// <b>Every value written here is declared, and it never displaces a measurement.</b> It is stored
/// under <see cref="FieldSource.Owner"/>, which is a row of its own: <c>GameField</c> is keyed
/// <c>(game, field, source)</c> precisely so both sides can coexist, so an owner's <c>GENRE</c> could
/// not overwrite MSSP's even if one were writable. It ages like every other fact, it carries its
/// source onto every surface that renders it, and the fields it may reach are ones no probe produces
/// — which is what makes §5.1's ladder put <c>owner</c> above <c>mssp</c> for "enrichment-only
/// fields" without that ranking ever silencing anything.
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
    TimeProvider time)
{
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
    /// Owner rows only. The dashboard edits what owners wrote and must not offer a measurement back
    /// as though it were an editable draft of one.
    /// </remarks>
    public async Task<IReadOnlyDictionary<string, GameField>> DeclaredAsync(
        Guid gameId,
        CancellationToken cancellationToken = default) =>
        (await fields.ForGameAsync(gameId, cancellationToken))
            .Where(field => field.Source is FieldSource.Owner)
            .ToDictionary(field => field.Field, StringComparer.Ordinal);

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
            if (registry.Find(edit.Field) is not { OwnerEnrichable: true })
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

            // Clearing a field that was never set is not a clearing. An empty form box would
            // otherwise mint a blank row for every field its owner left alone.
            if (value.Length == 0 && !declared.ContainsKey(edit.Field))
            {
                continue;
            }

            observations.Add(new FieldObservation(edit.Field, FieldSource.Owner, value));
        }

        return new EnrichmentOutcome(
            EnrichmentVerdict.Applied,
            null,
            await reconciler.ApplyAsync(gameId, observations, time.GetUtcNow(), cancellationToken));
    }

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
