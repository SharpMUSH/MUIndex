namespace MUI.Web.Accounts;

/// <summary>
/// An account, and deliberately almost nothing about a person (spec §8.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>No email, no password hash, and neither is an omission.</b> Sign-in is passkeys only, so there
/// is no password to store; and §8.2's recovery path is to make a new account and re-verify through
/// the game, so there is no address to recover to. The root of trust is the server the operator
/// controls, which is why an account here is a durable handle to hang a claim on rather than an
/// identity worth defending.
/// </para>
/// <para>
/// It follows that this is a poor thing to steal, and that is the design working. Taking somebody's
/// account gets you the ability to answer some hand-typed descriptions on games whose real owner
/// takes them back in one probe by publishing a fresh token — <b>and no measurement whatever</b>.
/// Not a player count, not a capability, not an hour of reachability: there is no form on this site
/// that reaches one, so there is nothing here an attacker could use to make a game look busier,
/// better connected or more reliable than it was measured to be.
/// </para>
/// </remarks>
public sealed class MuiUser
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>What the account calls itself. Never verified as a claim about anybody.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Identity's case-insensitive lookup key. Written by Identity, not by us.</summary>
    public string NormalisedName { get; set; } = string.Empty;

    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString();

    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastSignedInAt { get; set; }
}
