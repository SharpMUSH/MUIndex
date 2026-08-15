using MUI.Crawler;

namespace MUI.Web.Data;

/// <summary>
/// The two things a deployment owns about the in-process crawler: whether it runs here, and the
/// addresses it knows before it has followed anything.
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

        foreach (var address in Seeds(configuration))
        {
            builder.Seed(address.Host, address.Port);
        }

        return builder;
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
