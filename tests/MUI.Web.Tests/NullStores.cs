using MUI.Catalog;
using MUI.Catalog.Persistence;

namespace MUI.Web.Tests;

/// <summary>
/// The two stores a <see cref="ClaimService"/> is constructed over when nothing will ask it anything.
/// </summary>
/// <remarks>
/// Several surfaces switch on whether a <c>ClaimService</c> resolves at all rather than on anything
/// it answers — <see cref="MUI.Web.Components.Pages.Game.Claimable"/> and the signed-out half of the
/// claim page both do. Every member throws on purpose: a test that reaches one of these has left the
/// state it meant to be describing, and should say so loudly rather than read a default.
/// </remarks>
internal sealed class NullGameStore : IGameStore
{
    public Task<GameRecord?> ByIdAsync(Guid id, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<GameRecord?> BySlugAsync(string slug, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task InsertAsync(GameRecord game, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task ExcludeAsync(Guid id, string reason, DateTimeOffset at, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task IncludeAsync(Guid id, DateTimeOffset at, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task UnlistAsync(Guid id, Guid byUserId, DateTimeOffset at, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task RelistAsync(Guid id, DateTimeOffset at, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task SetStateAsync(Guid id, LifecycleState state, DateTimeOffset at, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task CorroborateAsync(
        Guid id, DateTimeOffset at, IReadOnlyList<string> signals, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task MarkReachableAsync(Guid id, DateTimeOffset at, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<string?> RenameAsync(
        Guid id, string name, string slug, DateTimeOffset at, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task SetClaimedAsync(Guid id, bool isClaimed, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<GameRecord>> UnarchivedAsync(CancellationToken ct = default) =>
        throw new NotSupportedException();
}

internal sealed class NullClaimStore : IClaimStore
{
    public Task<GameClaim?> FindAsync(Guid claimId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<GameClaim>> ForGameAsync(Guid gameId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<GameClaim>> ForUserAsync(Guid userId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<GameClaim?> FindPendingByTokenAsync(Guid gameId, string token, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task InsertAsync(GameClaim claim, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task UpdateAsync(GameClaim claim, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task RecordEventAsync(ClaimEvent claimEvent, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ClaimEvent>> EventsAsync(Guid claimId, CancellationToken ct = default) =>
        throw new NotSupportedException();
}
