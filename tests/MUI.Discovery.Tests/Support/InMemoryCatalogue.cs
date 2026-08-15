using MUI.Catalog;

namespace MUI.Discovery.Tests.Support;

/// <summary>The games that exist. Nothing more: identity only ever asks whether one still does.</summary>
public sealed class InMemoryGameDirectory : IGameDirectory
{
    private readonly HashSet<Guid> _games = [];

    public Guid Add()
    {
        var id = Guid.CreateVersion7();
        _games.Add(id);
        return id;
    }

    /// <summary>Forgets a game without touching its endpoints or fields — the orphaned-row shape.</summary>
    public void Forget(Guid gameId) => _games.Remove(gameId);

    public Task<bool> ExistsAsync(Guid gameId, CancellationToken ct) => Task.FromResult(_games.Contains(gameId));
}

/// <summary>
/// Addresses to games. Keyed on the canonical host and the port, because two ports on one machine are
/// two endpoints and often two different games.
/// </summary>
public sealed class InMemoryEndpointDirectory : IEndpointDirectory
{
    private readonly Dictionary<(string Host, int Port), KnownEndpoint> _endpoints = new();

    public IReadOnlyCollection<KnownEndpoint> All => _endpoints.Values.ToList();

    public Task<KnownEndpoint?> ByAddressAsync(string host, int port, CancellationToken ct) =>
        Task.FromResult(_endpoints.GetValueOrDefault((CanonicalHost.Normalize(host), port)));

    public Task UpsertAsync(KnownEndpoint endpoint, CancellationToken ct)
    {
        _endpoints[(CanonicalHost.Normalize(endpoint.Host), endpoint.Port)] = endpoint;
        return Task.CompletedTask;
    }
}

/// <summary>
/// The field store and its reverse index over <b>one</b> dictionary.
/// </summary>
/// <remarks>
/// The two live on one type deliberately, and that is not tidiness: a test cannot then seed a field
/// through <see cref="UpsertAsync"/> that the matcher fails to find. Two fakes over two dictionaries
/// would let the matcher's tests pass against a world the matcher could never see.
/// </remarks>
public sealed class InMemoryGameFieldStore : IGameFieldStore, IGameFieldIndex
{
    private readonly Dictionary<(Guid GameId, string Field, FieldSource Source), GameField> _fields = new();
    private readonly List<FieldChange> _changes = [];

    public IReadOnlyList<FieldChange> Changes => _changes;

    public Task<IReadOnlyList<GameField>> ForGameAsync(Guid gameId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GameField>>(_fields.Values.Where(f => f.GameId == gameId).ToList());

    public Task<IReadOnlyList<GameField>> ForGameAsync(
        Guid gameId,
        FieldSource only,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GameField>>(
            [.. _fields.Values.Where(f => f.GameId == gameId && f.Source == only)]);

    public Task UpsertAsync(GameField field, CancellationToken cancellationToken = default)
    {
        _fields[(field.GameId, field.Field, field.Source)] = field;
        return Task.CompletedTask;
    }

    public Task RecordChangeAsync(FieldChange change, CancellationToken cancellationToken = default)
    {
        _changes.Add(change);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Case-insensitive on both field name and value, trimmed on both sides, distinct game ids — the
    /// comparison <see cref="IGameFieldIndex"/> states, so a database implementation and this one agree.
    /// </summary>
    public Task<IReadOnlyList<Guid>> GamesWithFieldAsync(string field, string value, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Guid>>(_fields.Values
            .Where(f => string.Equals(f.Field, field, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(f.Value.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(f => f.GameId)
            .Distinct()
            .ToList());
}
