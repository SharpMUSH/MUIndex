using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using MUI.Web.Accounts;

namespace MUI.Web.Tests;

/// <summary>
/// The whole WebAuthn ceremony, both halves, driven by an authenticator written here.
/// </summary>
/// <remarks>
/// <para>
/// <b>Registering and signing in were never tested together, and that is exactly where the defect
/// was.</b> <c>/passkey/register</c> signs the operator in itself, so a suite that stops at "the
/// credential was stored" watches the door being held open by the hand that just came through it.
/// The first request that actually needed the passkey was the one after the process restarted, in
/// production, and it answered 401: the user handle burned into the credential at
/// <c>registration-options</c> was a GUID that no account ever had.
/// </para>
/// <para>
/// So the assertion here is the one property no single-endpoint test can state — <b>a passkey
/// registered on this site signs in on this site</b> — and the cookie jar is emptied between the two
/// halves, because a restart is precisely the event that removes the session and leaves the
/// credential.
/// </para>
/// <para>
/// <see cref="SoftwareAuthenticator"/> is a real authenticator in the only sense that matters here:
/// it holds a P-256 key, it signs what WebAuthn says to sign, and — the part under test — it hands
/// back the user handle it was given at registration, months later, exactly as a phone would. No
/// database: the store is in-memory, because what is under test is which identifier the endpoints
/// agree on and not the SQL that persists it, and <see cref="DapperUserStore"/> has its own coverage.
/// </para>
/// </remarks>
public class PasskeyCeremonyTests
{
    [Test]
    public async Task APasskeyRegisteredOnThisSiteSignsInAfterTheSessionIsGone()
    {
        await using var host = await CeremonyHost.StartAsync();
        var authenticator = new SoftwareAuthenticator();

        var creationOptions = await host.PostAsync(
            "/account/passkey/registration-options?name=probe", content: null);

        await Assert.That(creationOptions.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var registered = await host.PostAsync(
            "/account/passkey/register",
            Envelope(authenticator.Create(await creationOptions.Content.ReadAsStringAsync())));

        await Assert.That(registered.StatusCode).IsEqualTo(HttpStatusCode.OK);

        // The restart. The credential outlives the process; the sign-in cookie does not.
        host.ForgetSession();

        var requestOptions = await host.PostAsync(
            "/account/passkey/assertion-options", content: null);

        await Assert.That(requestOptions.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var signedIn = await host.PostAsync(
            "/account/passkey/sign-in",
            Envelope(authenticator.Get(await requestOptions.Content.ReadAsStringAsync())));

        await Assert.That(signedIn.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    /// <summary>
    /// The account's identifier is the user handle the authenticator was given, and not a second one.
    /// </summary>
    /// <remarks>
    /// The test above fails for this reason and would also fail for a dozen others, so this one names
    /// it: a WebAuthn user handle is written into the credential once and is not renegotiable, which
    /// makes the identifier minted at <c>registration-options</c> the account's identifier rather
    /// than a placeholder standing in until a real one exists.
    /// </remarks>
    [Test]
    public async Task TheAccountKeepsTheUserHandleTheAuthenticatorWasGiven()
    {
        await using var host = await CeremonyHost.StartAsync();
        var authenticator = new SoftwareAuthenticator();

        var creationOptions = await host.PostAsync(
            "/account/passkey/registration-options?name=probe", content: null);

        var offered = JsonNode.Parse(await creationOptions.Content.ReadAsStringAsync())!["user"]!["id"]!
            .GetValue<string>();

        await host.PostAsync(
            "/account/passkey/register",
            Envelope(authenticator.Create(await creationOptions.Content.ReadAsStringAsync())));

        var account = host.Store.Users.Single();

        await Assert.That(account.Id.ToString())
            .IsEqualTo(Encoding.UTF8.GetString(Base64Url.Decode(offered)));
    }

    /// <summary>What <c>passkey.js</c> posts: the credential as a JSON string inside a JSON object.</summary>
    private static string Envelope(string credentialJson) =>
        JsonSerializer.Serialize(new { credential = credentialJson });

    /// <summary>The site as the deployable composes it, with the account store held in memory.</summary>
    private sealed class CeremonyHost : IAsyncDisposable
    {
        /// <summary>Never connected to. Only its presence matters: it is what maps the accounts.</summary>
        private const string ConnectionString = "Host=127.0.0.1;Port=1;Database=mui;Username=mui";

        private readonly WebApplication _app;
        private readonly HttpClient _client;
        private readonly CookieContainer _cookies;

        private CeremonyHost(
            WebApplication app,
            HttpClient client,
            CookieContainer cookies,
            InMemoryUserStore store)
        {
            _app = app;
            _client = client;
            _cookies = cookies;
            Store = store;
        }

        public InMemoryUserStore Store { get; }

        public static async Task<CeremonyHost> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Production,
            });

            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");

            // Stated rather than inferred from the host header, as the deployment states it — and it
            // has to be, because the origin the authenticator below signs is a name and the loopback
            // address this host binds is not one.
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Passkeys:ServerDomain"] = SoftwareAuthenticator.ServerDomain,
            });

            builder.Services.AddMuiSite(builder.Configuration, ConnectionString);

            var store = new InMemoryUserStore();
            builder.Services.AddScoped<IUserStore<MuiUser>>(_ => store);

            if (builder.Services.FirstOrDefault(d =>
                    d.ImplementationType?.Name == "CrawlerService") is { } crawler)
            {
                builder.Services.Remove(crawler);
            }

            var app = builder.Build();
            app.UseMuiSite(ConnectionString);

            await app.StartAsync();

            var address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.First();

            var cookies = new CookieContainer();

            var client = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false,
                CookieContainer = cookies,
            })
            {
                BaseAddress = new Uri(address),
            };

            // Identity checks the origin the authenticator signed against the request's own Origin
            // header, which is a browser's word for the page the ceremony was started from. A client
            // that omits it is refused before any signature is looked at, so the harness sends what
            // a browser on the configured domain would send.
            client.DefaultRequestHeaders.Add("Origin", $"https://{SoftwareAuthenticator.ServerDomain}");

            return new CeremonyHost(app, client, cookies, store);
        }

        /// <summary>What a restart does to a browser that comes back: the cookies are no longer good.</summary>
        public void ForgetSession()
        {
            foreach (Cookie cookie in _cookies.GetAllCookies())
            {
                cookie.Expired = true;
            }
        }

        public Task<HttpResponseMessage> PostAsync(string path, string? content) =>
            _client.PostAsync(
                path,
                content is null
                    ? null
                    : new StringContent(content, Encoding.UTF8, "application/json"));

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    /// <summary>
    /// An authenticator, in software: one P-256 key, one credential, and the user handle it was told.
    /// </summary>
    /// <remarks>
    /// Deliberately not a mock. Every byte the endpoints verify is produced here the way a real
    /// authenticator produces it — the authenticator data with its RP-ID hash and flags, the
    /// self-attested credential with no attestation statement, and an ECDSA signature over the
    /// concatenation WebAuthn §7.2 names — so a test passing here says the server's verification
    /// accepted a credential rather than that a fake said it should.
    /// </remarks>
    private sealed class SoftwareAuthenticator
    {
        public const string ServerDomain = "localhost";

        private static readonly byte[] Aaguid = new byte[16];

        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly byte[] _credentialId = RandomNumberGenerator.GetBytes(16);

        /// <summary>Kept from registration and handed back at every sign-in, as a real one does.</summary>
        private string _userHandle = string.Empty;

        /// <summary>Up on every assertion. A counter that never moved would be a cloned credential.</summary>
        private uint _signCount;

        public string Create(string creationOptionsJson)
        {
            var options = JsonNode.Parse(creationOptionsJson)!;

            _userHandle = options["user"]!["id"]!.GetValue<string>();

            var clientData = ClientData("webauthn.create", options["challenge"]!.GetValue<string>());
            var authenticatorData = AuthenticatorData(includeCredential: true);

            var attestation = new CborWriter()
                .Map(3)
                .Text("fmt").Text("none")
                .Text("attStmt").Map(0)
                .Text("authData").Bytes(authenticatorData)
                .ToArray();

            return JsonSerializer.Serialize(new
            {
                authenticatorAttachment = "platform",
                clientExtensionResults = new { },
                id = Base64Url.Encode(_credentialId),
                rawId = Base64Url.Encode(_credentialId),
                response = new
                {
                    attestationObject = Base64Url.Encode(attestation),
                    clientDataJSON = Base64Url.Encode(clientData),
                    transports = new[] { "internal" },
                },
                type = "public-key",
            });
        }

        public string Get(string requestOptionsJson)
        {
            var options = JsonNode.Parse(requestOptionsJson)!;

            _signCount++;

            var clientData = ClientData("webauthn.get", options["challenge"]!.GetValue<string>());
            var authenticatorData = AuthenticatorData(includeCredential: false);

            var signed = new byte[authenticatorData.Length + 32];
            authenticatorData.CopyTo(signed, 0);
            SHA256.HashData(clientData).CopyTo(signed, authenticatorData.Length);

            var signature = _key.SignData(
                signed, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

            return JsonSerializer.Serialize(new
            {
                authenticatorAttachment = "platform",
                clientExtensionResults = new { },
                id = Base64Url.Encode(_credentialId),
                rawId = Base64Url.Encode(_credentialId),
                response = new
                {
                    authenticatorData = Base64Url.Encode(authenticatorData),
                    clientDataJSON = Base64Url.Encode(clientData),
                    signature = Base64Url.Encode(signature),
                    userHandle = _userHandle,
                },
                type = "public-key",
            });
        }

        private static byte[] ClientData(string type, string challenge) =>
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                type,
                challenge,
                origin = $"https://{ServerDomain}",
                crossOrigin = false,
            }));

        private byte[] AuthenticatorData(bool includeCredential)
        {
            // UserPresent and UserVerified always — the site asks for verification — and
            // AttestedCredentialData only when the credential is being created. BackupEligible and
            // BackedUp stay clear in both halves: the server checks the two agree, so a single-device
            // credential has to look like one at sign-in too.
            var flags = (byte)(0x01 | 0x04 | (includeCredential ? 0x40 : 0x00));

            var data = new List<byte>(SHA256.HashData(Encoding.UTF8.GetBytes(ServerDomain)))
            {
                flags,
            };

            data.AddRange([
                (byte)(_signCount >> 24), (byte)(_signCount >> 16),
                (byte)(_signCount >> 8), (byte)_signCount]);

            if (includeCredential)
            {
                var parameters = _key.ExportParameters(includePrivateParameters: false);

                data.AddRange(Aaguid);
                data.AddRange([(byte)(_credentialId.Length >> 8), (byte)_credentialId.Length]);
                data.AddRange(_credentialId);

                // COSE_Key: EC2 over P-256, ES256, with the two coordinates.
                data.AddRange(new CborWriter()
                    .Map(5)
                    .Integer(1).Integer(2)
                    .Integer(3).Integer(-7)
                    .Integer(-1).Integer(1)
                    .Integer(-2).Bytes(parameters.Q.X!)
                    .Integer(-3).Bytes(parameters.Q.Y!)
                    .ToArray());
            }

            return [.. data];
        }
    }

    /// <summary>
    /// Enough CBOR to write an attestation object and a COSE key, and no more.
    /// </summary>
    /// <remarks>
    /// Hand-written rather than a package reference: the shapes are two fixed maps of small integers,
    /// byte strings and text strings, and a test project taking a dependency to emit forty bytes is
    /// a worse trade than thirty lines that can be read in one sitting.
    /// </remarks>
    private sealed class CborWriter
    {
        private readonly List<byte> _bytes = [];

        public CborWriter Map(int count) => Head(major: 5, (ulong)count);

        public CborWriter Integer(long value) => value >= 0
            ? Head(major: 0, (ulong)value)
            : Head(major: 1, (ulong)(-1 - value));

        public CborWriter Bytes(byte[] value)
        {
            Head(major: 2, (ulong)value.Length);
            _bytes.AddRange(value);

            return this;
        }

        public CborWriter Text(string value)
        {
            var utf8 = Encoding.UTF8.GetBytes(value);

            Head(major: 3, (ulong)utf8.Length);
            _bytes.AddRange(utf8);

            return this;
        }

        public byte[] ToArray() => [.. _bytes];

        private CborWriter Head(int major, ulong value)
        {
            var prefix = (byte)(major << 5);

            if (value < 24)
            {
                _bytes.Add((byte)(prefix | (byte)value));
            }
            else if (value <= byte.MaxValue)
            {
                _bytes.AddRange([(byte)(prefix | 24), (byte)value]);
            }
            else if (value <= ushort.MaxValue)
            {
                _bytes.AddRange([(byte)(prefix | 25), (byte)(value >> 8), (byte)value]);
            }
            else
            {
                _bytes.AddRange([
                    (byte)(prefix | 26),
                    (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
            }

            return this;
        }
    }

    /// <summary>
    /// <see cref="DapperUserStore"/>'s two interfaces over dictionaries.
    /// </summary>
    /// <remarks>
    /// The SQL store is tested against a real Postgres elsewhere. What this suite needs from a store
    /// is that it answers the same questions in the same order, so the property under test is the
    /// identifier the endpoints agree on rather than the availability of a database.
    /// </remarks>
    private sealed class InMemoryUserStore : IUserStore<MuiUser>, IUserPasskeyStore<MuiUser>
    {
        private readonly List<MuiUser> _users = [];
        private readonly Dictionary<string, (Guid User, UserPasskeyInfo Passkey)> _passkeys = [];

        public IReadOnlyList<MuiUser> Users => _users;

        public Task<string> GetUserIdAsync(MuiUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.Id.ToString());

        public Task<string?> GetUserNameAsync(MuiUser user, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(user.DisplayName);

        public Task SetUserNameAsync(MuiUser user, string? userName, CancellationToken cancellationToken)
        {
            user.DisplayName = userName ?? string.Empty;

            return Task.CompletedTask;
        }

        public Task<string?> GetNormalizedUserNameAsync(
            MuiUser user,
            CancellationToken cancellationToken) =>
            Task.FromResult<string?>(user.NormalisedName);

        public Task SetNormalizedUserNameAsync(
            MuiUser user,
            string? normalizedName,
            CancellationToken cancellationToken)
        {
            user.NormalisedName = normalizedName ?? string.Empty;

            return Task.CompletedTask;
        }

        public Task<IdentityResult> CreateAsync(MuiUser user, CancellationToken cancellationToken)
        {
            _users.Add(user);

            return Task.FromResult(IdentityResult.Success);
        }

        public Task<IdentityResult> UpdateAsync(MuiUser user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> DeleteAsync(MuiUser user, CancellationToken cancellationToken)
        {
            _users.Remove(user);

            return Task.FromResult(IdentityResult.Success);
        }

        public Task<MuiUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(_users.FirstOrDefault(u => u.Id.ToString() == userId));

        public Task<MuiUser?> FindByNameAsync(
            string normalizedUserName,
            CancellationToken cancellationToken) =>
            Task.FromResult(_users.FirstOrDefault(u => u.NormalisedName == normalizedUserName));

        public Task AddOrUpdatePasskeyAsync(
            MuiUser user,
            UserPasskeyInfo passkey,
            CancellationToken cancellationToken)
        {
            _passkeys[Convert.ToHexString(passkey.CredentialId)] = (user.Id, passkey);

            return Task.CompletedTask;
        }

        public Task<IList<UserPasskeyInfo>> GetPasskeysAsync(
            MuiUser user,
            CancellationToken cancellationToken) =>
            Task.FromResult<IList<UserPasskeyInfo>>(
                [.. _passkeys.Values.Where(p => p.User == user.Id).Select(p => p.Passkey)]);

        public Task<MuiUser?> FindByPasskeyIdAsync(
            byte[] credentialId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                _passkeys.TryGetValue(Convert.ToHexString(credentialId), out var found)
                    ? _users.FirstOrDefault(u => u.Id == found.User)
                    : null);

        public Task<UserPasskeyInfo?> FindPasskeyAsync(
            MuiUser user,
            byte[] credentialId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                _passkeys.TryGetValue(Convert.ToHexString(credentialId), out var found)
                && found.User == user.Id
                    ? found.Passkey
                    : null);

        public Task RemovePasskeyAsync(
            MuiUser user,
            byte[] credentialId,
            CancellationToken cancellationToken)
        {
            _passkeys.Remove(Convert.ToHexString(credentialId));

            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }

    /// <summary>Base64url, which is the only encoding WebAuthn speaks.</summary>
    private static class Base64Url
    {
        public static string Encode(byte[] value) =>
            Convert.ToBase64String(value).Replace('+', '-').Replace('/', '_').TrimEnd('=');

        public static byte[] Decode(string value) =>
            Convert.FromBase64String(
                value.Replace('-', '+').Replace('_', '/').PadRight(
                    value.Length + ((4 - (value.Length % 4)) % 4), '='));
    }
}
