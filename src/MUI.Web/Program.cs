using MUI.Web;
using MUI.Web.Data;
using MUI.Web.Mcp;

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

    // The MCP endpoint is mapped whenever there is a database (SiteComposition), whether or not an
    // operator has set the token that gates it — fail-closed, not absent, so the route's own 401s are
    // the loud failure rather than a silent 404 nobody can tell apart from a typo in the path.
    if (MuiMcp.ResolveToken(app.Configuration) is null)
    {
        startupLog.LogWarning(
            "{Route} is mapped but {Env} is not set: every request to it will fail authentication "
            + "until it is.",
            MuiMcp.Route,
            MuiMcp.TokenEnvironmentVariable);
    }
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
