namespace MUI.Web.Mcp;

/// <summary>
/// The authenticated MCP endpoint that replaces the ssh/scp/<c>docker compose run --entrypoint
/// mui-crawl</c> administration dance (CLAUDE.md's "Administering the site" section).
/// </summary>
/// <remarks>
/// <para>
/// <b>A narrow, purpose-built tool surface, not the CLI in a trenchcoat.</b> The Dockerfile keeps
/// <c>mui-crawl</c> out of the deployed image on purpose — see its own comment — because an image that
/// shipped the raw, all-flags binary would invite it into a container's entrypoint. This is a
/// different thing: eight specific, authenticated tools (<see cref="MuiMcpTools"/>) built on the exact
/// same library services the CLI uses, mounted as a route group inside the one deployable rather than
/// a second image or a second process.
/// </para>
/// <para>
/// <b>Mapped only where there is a database</b> — inside <c>SiteComposition</c>'s
/// <c>connectionString is not null</c> branch, exactly like accounts, submissions and icons. Every one
/// of the eight tools needs the Postgres-backed crawler graph <c>AddMuiCrawler</c> registers; there is
/// nothing useful for this route to do against the demo fixture.
/// </para>
/// </remarks>
public static class MuiMcp
{
    /// <summary>Where the endpoint is mounted.</summary>
    public const string Route = "/mcp";

    /// <summary>The shared bearer secret. Unset means "refuse every request", never "allow every request".</summary>
    public const string TokenEnvironmentVariable = "MUI_MCP_TOKEN";

    public const string TokenConfigurationKey = "Mcp:Token";

    /// <summary>
    /// Registers the MCP server and its authentication scheme.
    /// </summary>
    /// <remarks>
    /// Called after <c>AddMuiAccounts</c> in <c>SiteComposition.AddMuiSite</c>, so
    /// <c>services.AddAuthentication(IdentityConstants.ApplicationScheme)</c> has already set the
    /// default scheme. This calls the parameterless <c>AddAuthentication()</c> overload deliberately —
    /// it only adds a second scheme alongside the default and must never touch which one that is, or a
    /// signed-in operator's cookie would start being asked to authenticate an unrelated route.
    /// </remarks>
    public static IServiceCollection AddMuiMcp(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var token = ResolveToken(configuration);

        services
            .AddAuthentication()
            .AddScheme<McpTokenAuthenticationOptions, McpTokenAuthenticationHandler>(
                McpTokenAuthenticationHandler.SchemeName,
                options => options.Token = token);

        // Not AddAuthorization() again here: SiteComposition.AddMuiSite calls AddMuiMcp only inside
        // the same connectionString-is-not-null branch where AddMuiAccounts has already called it, and
        // the two must share one AuthorizationOptions rather than each configuring their own.
        services
            .AddMcpServer()
            .WithHttpTransport(options =>
            {
                // No server-to-client requests (sampling, elicitation) from any of the seven tools,
                // and stateless is what lets a tool call reuse the ASP.NET Core request's own DI scope
                // — see MuiMcpTools' own remarks on why that matters for reusing the process's
                // singleton CrawlCycle rather than a second graph.
                options.Stateless = true;
            })
            .WithTools<MuiMcpTools>();

        return services;
    }

    /// <summary>Maps the route, gated on the token scheme and nothing else.</summary>
    /// <remarks>
    /// Scoped to <see cref="McpTokenAuthenticationHandler.SchemeName"/> explicitly, rather than a bare
    /// <c>RequireAuthorization()</c> that would accept whatever the default scheme's policy allows —
    /// this route must never be reachable with a signed-in operator's cookie session, which is a
    /// different trust boundary entirely (a game owner, not somebody administering the site).
    /// </remarks>
    public static WebApplication UseMuiMcp(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapMcp(Route)
            .RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(McpTokenAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser());

        return app;
    }

    /// <summary>
    /// Environment first, then configuration — the same order <c>PostgresData</c> and
    /// <c>CrawlerSettings</c> read their own secrets and settings in, so a container is pointed at a
    /// token the same way it is pointed at a database.
    /// </summary>
    public static string? ResolveToken(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return Environment.GetEnvironmentVariable(TokenEnvironmentVariable) is { Length: > 0 } fromEnvironment
            ? fromEnvironment
            : configuration[TokenConfigurationKey] is { Length: > 0 } fromConfig
                ? fromConfig
                : null;
    }
}
