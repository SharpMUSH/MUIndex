using MUI.Catalog;
using MUI.Web.Components;
using MUI.Web.Data;
using MUI.Web.Fixtures;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents();

// The site reads through IGameQueries and nothing else. Today a fixture answers it; Postgres will
// answer it later without a page changing. That seam is what lets the web tier and the crawler be
// built at the same time instead of one waiting on the other.
builder.Services.AddSingleton<FixtureGameQueries>();
builder.Services.AddSingleton<IGameQueries>(s => s.GetRequiredService<FixtureGameQueries>());

// IAvailabilityHistory is the one thing the pages need that IGameQueries does not yet expose: the
// availability intervals the 90-day strip and the archive are both arithmetic over. It belongs on
// IGameQueries and lives beside it only because that interface is owned elsewhere — see the
// interface's own remarks.
builder.Services.AddSingleton<IAvailabilityHistory>(s => s.GetRequiredService<FixtureGameQueries>());

// Ages are relative to a clock, and a clock is a dependency like any other — the plain surface and
// the rendered page must not each reach for DateTimeOffset.UtcNow and disagree by a tick.
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

app.UseStaticFiles();

// Razor Components register anti-forgery metadata on every endpoint, so the middleware has to be
// present even though this site has no POST form yet. The facet panel is a GET form deliberately —
// a filter is a bookmarkable question, not a state change — so nothing here is token-protected.
app.UseAntiforgery();

app.MapRazorComponents<App>();

app.Run();

/// <summary>Exposed so <c>MUI.Web.Tests</c> can host the app in-process.</summary>
public partial class Program;
