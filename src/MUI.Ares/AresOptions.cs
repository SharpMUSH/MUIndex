namespace MUI.Ares;

/// <summary>Where AresCentral is, and what it expects us to present.</summary>
public sealed record AresOptions
{
    public Uri BaseAddress { get; init; } = new("https://arescentral.aresmush.com/");

    /// <summary>The path the games list is at, relative to <see cref="BaseAddress"/>.</summary>
    public string GamesPath { get; init; } = "api/games";

    /// <summary>The client id AresCentral issued this deployment.</summary>
    public string ClientId { get; init; } = "";

    /// <summary>The key that goes with it.</summary>
    public string ApiKey { get; init; } = "";

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The most body we will read.
    /// </summary>
    /// <remarks>
    /// Read to a ceiling rather than trusting <c>Content-Length</c>, the rule <c>IconFetcher</c>
    /// already follows. A few hundred games with Markdown blurbs sits well inside this.
    /// </remarks>
    public long MaxResponseBytes { get; init; } = 8 * 1024 * 1024;

    /// <summary>
    /// Refuses a half-configured deployment at startup.
    /// </summary>
    /// <remarks>
    /// AresCentral issues the id and the key as a pair, so one without the other is a typo in a
    /// compose file rather than a state worth supporting.
    /// </remarks>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                "AresCentral needs both a client id and an API key; it issues them as a pair.");
        }

        if (Timeout <= TimeSpan.Zero || MaxResponseBytes <= 0)
        {
            throw new InvalidOperationException(
                "AresCentral needs a positive timeout and response ceiling.");
        }
    }
}
