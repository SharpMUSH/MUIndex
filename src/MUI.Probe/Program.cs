using MUI.Crawl;

// One probe, printed: point it at a host and see exactly what an anonymous connection gets told.
// Never authenticates — TelnetProbe.PermittedCommands is the whole of what goes on the wire.
var host = args.Length > 0 ? args[0] : "mush.pennmush.org";
var port = args.Length > 1 && int.TryParse(args[1], out var p) ? p : 4201;

// The third argument is the CHARSET override a staff row would carry, so the encoding a game needs
// can be tried against the real server before anybody writes it down. Names are .NET's:
// gbk, big5, euc-kr, iso-8859-1. Anything this runtime does not know is ignored, not fatal.
var charset = args.Length > 2 ? args[2] : null;

// §11: the same contact address the deployable announces, so a probe run by hand still identifies us.
var options = Environment.GetEnvironmentVariable("MUI_CRAWL_INFO_URL") is { Length: > 0 } contact
    ? new ProbeOptions { InfoUrl = contact }
    : new ProbeOptions();

options.Validate();

var result = await new TelnetProbe(options).ProbeAsync(new ProbeTarget(host, port) { Charset = charset });

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

Console.WriteLine($"banner        {result.Banner?.Length ?? 0} chars"
    + (LoginPromptGate.Classify(result.Banner) is not null ? " — still a gate, unanswered" : string.Empty));

if (result.BannerPlayerCount is { } fromBanner)
{
    Console.WriteLine($"banner count  {fromBanner} (stated in the connect screen)");
}

Console.WriteLine($"charset       {result.Negotiation.Charset ?? "(unset)"}{(result.Negotiation.CharsetNegotiated ? " (negotiated)" : " (default)")}");

// What was negotiated and what the bytes were read as are two different facts, and the gap between
// them is the whole point of the override — print both rather than letting one stand for the other.
Console.WriteLine($"read as       {result.ReadAs} ({result.CharsetSource})");

foreach (var (variable, values) in result.Mssp)
{
    Console.WriteLine($"  mssp        {variable} = {string.Join(" | ", values)}");
}

// §6.2 — printed even when empty, since the gap between the raw reply and the reading is the
// diagnostic: an INFO that plainly names an engine with an empty reading means the parser needs work.
Console.WriteLine($"codebase      {LoginCommandReading.MeaningfulCodebase(result.Info, result.Version)
    ?? "— nothing the reader would stand behind"}");

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

if (result.Negotiation.MsdpMessages.Count > 0)
{
    Console.WriteLine($"msdp          {string.Join(" | ", result.Negotiation.MsdpMessages)}");
}

if (result.Failure is { } failure)
{
    Console.WriteLine($"failure       {failure.Cause} — {failure.Detail}");
}

if (result.Mssp.Count > 0)
{
    // Wire order, not alphabetical — for a repeated variable like REFERRAL the sequence is meaningful.
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
