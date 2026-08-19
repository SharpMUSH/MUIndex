using MUI.Crawler;

namespace MUI.Web.Data;

/// <summary>
/// The three things a deployment owns about the in-process crawler: whether it runs here, the
/// addresses it knows before it has followed anything, and the address it gives out when it dials.
/// </summary>
/// <remarks>
/// Environment first, configuration key behind it, exactly as <see cref="PostgresData"/> reads the
/// connection string. Nothing here can exempt an address from §7.2's resolved-address gate — that
/// exemption is a human's deliberate choice, made with <c>mui-crawl --seed-exempt</c>, never an
/// environment variable copied between deployments.
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
    /// A setting rather than a constant, since the address belongs to the deployment doing the
    /// dialling — a fork inheriting our contact page would point the servers it probed at us.
    /// </remarks>
    public const string InfoUrlEnvironmentVariable = "MUI_CRAWL_INFO_URL";

    public const string InfoUrlConfigurationKey = "Crawler:Probe:InfoUrl";

    /// <summary>
    /// <c>true</c> runs the Intermud-3 pass, which needs the <c>i3</c> sidecar to be running.
    /// </summary>
    /// <remarks>
    /// Off unless said out loud: turning it on connects a container to a public network and registers
    /// a name there permanently.
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
    /// A misspelled value throws rather than being read as "not false, so leave it on" —
    /// <c>MUI_CRAWL_ENABLED=no</c> should not leave a deployment dialling while it believes it's off.
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
    /// Parsed here rather than left as a string, so <c>MUI_I3_GATEWAY=i3:notaport</c> fails beside the
    /// setting that caused it. <see cref="I3ServiceOptions.Validate"/> catches a missing key the same
    /// way, rather than as an authentication failure on a loop that reads like the sidecar is broken.
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
