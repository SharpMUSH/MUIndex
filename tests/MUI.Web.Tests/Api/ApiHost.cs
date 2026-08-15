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
/// <para>
/// A real server and not a hand-called handler, because half of what this task promises is HTTP
/// rather than JSON: a 304 that carries no body, a 301 that survives forever, a dump that arrives
/// chunked because it was never assembled. None of those can be asserted against a delegate.
/// </para>
/// <para>
/// It builds its own host rather than booting <c>Program</c> so a test can vary configuration —
/// slug aliases, the dataset licence — without an environment variable, and so no test project
/// needs a package the solution does not already carry.
/// </para>
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
    /// Replaces the fixture's read side — which is how a test asserts what an endpoint <em>does
    /// not</em> ask for, by handing it a catalogue that refuses the question.
    /// </param>
    /// <param name="services">
    /// Anything a database would have registered — the former-slug store, above all. Applied before
    /// <c>AddMuiApi</c>, because that is the order a real host composes in: the catalogue is chosen
    /// first and the API asks what it found.
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

        // The fixture measured nothing, so §10's series is empty here unless a test supplies one.
        // Registered before the caller's own hook so that a test's registration is the later one and
        // therefore the one that resolves.
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

        // Redirects are not followed: a permanent redirect from a former slug is the assertion, and
        // a client that quietly follows it would report a 200 and prove nothing.
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
