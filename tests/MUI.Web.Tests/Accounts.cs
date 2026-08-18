using Microsoft.AspNetCore.Identity;

using MUI.Web.Accounts;

namespace MUI.Web.Tests;

/// <summary>
/// Enough of a user store to find an account and list its passkeys.
/// </summary>
/// <remarks>
/// <para>
/// Identity's <c>UserManager</c> is a concrete type over a store interface, so a page that calls it
/// needs one. This is persistence rather than authorisation: which account a page is for comes from
/// the cascaded principal, and what that account may see comes from the claim store.
/// </para>
/// <para>
/// Its own file because two surfaces need it. The dashboard and the claim page both go through
/// <c>UserManager.GetUserAsync</c> before they render anything an owner can act on, and the second
/// one could not be rendered at all while this was private to the first — so the claim page's
/// signed-in states had no test, which is the same gap the dashboard was in before it got one.
/// </para>
/// </remarks>
internal sealed class Accounts(List<MuiUser> users, DateTimeOffset now)
    : IUserStore<MuiUser>, IUserPasskeyStore<MuiUser>
{
    public Task<MuiUser?> FindByIdAsync(string id, CancellationToken ct) =>
        Task.FromResult(users.FirstOrDefault(u => u.Id.ToString() == id));

    public Task<string> GetUserIdAsync(MuiUser user, CancellationToken ct) =>
        Task.FromResult(user.Id.ToString());

    public Task<string?> GetUserNameAsync(MuiUser user, CancellationToken ct) =>
        Task.FromResult<string?>(user.DisplayName);

    public Task<string?> GetNormalizedUserNameAsync(MuiUser user, CancellationToken ct) =>
        Task.FromResult<string?>(user.NormalisedName);

    public Task<MuiUser?> FindByNameAsync(string name, CancellationToken ct) =>
        Task.FromResult(users.FirstOrDefault(u => u.NormalisedName == name));

    public Task SetUserNameAsync(MuiUser user, string? name, CancellationToken ct) => Task.CompletedTask;

    public Task SetNormalizedUserNameAsync(MuiUser user, string? name, CancellationToken ct) =>
        Task.CompletedTask;

    public Task<IdentityResult> CreateAsync(MuiUser user, CancellationToken ct) =>
        Task.FromResult(IdentityResult.Success);

    public Task<IdentityResult> UpdateAsync(MuiUser user, CancellationToken ct) =>
        Task.FromResult(IdentityResult.Success);

    public Task<IdentityResult> DeleteAsync(MuiUser user, CancellationToken ct) =>
        Task.FromResult(IdentityResult.Success);

    public Task AddOrUpdatePasskeyAsync(MuiUser user, UserPasskeyInfo passkey, CancellationToken ct) =>
        Task.CompletedTask;

    public Task<IList<UserPasskeyInfo>> GetPasskeysAsync(MuiUser user, CancellationToken ct) =>
        Task.FromResult<IList<UserPasskeyInfo>>(
        [
            new UserPasskeyInfo(
                [1, 2, 3],
                [4, 5, 6],
                now.AddYears(-1),
                0u,
                ["internal"],
                true,
                true,
                true,
                [],
                [])
            {
                Name = "the yubikey in the drawer",
            },
        ]);

    public Task<MuiUser?> FindByPasskeyIdAsync(byte[] id, CancellationToken ct) =>
        Task.FromResult<MuiUser?>(null);

    public Task<UserPasskeyInfo?> FindPasskeyAsync(MuiUser user, byte[] id, CancellationToken ct) =>
        Task.FromResult<UserPasskeyInfo?>(null);

    public Task RemovePasskeyAsync(MuiUser user, byte[] id, CancellationToken ct) => Task.CompletedTask;

    public void Dispose()
    {
    }
}
