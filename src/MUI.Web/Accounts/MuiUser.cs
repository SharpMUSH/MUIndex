namespace MUI.Web.Accounts;

/// <summary>
/// An account, and deliberately almost nothing about a person (spec §8.2).
/// </summary>
/// <remarks>
/// <b>No email, no password hash, and neither is an omission.</b> Sign-in is passkeys only; §8.2's
/// recovery path is to make a new account and re-verify through the game. The root of trust is the
/// server the operator controls, so an account is a handle to hang a claim on, not an identity worth
/// defending — stealing one reaches no measurement whatever, so an attacker can't make a game look
/// busier, better connected, or more reliable than it was measured to be.
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
