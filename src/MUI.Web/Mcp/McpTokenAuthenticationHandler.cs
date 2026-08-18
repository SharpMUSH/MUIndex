using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace MUI.Web.Mcp;

/// <summary>Options for <see cref="McpTokenAuthenticationHandler"/>: the one secret it checks against.</summary>
public sealed class McpTokenAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// The configured <c>MUI_MCP_TOKEN</c>, or null when nobody has set one.
    /// </summary>
    /// <remarks>
    /// Null is not "accept anything" — see <see cref="McpTokenAuthenticationHandler.HandleAuthenticateAsync"/>,
    /// which fails every request while this is null. Fail closed, not fail open: an administrative
    /// surface that quietly opened up the moment an operator forgot to set an env var would be a worse
    /// failure than one that simply does not answer.
    /// </remarks>
    public string? Token { get; set; }
}

/// <summary>
/// A single shared bearer secret, checked in constant time (spec: MCP endpoint, CLAUDE.md).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is not OAuth and does not pretend to be.</b> The tools this gates are the administrative
/// surface that used to be <c>ssh</c> plus <c>docker compose run --entrypoint mui-crawl</c> — one
/// operator, one secret, set out-of-band on the box exactly as <c>MUI_CRAWL_POSTGRES</c> is. A bearer
/// token compared with <see cref="CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte},ReadOnlySpan{byte})"/>
/// is the whole of what that needs; the framework's JWT-bearer handler would add a token issuer, a
/// signing key and an expiry this deployment has none of.
/// </para>
/// <para>
/// <b>Never <c>==</c> or <see cref="string.Equals(string?, string?)"/> on the token.</b> Both short-circuit
/// on the first mismatched byte, which leaks how much of a guess was right through response timing —
/// exactly the class of bug this handler exists not to reintroduce.
/// </para>
/// </remarks>
public sealed class McpTokenAuthenticationHandler(
    IOptionsMonitor<McpTokenAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<McpTokenAuthenticationOptions>(options, logger, encoder)
{
    /// <summary>The scheme name this is registered under — never the default, so no cookie session ever satisfies it.</summary>
    public const string SchemeName = "MuiMcpToken";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Fail closed. See McpTokenAuthenticationOptions.Token: an unset MUI_MCP_TOKEN must mean
        // "nobody can call this", never "anybody can".
        if (Options.Token is not { Length: > 0 } expected)
        {
            return Task.FromResult(AuthenticateResult.Fail(
                $"{MuiMcp.TokenEnvironmentVariable} is not set; the MCP endpoint refuses every request until it is."));
        }

        if (!Request.Headers.TryGetValue(HeaderNames.Authorization, out var raw)
            || !AuthenticationHeaderValue.TryParse(raw.ToString(), out var header)
            || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || header.Parameter is not { Length: > 0 } presented)
        {
            return Task.FromResult(AuthenticateResult.Fail("No bearer token was presented."));
        }

        var matches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(presented));

        if (!matches)
        {
            return Task.FromResult(AuthenticateResult.Fail("The bearer token did not match."));
        }

        // No user account behind this — it is a shared operator secret, not an identity. The name is
        // fixed rather than read off the token, which carries nothing to read a name out of.
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "mcp-operator")], SchemeName);

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
