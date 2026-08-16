using MUI.Crawler;

namespace MUI.Web.Data;

/// <summary>
/// The three things a deployment owns about the in-process crawler: whether it runs here, the
/// addresses it knows before it has followed anything, and the address it gives out when it dials.
/// </summary>
/// <remarks>
/// <para>
/// Environment first and a configuration key behind it, exactly as <see cref="PostgresData"/> reads
/// the connection string, so a container is pointed at a database and given a seed list the same way
/// and neither needs a config file shipped beside it.
/// </para>
/// <para>
/// <b>Nothing here can exempt an address from the resolved-address gate.</b> §7.2's exemption is a
/// claim a human makes about one address they chose on purpose, and an environment variable copied
/// between deployments is not that human. Pointing the crawler at a private address stays
/// <c>mui-crawl --seed-exempt</c>, where somebody types it and means it.
/// </para>
/// <para>
/// Seeds matter on day one and are then a convenience: §7.1's effective seed set is every game ever
/// found, and seeding is idempotent, so a restart with the same list does not drag anything forward
/// or repeat a burst of traffic at somebody else's server.
/// </para>
/// </remarks>
public static class CrawlerSettings
{
    /// <summary>Whitespace- or comma-separated <c>host:port</c> addresses.</summary>
    public const string SeedsEnvironmentVariable = "MUI_CRAWL_SEEDS";

    public const string SeedsConfigurationKey = "Crawler:Seeds";

    /// <summary><c>false</c> makes this replica a pure web tier.</summary>
    public const string EnabledEnvironmentVariable = "MUI_CRAWL_ENABLED";

    public const string EnabledConfigurationKey = "Crawler:Enabled";

    /// <summary>
    /// Where an admin who has just been dialled can read what we do and ask us to stop (spec §11).
    /// </summary>
    /// <remarks>
    /// It is a setting rather than a constant because the address belongs to the deployment doing the
    /// dialling: the compiled default is a placeholder that answers nobody, and a fork inheriting our
    /// contact page would point the servers <em>it</em> probed at us.
    /// </remarks>
    public const string InfoUrlEnvironmentVariable = "MUI_CRAWL_INFO_URL";

    public const string InfoUrlConfigurationKey = "Crawler:Probe:InfoUrl";

    /// <summary>
    /// <c>true</c> runs the Intermud-3 pass, which needs the <c>i3</c> sidecar to be running.
    /// </summary>
    /// <remarks>
    /// Off unless said out loud, because turning it on connects a container to a public network and
    /// registers a name there permanently. That is not a thing to acquire by upgrading an image.
    /// </remarks>
    public const string I3EnabledEnvironmentVariable = "MUI_I3_ENABLED";

    public const string I3EnabledConfigurationKey = "Crawler:I3:Enabled";

    /// <summary>Where the sidecar's newline-delimited JSON-RPC surface is, as <c>host:port</c>.</summary>
    public const string I3GatewayEnvironmentVariable = "MUI_I3_GATEWAY";

    public const string I3GatewayConfigurationKey = "Crawler:I3:Gateway";

    /// <summary>The key the sidecar expects. No default: its shipped configuration has example keys.</summary>
    public const string I3ApiKeyEnvironmentVariable = "MUI_I3_API_KEY";

    public const string I3ApiKeyConfigurationKey = "Crawler:I3:ApiKey";

    private static readonly char[] Separators = [',', ' ', '\t', '\r', '\n'];

    /// <summary>Applies what the environment said, and throws rather than shrugging at a typo.</summary>
    /// <remarks>
    /// A misspelled value is an error for the reason <c>mui-crawl</c> refuses an unrecognised switch:
    /// <c>MUI_CRAWL_ENABLED=no</c> read as "not the word false, so leave it on" is a deployment that
    /// believes it turned the crawler off and is still dialling.
    /// </remarks>
    public static CrawlerOptionsBuilder Apply(this CrawlerOptionsBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        if (Read(configuration, EnabledEnvironmentVariable, EnabledConfigurationKey) is { } enabled)
        {
            builder.Enabled = bool.TryParse(enabled, out var value)
                ? value
                : throw new ArgumentException(
                    $"{EnabledEnvironmentVariable} is '{enabled}', which is neither true nor false.");
        }

        if (Read(configuration, InfoUrlEnvironmentVariable, InfoUrlConfigurationKey) is { } contact)
        {
            builder.Probe = builder.Probe with { InfoUrl = contact };

            // Here rather than only at CrawlerOptions.Validate, so a typo fails beside the setting it
            // came from instead of after the rest of the graph has been assembled around it.
            builder.Probe.Validate();
        }

        ApplyI3(builder, configuration);

        foreach (var address in Seeds(configuration))
        {
            builder.Seed(address.Host, address.Port);
        }

        return builder;
    }

    /// <summary>
    /// Applies the Intermud-3 settings, and refuses a half-configured pass rather than starting one.
    /// </summary>
    /// <remarks>
    /// The address is parsed here rather than left as a string so that <c>MUI_I3_GATEWAY=i3:notaport</c>
    /// fails beside the setting that caused it. <see cref="I3ServiceOptions.Validate"/> catches the
    /// missing key separately, at startup, for the same reason: the alternative is an authentication
    /// failure on a five-minute loop that reads like the sidecar is broken.
    /// </remarks>
    private static void ApplyI3(CrawlerOptionsBuilder builder, IConfiguration configuration)
    {
        if (Read(configuration, I3EnabledEnvironmentVariable, I3EnabledConfigurationKey) is { } enabled)
        {
            builder.I3 = builder.I3 with
            {
                Enabled = bool.TryParse(enabled, out var value)
                    ? value
                    : throw new ArgumentException(
                        $"{I3EnabledEnvironmentVariable} is '{enabled}', which is neither true nor false."),
            };
        }

        if (Read(configuration, I3GatewayEnvironmentVariable, I3GatewayConfigurationKey) is { } address)
        {
            var separator = address.LastIndexOf(':');
            if (separator <= 0 || !int.TryParse(address[(separator + 1)..], out var port))
            {
                throw new ArgumentException(
                    $"{I3GatewayEnvironmentVariable} is '{address}', which is not host:port.");
            }

            builder.I3 = builder.I3 with
            {
                Gateway = builder.I3.Gateway with { Host = address[..separator], Port = port },
            };
        }

        if (Read(configuration, I3ApiKeyEnvironmentVariable, I3ApiKeyConfigurationKey) is { } key)
        {
            builder.I3 = builder.I3 with { Gateway = builder.I3.Gateway with { ApiKey = key } };
        }

        builder.I3.Validate();
    }

    /// <summary>The configured seeds, parsed, in the order they were written.</summary>
    public static IReadOnlyList<CrawlSeed> Seeds(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return Read(configuration, SeedsEnvironmentVariable, SeedsConfigurationKey) is { } list
            ? [.. list.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(address => CrawlSeed.Parse(address))]
            : [];
    }

    private static string? Read(IConfiguration configuration, string environmentVariable, string key) =>
        Environment.GetEnvironmentVariable(environmentVariable) is { Length: > 0 } fromEnvironment
            ? fromEnvironment
            : configuration[key] is { Length: > 0 } fromConfig
                ? fromConfig
                : null;
}
