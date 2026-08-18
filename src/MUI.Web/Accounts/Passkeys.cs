using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawler;

using Npgsql;

using MUI.Web.Localization;

namespace MUI.Web.Accounts;

/// <summary>
/// Sign-in, and the endpoints WebAuthn needs on the way (spec §8.2).
/// </summary>
/// <remarks>
/// <b>Passkeys only</b> — no passwords, no email, no federated provider. Recovery (§8.2) is to make a
/// new account and re-verify through the game, since the root of trust is the server the operator
/// controls, not the credential.
/// <b>These four endpoints are the only part of this site that needs JavaScript</b> —
/// <c>navigator.credentials</c> has no scripting-off path, and the boundary is drawn here rather than
/// letting it leak outward: everything else works with scripting disabled.
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
                // A display name, not an address — this name appears beside a claim on a public page.
                options.User.RequireUniqueEmail = false;
            })
            .AddSignInManager()
            .AddUserStore<DapperUserStore>();

        services.AddScoped<IUserStore<MuiUser>>(s => new DapperUserStore(
            s.GetRequiredService<NpgsqlDataSource>(),
            s.GetRequiredService<TimeProvider>()));

        // Claiming's two services are singletons: both are stateless over a pooled NpgsqlDataSource,
        // and ClaimService in particular is consumed by the crawl loop's singleton BackgroundService,
        // which can never legally be given a scoped dependency. TryAdd because AddMuiCrawler registers
        // the same pair for the crawl loop and this method registers it for the dashboard — one
        // deployable calls both (§4.11), and neither may end up with a second instance or lifetime.
        services.TryAddSingleton<IClaimStore>(s => new NpgsqlClaimStore(
            s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton<IGameStore>(s => new NpgsqlGameStore(
            s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton<ClaimService>();

        // The write half of a claim (§8.5), through the same reconciler the crawler's observations
        // use — an owner's value reaches the change feed by the same code path as a measured one, but
        // lands in a row of its own so the two never contend.
        services.AddScoped<OwnerEnrichment>(s =>
        {
            var store = new NpgsqlGameFieldStore(s.GetRequiredService<NpgsqlDataSource>());

            return new OwnerEnrichment(
                new NpgsqlClaimStore(s.GetRequiredService<NpgsqlDataSource>()),
                store,
                new FieldReconciler(store),
                s.GetRequiredService<IFieldRegistry>(),
                s.GetRequiredService<TimeProvider>(),

                // Second half of a NAME write (§5.7): the URL minted from the name is a promise to
                // everybody holding the old one. GetRequiredService rather than GetService — this
                // deployment always registers SlugMinter (AddMuiCrawler, under the same
                // connection-string guard), and asking softly here would let a composition mistake
                // fail silently as owners renaming games with the URL never moving.
                s.GetRequiredService<SlugMinter>());
        });

        // §11's third route, wired to the dashboard rather than only to the CLI.
        services.AddScoped<OwnerOptOut>();

        // The second half of that decision (migration 0025); takes OwnerOptOut rather than re-deriving
        // whether every address already stands opted out.
        services.AddScoped<OwnerListing>();

        services.Configure<IdentityPasskeyOptions>(options =>
        {
            // Set explicitly, never inferred from the host header (a credential-scoping risk per the
            // ASP.NET Core docs) — a passkey is bound to this value.
            options.ServerDomain = configuration["Passkeys:ServerDomain"];
            options.UserVerificationRequirement = "required";
            options.ResidentKeyRequirement = "required";
        });

        services
            .AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies();

        services.AddAuthorization();

        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/account/sign-in";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.SlidingExpiration = true;

            options.ExpireTimeSpan = TimeSpan.FromDays(30);
        });

        return services;
    }

    /// <summary>
    /// The WebAuthn ceremony, as four endpoints the page's script calls in order.
    /// </summary>
    /// <remarks>Minimal APIs rather than Razor handlers: the browser talks JSON here, matching the shapes WebAuthn's JS API expects.</remarks>
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
            // A signed-in operator is adding a second credential to their account; anybody else is
            // creating one, and the account does not exist until the credential comes back verified.
            var user = await users.GetUserAsync(context.User);

            var entity = user is not null
                ? new PasskeyUserEntity
                {
                    Id = await users.GetUserIdAsync(user),
                    Name = user.DisplayName,
                    DisplayName = user.DisplayName,
                }
                : NewAccount(name);

            return TypedResults.Content(
                await signIn.MakePasskeyCreationOptionsAsync(entity),
                contentType: "application/json");

            // Id and name are decided here and nowhere else; `register` reads both back off the
            // attestation rather than deciding either again, since the authenticator makes the id
            // permanent the moment it writes the credential.
            static PasskeyUserEntity NewAccount(string? name)
            {
                var chosen = Naming.Clean(name);

                return new PasskeyUserEntity
                {
                    Id = Guid.CreateVersion7().ToString(),
                    Name = chosen,
                    DisplayName = chosen,
                };
            }
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
                // Created here, at the moment a credential exists to reach it with — and, critically,
                // under the id from the attestation, not a freshly minted GUID. A discoverable
                // credential is resolved later by FindByIdAsync(userHandle); minting a different id
                // here leaves no row the credential's handle can ever find, and the failure is silent
                // until the very next sign-in attempt. TryParse because the id comes back as a string.
                if (!Guid.TryParse(attestation.UserEntity.Id, out var handle))
                {
                    return Results.BadRequest("That passkey could not be registered.");
                }

                user = new MuiUser
                {
                    Id = handle,
                    DisplayName = attestation.UserEntity.DisplayName,
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
                // No user: the browser offers whatever discoverable credential it holds for this
                // domain, which is why ResidentKeyRequirement is "required" at registration.
                await signIn.MakePasskeyRequestOptionsAsync(user: null),
                contentType: "application/json"));

        accounts.MapPost("/passkey/sign-in", async (
            SignInManager<MuiUser> signIn,
            PasskeySubmission submission) =>
        {
            // Asserted and then signed in explicitly, rather than PasskeySignInAsync — that method has
            // no isPersistent and issues only a session cookie, which would make sign-in expire on
            // tab close while registration granted a lasting session. Both doors into one account
            // must agree on how long it stays open.
            var assertion = await signIn.PerformPasskeyAssertionAsync(submission.Credential);

            if (!assertion.Succeeded)
            {
                return Results.Unauthorized();
            }

            await signIn.SignInAsync(assertion.User, isPersistent: true);

            return Results.Ok(new { redirect = DashboardPath });
        });

        // §8.1's on-demand check. It does not dial anything itself — it brings the game's crawl
        // targets forward and the crawler's own loop does the dialling, under CRAWL DELAY and §7.2's
        // address gate. Keeping the two apart stops a public-page button becoming a way to make us
        // connect to a stranger's server on demand: the rate limit is per claim, and a claim can't
        // exist for a game nobody has been offered. See ClaimService.RequestCheckAsync.
        //
        // IGameStore, not IGameQueries: a submitted game is hidden from every public read until
        // claimed (migration 0010), and the public read would make this the second thing a hidden
        // game couldn't do.
        app.MapPost("/g/{slug}/claim/check", async (
            HttpContext context,
            UserManager<MuiUser> users,
            IGameStore games,
            IClaimStore claims,
            ClaimService service,
            string slug) =>
        {
            var user = await users.GetUserAsync(context.User);

            if (user is null || await games.BySlugAsync(slug) is not { } game)
            {
                return Results.Redirect(
                LocaleRouting.Link(context.LocaleOf().Tag, $"/g/{slug}/claim"));
            }

            var mine = (await claims.ForUserAsync(user.Id))
                .FirstOrDefault(c => c.GameId == game.Id && c.RevokedAt is null);

            if (mine is not null)
            {
                await service.RequestCheckAsync(mine.Id);
            }

            return Results.Redirect(
                LocaleRouting.Link(context.LocaleOf().Tag, $"/g/{slug}/claim"));
        }).RequireAuthorization();

        accounts.MapPost("/sign-out", async (HttpContext context, SignInManager<MuiUser> signIn) =>
        {
            await signIn.SignOutAsync();

            // The front page in the language they were reading — signing out is not a language change.
            return Results.Redirect(LocaleRouting.Link(context.LocaleOf().Tag, "/"));
        });

        // §8.5's enrichment and §11's suppression, which are the only writes a claim grants.
        app.MapMuiOwnerWrites();

        // §8.4's counter-claim and §8.5's several owners, as the two forms that reach them.
        app.MapMuiOwnership();
    }

    /// <summary>
    /// What the page posts back after the authenticator has answered.
    /// </summary>
    /// <remarks>The credential and nothing else — the name belongs to <c>registration-options</c>, where the entity is built, not to a value re-typed and trusted here.</remarks>
    public sealed record PasskeySubmission(string Credential);

    /// <summary>
    /// A display name is a label, and this is the whole of what we do to one.
    /// </summary>
    /// <remarks>Bounded and trimmed, with a default rather than a rejection for a blank field. Never treated as a claim about who anybody is.</remarks>
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
