namespace MUI.Discovery;

/// <summary>
/// <c>merge_log_absorbed_once_idx</c> (migration 0018) refusing an insert: the loser is already
/// absorbed by some other game.
/// </summary>
/// <remarks>
/// Deliberately Npgsql-agnostic. <see cref="ReviewMergeService"/> lives in MUI.Discovery, which must
/// not reference Npgsql (see <see cref="IUnitOfWork"/>'s own doc comment) — so the Npgsql-aware
/// persistence layer that actually recognises the constraint violation (<c>NpgsqlMergeLog.RecordAsync</c>)
/// throws this instead of letting the raw <c>PostgresException</c> cross that boundary, and
/// <see cref="ReviewMergeService.MergeAsync"/> catches it to produce <see cref="MergeVerdict.AlreadyAbsorbed"/>.
/// </remarks>
public sealed class MergeAlreadyAbsorbedException(string databaseMessage) : Exception(databaseMessage);

/// <summary>
/// The <c>merge_log_no_chains</c> trigger (migration 0018) refusing a merge that would leave a game
/// absorbed but redirecting nowhere.
/// </summary>
/// <remarks>
/// See <see cref="MergeAlreadyAbsorbedException"/> for why this is Npgsql-agnostic. The trigger is
/// <c>DEFERRABLE INITIALLY DEFERRED</c>, so — unlike the unique-index case — this can surface either
/// from the insert itself (when it runs as its own implicit transaction) or from a later commit (when
/// the insert shares an explicit transaction with a further write, as
/// <see cref="ReviewMergeService.MergeAsync"/>'s does); both <c>NpgsqlMergeLog.RecordAsync</c> and
/// <c>NpgsqlUnitOfWork.CommitAsync</c> recognise it and throw this.
/// </remarks>
public sealed class MergeWouldChainException(string databaseMessage) : Exception(databaseMessage);
