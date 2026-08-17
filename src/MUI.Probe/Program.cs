using MUI.Crawl;

// One probe, printed. This is the crawler's smallest useful shape: point it at a host and see
// exactly what a stranger's server tells an anonymous connection. It never authenticates —
// TelnetProbe.PermittedCommands is the whole of what goes on the wire.
var host = args.Length > 0 ? args[0] : "mush.pennmush.org";
var port = args.Length > 1 && int.TryParse(args[1], out var p) ? p : 4201;

// §11: the same contact address the deployable announces, when the environment has one to give. A
// probe run by hand is still a connection to somebody else's machine, and docs/deploy.md sends an
// operator here to dial twenty of them before choosing a host.
var options = Environment.GetEnvironmentVariable("MUI_CRAWL_INFO_URL") is { Length: > 0 } contact
    ? new ProbeOptions { InfoUrl = contact }
    : new ProbeOptions();

options.Validate();

var result = await new TelnetProbe(options).ProbeAsync(new ProbeTarget(host, port));

Console.WriteLine($"target        {result.Host}:{result.Port}");
Console.WriteLine($"outcome       {result.Outcome}");
Console.WriteLine($"elapsed       {result.Elapsed.TotalSeconds:F1}s");
Console.WriteLine($"mssp          {result.MsspOutcome} via {result.MsspTransport}");
Console.WriteLine($"who           {result.Who.Confidence}" + (result.Who.HasCount
    ? $" \u2192 {result.Who.Count} players"
    : result.Who.Attempted ? " \u2014 asked, unreadable" : " \u2014 never asked"));

if (result.MsspBytesRejected is { } rejected)
{
    Console.WriteLine($"mssp dropped  {rejected} bytes");
}

Console.WriteLine($"negotiated    {(result.OfferedOptions.Count == 0 ? "(none observed)" : string.Join(", ", result.OfferedOptions.Order()))}");

// How much screen we came away with, which is the first thing to look at on a game that gates its
// connect screen behind a colour question: 23 characters means we recorded the question, and a few
// thousand means BannerGate saw it for what it was and the terminator answered it.
Console.WriteLine($"banner        {result.Banner?.Length ?? 0} chars"
    + (BannerGate.IsAnsweredByReturn(result.Banner) ? " — still a gate, unanswered" : string.Empty));

if (result.BannerPlayerCount is { } fromBanner)
{
    Console.WriteLine($"banner count  {fromBanner} (stated in the connect screen)");
}

Console.WriteLine($"charset       {result.Negotiation.Charset ?? "(unset)"}{(result.Negotiation.CharsetNegotiated ? " (negotiated)" : " (default)")}");

// §6.2 — the pre-login command replies, and what this is prepared to conclude from them.
//
// Both halves are printed, and the reading is printed even when it is nothing, because the gap
// between them is the whole diagnostic: a game whose INFO plainly names its engine on line eleven
// and whose reading is empty is a parser to improve, and a reading that names a codebase the text
// does not is the bug this line was added to catch. It caught one on darcness.net:4201.
Console.WriteLine($"codebase      {LoginCommandReading.MeaningfulCodebase(result.Info, result.Version)
    ?? "— nothing the reader would stand behind"}");

// The other half of the same question, and printed for the same reason. Most of the hobby writes no
// labelled INFO line and does carry a licence credit on its connect screen, so on the majority of
// games this is the line that decides whether the page names an engine — and it is the one that has
// to be checked against the screen above it after any widening in CodebaseCredits.
Console.WriteLine($"credits       {CodebaseCredits.Named(result.Banner)
    ?? "— no licence notice this reader would stand behind"}");

if (LoginCommandReading.ConnectedPlayers(result.Info) is { } fromInfo)
{
    Console.WriteLine($"info count    {fromInfo} (declared in the INFO block)");
}

Reply("info", result.Info);
Reply("version", result.Version);

static void Reply(string label, string? text)
{
    var lines = (text ?? string.Empty)
        .Split('\n')
        .Select(line => line.TrimEnd())
        .Where(line => line.Trim().Length > 0)
        .ToList();

    if (lines.Count == 0)
    {
        Console.WriteLine($"{label,-13} (no reply)");
        return;
    }

    Console.WriteLine($"{label,-13} {lines.Count} lines");

    foreach (var line in lines.Take(24))
    {
        Console.WriteLine($"  | {line}");
    }

    if (lines.Count > 24)
    {
        Console.WriteLine($"  … {lines.Count - 24} more");
    }
}

if (result.Negotiation.EnvironmentRequested.Count > 0)
{
    Console.WriteLine($"mnes asked    {string.Join(", ", result.Negotiation.EnvironmentRequested)}");
}

if (result.Negotiation.GmcpPackages.Count > 0)
{
    Console.WriteLine($"gmcp          {string.Join(", ", result.Negotiation.GmcpPackages)}");
}

if (result.Failure is { } failure)
{
    Console.WriteLine($"failure       {failure.Cause} — {failure.Detail}");
}

if (result.Mssp.Count > 0)
{
    // Wire order, not alphabetical: MSSP has no sorted form, and for a variable a game repeats —
    // REFERRAL above all — the sequence is the game listing them rather than naming a set.
    Console.WriteLine($"mssp fields   {result.Mssp.Count}");
    foreach (var (key, values) in result.Mssp)
    {
        Console.WriteLine(values.Count == 1
            ? $"  {key,-16} {values[0]}"
            : $"  {key,-16} {values.Count} values: {string.Join(" | ", values)}");
    }
}

var banner = result.Banner?.TrimEnd();
if (!string.IsNullOrWhiteSpace(banner))
{
    var lines = banner.Split('\n');
    Console.WriteLine($"banner        {lines.Length} lines");
    foreach (var line in lines.Take(24))
    {
        Console.WriteLine($"  | {line.TrimEnd()}");
    }

    if (lines.Length > 24)
    {
        Console.WriteLine($"  … {lines.Length - 24} more");
    }
}
