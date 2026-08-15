using MUI.Catalog;
using MUI.Catalog.Persistence;

namespace MUI.Crawler;

/// <summary>
/// What a game's verified owner told us about reading their <c>WHO</c> (spec §8.5).
/// </summary>
/// <remarks>
/// <para>
/// A port of its own rather than handing the crawl loop a field store, and the narrowness is the
/// point: this is the <b>only</b> thing the dial path reads an owner's writing for. A loop holding a
/// general field reader is a loop that will grow a second owner-supplied input, and the next one may
/// not be as harmless as a header line — every other owner-declared value is a fact we publish, not
/// an instruction we act on.
/// </para>
/// <para>
/// What comes back is a hint about where to start counting rows. It cannot become a count, and it
/// cannot suppress one: <see cref="MUI.Crawl.WhoParser"/> consults it only after the server's own
/// printed summary, so an owner may say where to look and may not talk us out of a total their
/// server printed for itself.
/// </para>
/// </remarks>
public interface IWhoFormatHints
{
    /// <summary>The header line this game's owner declared, or null if they declared none.</summary>
    Task<string?> ForGameAsync(Guid gameId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The hint as the owner dashboard stored it — an internal field, written only by
/// <see cref="OwnerEnrichment"/>.
/// </summary>
/// <remarks>
/// Read at <see cref="FieldSource.Owner"/> explicitly rather than through the §5.1 precedence ladder.
/// The ladder answers "what is true about this game", and this question is "what did the owner ask
/// us to do" — a value from any other source would be this crawler taking instruction from something
/// it measured, which is a loop rather than a fact.
/// </remarks>
public sealed class StoredWhoFormatHints(IGameFieldStore fields) : IWhoFormatHints
{
    public async Task<string?> ForGameAsync(Guid gameId, CancellationToken cancellationToken = default)
    {
        var declared = await fields.ForGameAsync(gameId, FieldSource.Owner, cancellationToken);

        return declared
            .FirstOrDefault(f => string.Equals(f.Field, InternalFields.WhoHeader, StringComparison.Ordinal))
            is { Value.Length: > 0 } row
            ? row.Value
            : null;
    }
}
