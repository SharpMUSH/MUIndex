namespace MUI.Import;

/// <summary>What a history write actually did, and what it refused.</summary>
public sealed record HistoryWrite(int PresenceRows, int AvailabilityRows, int Refused)
{
    public static readonly HistoryWrite Nothing = new(0, 0, 0);
}

/// <summary>Where an imported game's history goes — if anywhere.</summary>
public interface IHistorySink
{
    Task<HistoryWrite> WriteAsync(Guid gameId, ImportedGame game, CancellationToken cancellationToken);
}

/// <summary>
/// The asserted tier's sink.
/// </summary>
/// <remarks>
/// <para>
/// <b>It takes no constructor parameters, and that is the enforcement.</b> Spec §7.6's "no history,
/// no presence, no grace" is a fact about this type — it holds no writer, no store and no clock, so
/// there is nothing it could write with — rather than a rule somebody has to remember at each of the
/// places history is offered.
/// </para>
/// <para>
/// It still <em>counts</em> what it refused. A run against a hand-maintained list that publishes
/// player counts should report that it declined 5,000 rows, not that it found none: the two read
/// identically in a silent sink, and only one of them is a source worth writing to about a bulk
/// export.
/// </para>
/// </remarks>
public sealed class AssertedHistorySink : IHistorySink
{
    public Task<HistoryWrite> WriteAsync(Guid gameId, ImportedGame game, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);

        var offered = game.Presence.Count + game.Availability.Count;

        return Task.FromResult(offered == 0 ? HistoryWrite.Nothing : new HistoryWrite(0, 0, offered));
    }
}

/// <summary>The tier chooses the sink, and nothing else does.</summary>
public static class HistorySink
{
    public static IHistorySink For(
        ImportTier tier,
        IImportWriter writer,
        IImportProvenanceStore provenance,
        DateTimeOffset importedAt) =>
        tier is ImportTier.Measured
            ? new MeasuredHistorySink(writer, provenance, importedAt)
            : new AssertedHistorySink();
}
