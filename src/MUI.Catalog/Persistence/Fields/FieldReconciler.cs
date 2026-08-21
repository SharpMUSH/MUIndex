using System.Text.RegularExpressions;

namespace MUI.Catalog.Persistence;

/// <summary>
/// Turns observations into stored fields (spec §5.1). Every observation does exactly one of two
/// things: <b>confirm</b> — bump <c>last_confirmed_at</c> and write nothing else — or <b>change</b> —
/// rewrite the row and append one <see cref="FieldChange"/>.
/// </summary>
/// <remarks>
/// The distinction is the whole economics of §5.1: a game whose <c>GENRE</c> never moves costs one
/// row per source for ever rather than one per probe, and the change feed stays a table of events
/// that actually happened. A first sighting is an addition and writes no change row — "GENRE
/// changed from nothing to Fantasy" would be an event about us, not the game.
/// </remarks>
public sealed partial class FieldReconciler(IGameFieldStore store) : IFieldReconciler
{
    /// <summary>
    /// Fields that move on every probe and are therefore never stored as descriptive fields.
    /// </summary>
    /// <remarks>
    /// Both change between probes; reconciling them would write a <see cref="FieldChange"/> row per
    /// probe per game and drown the change feed in noise. <c>PLAYERS</c> is presence (§5.2), written
    /// by <see cref="PresenceWriter"/>; <c>UPTIME</c> is a counter, not a description of the game.
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

        // One read for the whole probe. Which source wins is FieldPrecedence's job, on read — baking
        // a winner in here would go stale against the rows it summarises.
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

            if (LayoutEquivalent(existing.Value, observation.Value))
            {
                // Confirm: value unchanged, first_seen_at untouched, nothing reaches the change feed.
                await store.UpsertAsync(existing with { LastConfirmedAt = at }, cancellationToken);
                confirmed++;
                continue;
            }

            // Change: first_seen_at still means "when this (game, field, source) was first seen",
            // not "when this value was" — the change feed carries the value's history separately.
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

    /// <summary>
    /// Whether two values are the same fact reflowed, not two different facts.
    /// </summary>
    /// <remarks>
    /// A mid-sentence line wrap toggling in and out of a value between probes is ordinal-unequal but
    /// not a real change — runs of whitespace are layout, not content, so collapsing each run to one
    /// space before comparing tells "reflowed" apart from "reworded". <c>GameField.Value</c> still
    /// holds the value exactly as sent, since a value judged unchanged is never written (confirm
    /// branch above).
    /// </remarks>
    private static bool LayoutEquivalent(string a, string b) =>
        string.Equals(CollapseWhitespace(a), CollapseWhitespace(b), StringComparison.Ordinal);

    private static string CollapseWhitespace(string value) => WhitespaceRun().Replace(value, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
