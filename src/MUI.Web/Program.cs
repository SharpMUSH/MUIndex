using MUI.Web;
using MUI.Web.Data;

var builder = WebApplication.CreateBuilder(args);

// The site reads through IGameQueries and nothing else, and it prefers a real database to a fixture.
// Point MUI_POSTGRES (or ConnectionStrings:MUIndex) at the catalogue the crawler writes and every
// page renders measurements; without one the site still starts, but it says loudly and on every page
// that what it is showing was not measured.
var connectionString = PostgresData.ResolveConnectionString(builder.Configuration);

// The graph itself lives in SiteComposition, so that CompositionTests can resolve THE SAME
// registrations rather than a copy of them that agrees until somebody edits one of the two.
builder.Services.AddMuiSite(builder.Configuration, connectionString);

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

app.UseMuiSite(connectionString);

app.Run();

/// <summary>Exposed so <c>MUI.Web.Tests</c> can host the app in-process.</summary>
public partial class Program;
