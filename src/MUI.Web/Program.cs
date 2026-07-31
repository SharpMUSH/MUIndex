using MUI.Catalog;
using MUI.Web.Components;
using MUI.Web.Fixtures;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents();

// The site reads through IGameQueries and nothing else. Today a fixture answers it; Postgres will
// answer it later without a page changing. That seam is what lets the web tier and the crawler be
// built at the same time instead of one waiting on the other.
builder.Services.AddSingleton<IGameQueries, FixtureGameQueries>();

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
