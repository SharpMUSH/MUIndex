namespace MUI.Catalog.Persistence;

/// <summary>
/// Turns observations into stored fields (spec §5.1). Every observation does exactly one of two
/// things: <b>confirm</b> — bump <c>last_confirmed_at</c> and write nothing else — or <b>change</b> —
/// rewrite the row and append one <see cref="FieldChange"/>.
/// </summary>
/// <remarks>
/// <para>
/// The distinction is the whole economics of §5.1: a game whose <c>GENRE</c> never moves costs one
/// row per source for ever rather than one per probe, and the change feed is a table of events that
/// actually happened rather than a log of the crawler having run.
/// </para>
/// <para>
/// A first sighting is an <em>addition</em> and writes no change row. "Newly discovered" is §9's own
/// feed, and a change entry reading "GENRE changed from nothing to Fantasy" would be an event about
/// us rather than about the game.
/// </para>
/// </remarks>
public sealed class FieldReconciler(IGameFieldStore store) : IFieldReconciler
{
    /// <summary>
    /// Fields that move on every probe and are therefore never stored as descriptive fields.
    /// </summary>
    /// <remarks>
    /// MSSP requires <c>PLAYERS</c> and <c>UPTIME</c>, and both change between one probe and the
    /// next. Reconciling them would write a <see cref="FieldChange"/> row per probe per game — the
    /// exact cost §5.1 exists to avoid — and would fill the change feed with noise that hid every
    /// real event. <c>PLAYERS</c> is presence (§5.2) and is written by <see cref="PresenceWriter"/>;
    /// <c>UPTIME</c> is a counter, not a description of the game.
    /// </remarks>
    public static IReadOnlySet<string> VolatileFields { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "PLAYERS", "UPTIME" };

    public async Task<FieldReconciliation> ApplyAsync(
        Guid gameId,
        IReadOnlyList<FieldObservation> observed,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observed);

        if (observed.Count == 0)
        {
            return FieldReconciliation.Nothing;
        }

        // One read for the whole probe. The store is keyed (game, field, source), so nothing here
        // has to decide which source wins — that is FieldPrecedence's job, on read, and deriving it
        // now would bake a winner into storage that could go stale against the rows it summarises.
        var stored = (await store.ForGameAsync(gameId, cancellationToken))
            .ToDictionary(row => (row.Field, row.Source));

        var confirmed = 0;
        var changed = 0;
        var added = 0;

        foreach (var observation in observed)
        {
            if (VolatileFields.Contains(observation.Field))
            {
                continue;
            }

            if (!stored.TryGetValue((observation.Field, observation.Source), out var existing))
            {
                await store.UpsertAsync(
                    new GameField(
                        gameId,
                        observation.Field,
                        observation.Source,
                        observation.Value,
                        FirstSeenAt: at,
                        LastConfirmedAt: at),
                    cancellationToken);
                added++;
                continue;
            }

            if (string.Equals(existing.Value, observation.Value, StringComparison.Ordinal))
            {
                // Confirm. The value is unchanged, so first_seen_at is untouched and nothing reaches
                // the change feed. This is the common case by a wide margin and it costs one UPDATE.
                await store.UpsertAsync(existing with { LastConfirmedAt = at }, cancellationToken);
                confirmed++;
                continue;
            }

            // Change. first_seen_at still means "when this (game, field, source) was first seen",
            // not "when this value was", because the change feed already carries the value's history
            // and the chip wants the age of the row.
            await store.UpsertAsync(
                existing with { Value = observation.Value, LastConfirmedAt = at },
                cancellationToken);
            await store.RecordChangeAsync(
                new FieldChange(
                    gameId,
                    observation.Field,
                    observation.Source,
                    OldValue: existing.Value,
                    NewValue: observation.Value,
                    At: at),
                cancellationToken);
            changed++;
        }

        return new FieldReconciliation(confirmed, changed, added);
    }
}
