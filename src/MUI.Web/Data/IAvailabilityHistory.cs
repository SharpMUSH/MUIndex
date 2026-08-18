using MUI.Catalog;

namespace MUI.Web.Data;

/// <summary>
/// A game's availability intervals, which the availability strip and the archive are both built on.
/// </summary>
/// <remarks>
/// Belongs on <see cref="IGameQueries"/> and is only separate because that interface is owned
/// elsewhere; folding this in as an <c>AvailabilityAsync(gameId)</c> deletes this file and changes
/// nothing else. Returns <see cref="AvailabilityInterval"/> rather than a strip-shaped view model,
/// since intervals are the stored shape (spec §5.3) and the arithmetic over them already lives in
/// <see cref="Reachability"/>.
/// </remarks>
public interface IAvailabilityHistory
{
    Task<IReadOnlyList<AvailabilityInterval>> ForGameAsync(
        Guid gameId,
        CancellationToken cancellationToken = default);
}
