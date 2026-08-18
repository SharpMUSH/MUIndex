using MUI.Web;
using MUI.Web.Data;

var builder = WebApplication.CreateBuilder(args);

// Point MUI_POSTGRES (or ConnectionStrings:MUIndex) at the catalogue the crawler writes; without one
// the site still starts, but says loudly and on every page that what it shows was not measured.
var connectionString = PostgresData.ResolveConnectionString(builder.Configuration);

// The graph itself lives in SiteComposition, so CompositionTests resolves THE SAME registrations.
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
