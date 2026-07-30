var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// Placeholder. The site is designed but unimplemented — see docs/specs/.
app.MapGet("/", () => Results.Text("MUIndex — not yet implemented. See docs/specs/.", "text/plain"));

app.Run();

/// <summary>Exposed so <c>MUI.Web.Tests</c> can host the app in-process.</summary>
public partial class Program;
