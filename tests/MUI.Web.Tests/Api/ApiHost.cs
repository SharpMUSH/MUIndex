using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using MUI.Catalog;
using MUI.Web.Api;
using MUI.Web.Data;
using MUI.Web.Fixtures;

namespace MUI.Web.Tests.Api;

/// <summary>
/// The read API, running for real on a loopback port, answered by the same fixture the site uses.
/// </summary>
/// <remarks>
/// A real server, not a hand-called handler: HTTP-level facts like a bodyless 304, a permanent 301,
/// or a chunked dump can't be asserted against a delegate. Builds its own host rather than booting
/// <c>Program</c> so a test can vary configuration without an environment variable.
/// </remarks>
public sealed class ApiHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private ApiHost(WebApplication app, HttpClient client)
    {
        _app = app;
        Client = client;
    }

    public HttpClient Client { get; }

    /// <summary>The instant every age in a response is measured from. Fixed, so ages are assertable.</summary>
    public static DateTimeOffset Now => FixtureGameQueries.Now;

    /// <summary>A host with a catalogue's services and no configuration of its own.</summary>
    public static Task<ApiHost> StartAsync(Action<IServiceCollection> services) =>
        StartAsync(null, null, services);

    /// <summary>The API on a loopback port.</summary>
    /// <param name="settings">Configuration this host is to read, as a deployment would supply it.</param>
    /// <param name="queries">
    /// Replaces the fixture's read side, so a test can hand it a catalogue that refuses a question.
    /// </param>
    /// <param name="services">
    /// Anything a database would have registered — the former-slug store, above all. Applied before
    /// <c>AddMuiApi</c>, matching how a real host composes.
    /// </param>
    public static async Task<ApiHost> StartAsync(
        Dictionary<string, string?>? settings = null,
        IGameQueries? queries = null,
        Action<IServiceCollection>? services = null)
    {
        var builder = WebApplication.CreateSlimBuilder();

        builder.Logging.ClearProviders();
        if (settings is { Count: > 0 })
        {
            builder.Configuration.AddInMemoryCollection(settings);
        }

        builder.Services.AddSingleton<FixtureGameQueries>();
        builder.Services.AddSingleton<IGameQueries>(
            s => queries ?? s.GetRequiredService<FixtureGameQueries>());
        builder.Services.AddSingleton<IAvailabilityHistory>(
            s => s.GetRequiredService<FixtureGameQueries>());

        // Registered before the caller's hook so a test's own registration is the one that resolves.
        builder.Services.AddSingleton<IPresenceSeries, FixturePresenceSeries>();
        builder.Services.AddSingleton<TimeProvider>(new FixedClock(Now));
        services?.Invoke(builder.Services);
        builder.Services.AddMuiApi(builder.Configuration);

        // Port zero: the suite runs in parallel and a fixed port would make two tests fight.
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();
        app.MapMuiApi();
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        // Not followed: a redirect from a former slug is the assertion itself.
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(address),
        };

        return new ApiHost(app, client);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
