using Microsoft.AspNetCore.Identity;

using MUI.Web.Accounts;

namespace MUI.Web.Tests;

/// <summary>
/// Enough of a user store to find an account and list its passkeys.
/// </summary>
/// <remarks>Persistence, not authorisation: which account a page is for comes from the cascaded principal, and what it may see comes from the claim store.</remarks>
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
