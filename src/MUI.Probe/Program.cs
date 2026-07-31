using MUI.Crawl;

// One probe, printed. This is the crawler's smallest useful shape: point it at a host and see
// exactly what a stranger's server tells an anonymous connection. It never authenticates —
// TelnetProbe.PermittedCommands is the whole of what goes on the wire.
var host = args.Length > 0 ? args[0] : "mush.pennmush.org";
var port = args.Length > 1 && int.TryParse(args[1], out var p) ? p : 4201;

var result = await new TelnetProbe().ProbeAsync(new ProbeTarget(host, port));

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
if (result.BannerPlayerCount is { } fromBanner)
{
    Console.WriteLine($"banner count  {fromBanner} (stated in the connect screen)");
}

Console.WriteLine($"charset       {result.Negotiation.Charset ?? "(unset)"}{(result.Negotiation.CharsetNegotiated ? " (negotiated)" : " (default)")}");

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
