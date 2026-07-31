using MUI.Catalog;

namespace MUI.Web.Data;

/// <summary>
/// A game's availability intervals, which the availability strip and the archive are both built on.
/// </summary>
/// <remarks>
/// <para>
/// <b>This belongs on <see cref="IGameQueries"/> and is only here because that interface is owned
/// elsewhere.</b> <see cref="GamePage"/> carries <c>ReachableFraction</c> and <c>LongestOutage</c>,
/// which are two numbers derived from these rows — but the 90-day strip needs the rows themselves
/// (one bar per day, with the worst state and its cause), and the archive needs <em>when a game was
/// last reachable</em> and <em>how long it was known live</em>, which are the same arithmetic again.
/// Folding this into <c>IGameQueries</c> as an <c>AvailabilityAsync(gameId)</c> — or putting the
/// interval list on <c>GamePage</c> directly — deletes this file and changes nothing else.
/// </para>
/// <para>
/// It returns <see cref="AvailabilityInterval"/> rather than a strip-shaped view model deliberately:
/// intervals are the stored shape (spec §5.3), the arithmetic over them is already in
/// <see cref="Reachability"/>, and a store that pre-chewed them into ninety buckets would have to
/// know how wide the strip is.
/// </para>
/// </remarks>
public interface IAvailabilityHistory
{
    Task<IReadOnlyList<AvailabilityInterval>> ForGameAsync(
        Guid gameId,
        CancellationToken cancellationToken = default);
}
