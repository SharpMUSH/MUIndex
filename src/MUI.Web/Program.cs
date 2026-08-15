using MUI.Catalog;
using MUI.Crawler;
using MUI.Web.Accounts;
using MUI.Web.Api;
using MUI.Web.Components;
using MUI.Web.Data;
using MUI.Web.Fixtures;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents();

// The read API (spec §10) reads through the same IGameQueries the pages do, so the two surfaces
// cannot disagree about a fact. What it adds of its own — the dataset licence, the slug aliases and
// the attribution list — is configuration, because none of it is a measurement.
builder.Services.AddMuiApi(builder.Configuration);

// The site reads through IGameQueries and nothing else, and it prefers a real database to a
// fixture. Point MUI_POSTGRES (or ConnectionStrings:MUIndex) at the catalogue the crawler writes and
// every page renders measurements; without one the site still starts, but it says loudly and on
// every page that what it is showing was not measured.
//
// The fixture is not a fallback so much as a confession. A directory whose whole claim is that its
// data is measured must never quietly present invented data as though it were real — so the demo
// path is opt-in by absence, announced in the log, and marked in the page itself.
var connectionString = PostgresData.ResolveConnectionString(builder.Configuration);

if (connectionString is not null)
{
    builder.Services.AddPostgresCatalogue(connectionString);
    builder.Services.AddMuiCrawler(connectionString, configure =>
    {
        // MUI.Web already applies migrations during startup; the hosted crawler should not repeat
        // them when it takes the lease.
        configure.ApplyMigrations = false;
    });
}
else
{
    builder.Services.AddSingleton<FixtureGameQueries>();
    builder.Services.AddSingleton<IGameQueries>(s => s.GetRequiredService<FixtureGameQueries>());
    builder.Services.AddSingleton<IAvailabilityHistory>(s => s.GetRequiredService<FixtureGameQueries>());
}

builder.Services.AddSingleton(new CatalogueSource(connectionString is not null));

// Claiming needs a database: an account, a passkey and a claim are all rows (spec §8). Against the
// demo fixture the sign-in and claim surfaces are simply absent rather than present and broken —
// half a claim flow over invented games would be a worse answer than none.
if (connectionString is not null)
{
    builder.Services.AddMuiAccounts(builder.Configuration);
}

// Ages are relative to a clock, and a clock is a dependency like any other — the plain surface and
// the rendered page must not each reach for DateTimeOffset.UtcNow and disagree by a tick.
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

var startupLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MUIndex");

if (connectionString is not null)
{
    await PostgresData.ApplyMigrationsAsync(app.Services, startupLog);
    startupLog.LogInformation("Reading the catalogue from PostgreSQL.");
}
else
{
    startupLog.LogWarning(
        "No {Env} and no {Key}: serving DEMO data. Nothing on this site was measured. "
        + "Point it at the database the crawler writes to serve real measurements.",
        PostgresData.EnvironmentVariable, PostgresData.ConfigurationKey);
}

app.UseStaticFiles();

// Razor Components register anti-forgery metadata on every endpoint, so the middleware has to be
// present even though this site has no POST form yet. The facet panel is a GET form deliberately —
// a filter is a bookmarkable question, not a state change — so nothing here is token-protected.
app.UseAntiforgery();

if (connectionString is not null)
{
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapMuiAccounts();
}

app.MapRazorComponents<App>();
app.MapMuiApi();

app.Run();

/// <summary>Exposed so <c>MUI.Web.Tests</c> can host the app in-process.</summary>
public partial class Program;
