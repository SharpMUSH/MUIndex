using MUI.Discovery;

using Npgsql;

namespace MUI.Crawler.Persistence;

/// <summary>
/// Recognises <c>merge_log</c>'s two schema-level guards (migration 0018) so <see cref="NpgsqlMergeLog"/>
/// and <see cref="NpgsqlUnitOfWork"/> -- the two places either can actually surface, depending on
/// whether the insert shares a transaction with a later write -- can translate a raw
/// <see cref="PostgresException"/> into the Npgsql-agnostic exception MUI.Discovery is allowed to catch
/// (it must not reference Npgsql itself; see <see cref="IUnitOfWork"/>'s own doc comment).
/// </summary>
internal static class MergeLogConstraintViolations
{
    /// <summary>
    /// <c>merge_log_absorbed_once_idx</c>: a loser already folded in elsewhere. A plain partial unique
    /// index, not deferrable, so this always fires inside the insert itself and the constraint name
    /// Postgres reports is reliable -- the same idiom <c>NpgsqlI3BindingRepository.BindAsync</c> uses
    /// for <c>i3_mud_one_per_game_idx</c>.
    /// </summary>
    public static bool IsAlreadyAbsorbed(PostgresException error) =>
        error.SqlState == PostgresErrorCodes.UniqueViolation
        && error.ConstraintName == "merge_log_absorbed_once_idx";

    /// <summary>
    /// <c>merge_log_no_chains</c>: raised by hand inside the <c>merge_log_refuses_chains</c> PL/pgSQL
    /// trigger function (migration 0018) rather than reported by a real schema constraint, so Postgres
    /// never fills in a constraint name for it -- only the <c>ERRCODE</c> the trigger sets explicitly
    /// (<c>integrity_constraint_violation</c>, class 23000) is available to match on. Confirmed against
    /// a real Postgres that no constraint name reaches the client for this one; nothing else in this
    /// schema raises that code, so the class code alone is unambiguous here.
    /// </summary>
    public static bool IsRedirectChain(PostgresException error) =>
        error.SqlState == PostgresErrorCodes.IntegrityConstraintViolation;
}
