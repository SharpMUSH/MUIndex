using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;

using MUI.Catalog;
using MUI.Catalog.Persistence;

using Npgsql;

namespace MUI.Web.Accounts;

/// <summary>
/// Sign-in, and the endpoints WebAuthn needs on the way (spec §8.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Passkeys only.</b> No passwords, no email, no federated provider — we hold a public key and the
/// private key never leaves the operator's authenticator. What usually forces a password back into a
/// passwordless deployment is account recovery, and it does not apply here: §8.2's recovery path is
/// to make a new account and re-verify through the game, because the root of trust is the server the
/// operator controls rather than the credential.
/// </para>
/// <para>
/// <b>These four endpoints are the only part of this site that needs JavaScript.</b>
/// <c>navigator.credentials</c> has no scripting-off path, and rather than let that leak outwards the
/// boundary is drawn here: the catalogue, the game pages, the archive, plain mode and the API all
/// work with scripting disabled, and the part that does not is the part used by people who administer
/// a game server.
/// </para>
/// </remarks>
public static class Passkeys
{
    /// <summary>Where a signed-in operator lands, and where sign-in returns to.</summary>
    public const string DashboardPath = "/account";

    public static IServiceCollection AddMuiAccounts(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddIdentityCore<MuiUser>(options =>
            {
                // A display name, not an address. Identity's default set excludes most punctuation
                // and it is left as-is: this name appears beside a claim on a public page.
                options.User.RequireUniqueEmail = false;
            })
            .AddSignInManager()
            .AddUserStore<DapperUserStore>();

        services.AddScoped<IUserStore<MuiUser>>(s => new DapperUserStore(
            s.GetRequiredService<NpgsqlDataSource>(),
            s.GetRequiredService<TimeProvider>()));

        // Claiming's own two services, and both are singletons for a reason that is not tidiness.
        //
        // The dashboard resolves IClaimStore from the container and treats a null as "this site has
        // no database" — so an unregistered store did not throw, it made every operator's list of
        // claimed games empty on a site that had them. Nothing registered it, here or anywhere.
        //
        // ClaimService was scoped, and the crawl loop that settles beacons is a singleton
        // BackgroundService: a scoped dependency is one CrawlCycle can never legally be given. With
        // scope validation on — which is what `dotnet run` does — the container refused to build at
        // all, so the site did not start with a connection string set. Both are stateless over a
        // pooled NpgsqlDataSource, so a singleton is what they should always have been.
        //
        // TryAdd, because AddMuiCrawler registers the same pair for a crawler-only deployment and
        // the two compositions overlap in the one that runs both.
        services.TryAddSingleton<IClaimStore>(s => new NpgsqlClaimStore(
            s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton<IGameStore>(s => new NpgsqlGameStore(
            s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton<ClaimService>();

        services.Configure<IdentityPasskeyOptions>(options =>
        {
            // Set explicitly rather than inferred from the host header, which the ASP.NET Core docs
            // call a credential-scoping risk. It is also the reason §15.1's open domain question has
            // a deadline: a passkey is bound to this value, and every credential registered before
            // the domain settles has to be registered again after it moves.
            options.ServerDomain = configuration["Passkeys:ServerDomain"];
            options.UserVerificationRequirement = "required";
            options.ResidentKeyRequirement = "required";
        });

        services
            .AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies();

        // Needed by the one endpoint that calls RequireAuthorization — the on-demand claim check.
        services.AddAuthorization();

        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/account/sign-in";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.SlidingExpiration = true;
        });

        return services;
    }

    /// <summary>
    /// The WebAuthn ceremony, as four endpoints the page's script calls in order.
    /// </summary>
    /// <remarks>
    /// Minimal APIs rather than Razor handlers because the browser talks JSON here: the options go
    /// out as the shapes <c>PublicKeyCredential.parseCreationOptionsFromJSON</c> expects, and the
    /// credential comes back as the shape <c>PerformPasskeyAttestationAsync</c> reads.
    /// </remarks>
    public static void MapMuiAccounts(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var accounts = app.MapGroup("/account");

        accounts.MapPost("/passkey/registration-options", async (
            HttpContext context,
            UserManager<MuiUser> users,
            SignInManager<MuiUser> signIn,
            string? name) =>
        {
            // Two arms, and the difference is who is asking. A signed-in operator is adding a second
            // credential to the account they have; anybody else is creating one, and the account does
            // not exist until the credential comes back verified — so nothing is written here.
            var user = await users.GetUserAsync(context.User);

            var entity = user is not null
                ? new PasskeyUserEntity
                {
                    Id = await users.GetUserIdAsync(user),
                    Name = user.DisplayName,
                    DisplayName = user.DisplayName,
                }
                : new PasskeyUserEntity
                {
                    Id = Guid.CreateVersion7().ToString(),
                    Name = Naming.Clean(name),
                    DisplayName = Naming.Clean(name),
                };

            return TypedResults.Content(
                await signIn.MakePasskeyCreationOptionsAsync(entity),
                contentType: "application/json");
        });

        accounts.MapPost("/passkey/register", async (
            HttpContext context,
            UserManager<MuiUser> users,
            SignInManager<MuiUser> signIn,
            TimeProvider time,
            PasskeySubmission submission) =>
        {
            var attestation = await signIn.PerformPasskeyAttestationAsync(submission.Credential);

            if (!attestation.Succeeded)
            {
                return Results.BadRequest(attestation.Failure.Message);
            }

            var user = await users.GetUserAsync(context.User);

            if (user is null)
            {
                // The account is created here, at the moment a credential exists to reach it with.
                // Creating it earlier would leave unreachable accounts behind every abandoned
                // registration.
                user = new MuiUser
                {
                    DisplayName = Naming.Clean(submission.Name),
                    CreatedAt = time.GetUtcNow(),
                };

                var created = await users.CreateAsync(user);

                if (!created.Succeeded)
                {
                    return Results.BadRequest(string.Join(" ", created.Errors.Select(e => e.Description)));
                }
            }

            var stored = await users.AddOrUpdatePasskeyAsync(user, attestation.Passkey);

            if (!stored.Succeeded)
            {
                return Results.BadRequest("That passkey could not be stored.");
            }

            await signIn.SignInAsync(user, isPersistent: true);

            return Results.Ok(new { redirect = DashboardPath });
        });

        accounts.MapPost("/passkey/assertion-options", async (SignInManager<MuiUser> signIn) =>
            TypedResults.Content(
                // No user, so the browser offers whatever discoverable credential it holds for this
                // domain. That is what makes sign-in a single click with nothing typed first, and it
                // is why ResidentKeyRequirement is "required" at registration.
                await signIn.MakePasskeyRequestOptionsAsync(user: null),
                contentType: "application/json"));

        accounts.MapPost("/passkey/sign-in", async (
            SignInManager<MuiUser> signIn,
            PasskeySubmission submission) =>
        {
            var result = await signIn.PasskeySignInAsync(submission.Credential);

            return result.Succeeded
                ? Results.Ok(new { redirect = DashboardPath })
                : Results.Unauthorized();
        });

        // §8.1's on-demand check. It does not dial anything itself — it records that the claimant
        // asked, and the crawler's own scheduler is what brings the probe forward. Keeping the two
        // apart is what stops a button on a public page becoming a way to make us connect to a
        // stranger's server on demand: the rate limit is per claim, and a claim cannot exist for a
        // game nobody has been offered.
        app.MapPost("/g/{slug}/claim/check", async (
            HttpContext context,
            UserManager<MuiUser> users,
            IGameQueries queries,
            IClaimStore claims,
            ClaimService service,
            string slug) =>
        {
            var user = await users.GetUserAsync(context.User);

            if (user is null || await queries.FindAsync(slug) is not { } page)
            {
                return Results.Redirect($"/g/{slug}/claim");
            }

            var mine = (await claims.ForUserAsync(user.Id))
                .FirstOrDefault(c => c.GameId == page.Summary.Id && c.RevokedAt is null);

            if (mine is not null)
            {
                await service.RequestCheckAsync(mine.Id);
            }

            return Results.Redirect($"/g/{slug}/claim");
        }).RequireAuthorization();

        accounts.MapPost("/sign-out", async (SignInManager<MuiUser> signIn) =>
        {
            await signIn.SignOutAsync();

            return Results.Redirect("/");
        });

        // §8.4's counter-claim and §8.5's several owners, as the two forms that reach them.
        app.MapMuiOwnership();
    }

    /// <summary>What the page posts back after the authenticator has answered.</summary>
    public sealed record PasskeySubmission(string Credential, string? Name);

    /// <summary>
    /// A display name is a label, and this is the whole of what we do to one.
    /// </summary>
    /// <remarks>
    /// Bounded and trimmed, with a default rather than a rejection: somebody registering a passkey
    /// has already done the hard part, and refusing them over a blank field would be a poor moment to
    /// start being strict. It is never treated as a claim about who anybody is.
    /// </remarks>
    private static class Naming
    {
        private const int MaxLength = 40;

        public static string Clean(string? name)
        {
            var trimmed = name?.Trim();

            return string.IsNullOrEmpty(trimmed)
                ? $"operator-{Guid.CreateVersion7().ToString()[..8]}"
                : trimmed.Length <= MaxLength ? trimmed : trimmed[..MaxLength];
        }
    }
}
