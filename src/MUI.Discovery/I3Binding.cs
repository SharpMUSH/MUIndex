namespace MUI.Discovery;

/// <summary>
/// An Intermud-3 mud as we last saw it, and the game it turned out to be (spec §7.2, migration 0021).
/// </summary>
/// <remarks>
/// <b>The binding is recorded, not recomputed.</b> I3 names a participant by canonical name and IP
/// literal; we name a game by the hostname a player types. Re-deriving that join every cycle would
/// let DNS silently move a game's I3 identity, so it is written down once with a date on it.
/// </remarks>
public sealed record I3Binding
{
    /// <summary>I3's canonical name. Unique on the network, and case is significant.</summary>
    public required string MudName { get; init; }

    /// <summary>
    /// Null until the address behind this mud has answered a probe and been promoted (spec §7.1).
    /// </summary>
    /// <remarks>
    /// A mud the router lists loudly and that never answers a dial is not a game. That is the same
    /// rule every other address obeys and there is no I3 exemption from it.
    /// </remarks>
    public Guid? GameId { get; init; }

    public required string Host { get; init; }

    public required int Port { get; init; }

    /// <summary>
    /// Whether the mud advertises <c>who</c>.
    /// </summary>
    /// <remarks>
    /// <b>Consent rather than capability.</b> I3's services mapping is the network's own opt-in
    /// mechanism, and a mud that omits <c>who</c> has said it does not answer that question. Stored
    /// beside the binding because the gate and the identity are read together.
    /// </remarks>
    public bool AnswersWho { get; init; }

    /// <summary>
    /// Whether the router last reported this mud up.
    /// </summary>
    /// <remarks>
    /// The network's own words, not a guess: I3 never removes a listing, only marks it down, so a
    /// mud that has left keeps a row here forever. Refreshed on every mudlist pass the same way
    /// <see cref="AnswersWho"/> is, and read together with it at ask time — a down mud will not
    /// answer, so asking spends a packet to learn what the mudlist already said.
    /// </remarks>
    public bool IsUp { get; init; } = true;

    public required DateTimeOffset FirstSeenAt { get; init; }

    public required DateTimeOffset LastSeenAt { get; init; }

    /// <summary>When we last sent a <c>who-req</c>, so the pacing floor survives a restart.</summary>
    public DateTimeOffset? LastAskedAt { get; init; }
}

/// <summary>
/// Where I3 bindings live. Monotonic in the same way the target registry is: a mud that leaves the
/// network keeps its row, because the evidence of where it used to be is worth more than the space.
/// </summary>
public interface II3BindingRepository
{
    /// <summary>
    /// Records what the router said about this mud, creating the row or refreshing the parts that
    /// change. Never clears an established <see cref="I3Binding.GameId"/>.
    /// </summary>
    Task UpsertAsync(
        string mudName,
        string host,
        int port,
        bool answersWho,
        bool isUp,
        DateTimeOffset seenAt,
        CancellationToken ct);

    /// <summary>
    /// Attaches a game to a mud, once its address has been promoted.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if this mud now holds the game, <see langword="false"/> if another
    /// name already does or this mud was already bound.
    /// </returns>
    /// <remarks>
    /// A refusal is an answer here, not an error: one game routinely registers several I3 names (e.g.
    /// <c>The Zone</c> / <c>The Zone-dalet</c> / <c>The Zone-i4</c>, one mud per router), so whichever
    /// name binds first counts the game. Letting the unique index throw broke the whole cycle on
    /// retry for every game with a second I3 name.
    /// </remarks>
    Task<bool> BindAsync(string mudName, Guid gameId, CancellationToken ct);

    /// <summary>Every mud we have ever seen listed.</summary>
    Task<IReadOnlyList<I3Binding>> AllAsync(CancellationToken ct);

    /// <summary>
    /// Bound muds that advertise <c>who</c>, that the router last reported up, and that have not been
    /// asked since <paramref name="notSince"/>.
    /// </summary>
    Task<IReadOnlyList<I3Binding>> AskableAsync(
        DateTimeOffset notSince,
        int limit,
        CancellationToken ct);

    /// <summary>Marks a mud as asked, whether or not it answered.</summary>
    /// <remarks>
    /// <b>Asked, not answered.</b> The pacing floor exists to bound what we send, and a mud that
    /// stays silent must not therefore be asked again immediately — that would turn the quietest
    /// participants into the ones we bother most.
    /// </remarks>
    Task MarkAskedAsync(string mudName, DateTimeOffset at, CancellationToken ct);
}
