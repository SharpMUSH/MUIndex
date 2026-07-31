namespace MUI.Catalog.Persistence;

/// <summary>
/// Decides which of the three states an hour is in, and writes at most one row (spec §5.2, §5.4).
/// </summary>
/// <remarks>
/// <para>
/// The three states, and why the middle one exists at all:
/// </para>
/// <list type="bullet">
/// <item><b>Counted</b> — a row with a count, <em>including a measured zero</em>. We got in and
/// nobody was there, which is a real and useful fact about a game.</item>
/// <item><b>Unmeasurable</b> — a row with no count and a reason. We got in and could not count.</item>
/// <item><b>Nothing</b> — the probe failed, and the availability series carries that instead.</item>
/// </list>
/// <para>
/// Collapsing the middle case into either neighbour is the worst bug this codebase could ship: a game
/// whose <c>DOING</c> header is customised past our parser would render as permanently dark while
/// running perfectly well. It is also the bug the first cut of the spec actually shipped.
/// </para>
/// <para>
/// One row per game per instant — <c>presence_sample</c>'s primary key is <c>(game_id, at)</c>. A
/// probe that read a count from both <c>WHO</c> and MSSP must therefore have chosen between them
/// before it gets here, which is where §5.2's "who outranks mssp" is applied; a second call at the
/// same timestamp is discarded rather than allowed to overwrite the better source.
/// </para>
/// </remarks>
public sealed class PresenceWriter(IPresenceStore store) : IPresenceWriter
{
    public async Task<PresenceOutcome> WriteAsync(
        Guid gameId,
        PresenceReading reading,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reading);

        if (reading.Count is { } count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(reading), count, "A player count read off a game is never negative.");
            }

            if (reading.Reason is not null)
            {
                // A row cannot be both counted and uncountable. Storing one would be a third state
                // the heatmap has no rendering for, so it is refused here as well as by the schema's
                // presence_sample_null_count_has_a_reason constraint.
                throw new ArgumentException(
                    "A counted reading must not carry an unmeasurable reason.", nameof(reading));
            }

            await store.AppendAsync(
                new PresenceSample
                {
                    GameId = gameId,
                    At = at,
                    Count = count,
                    Source = reading.Source,
                    Aggregates = reading.Aggregates,
                },
                cancellationToken);

            return PresenceOutcome.Counted;
        }

        // No count. The reason is not decoration: without it, "we got in and could not count" is
        // indistinguishable from "we never got in", and those render differently by design.
        if (reading.Reason is not { } reason)
        {
            throw new ArgumentException(
                "A reading with no count must say why it could not be counted. A probe that failed "
                + "writes no presence row at all — call the availability writer instead.",
                nameof(reading));
        }

        await store.AppendAsync(
            new PresenceSample
            {
                GameId = gameId,
                At = at,
                Count = null,
                Source = reading.Source,
                Reason = reason,
                Aggregates = reading.Aggregates,
            },
            cancellationToken);

        return PresenceOutcome.RecordedUnmeasurable;
    }
}
