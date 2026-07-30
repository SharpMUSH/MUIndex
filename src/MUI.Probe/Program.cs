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

if (result.MsspBytesRejected is { } rejected)
{
    Console.WriteLine($"mssp dropped  {rejected} bytes");
}

Console.WriteLine($"offered       {(result.OfferedOptions.Count == 0 ? "(none observed)" : string.Join(", ", result.OfferedOptions.Order()))}");

if (result.Failure is { } failure)
{
    Console.WriteLine($"failure       {failure.Cause} — {failure.Detail}");
}

if (result.Mssp.Count > 0)
{
    Console.WriteLine("mssp fields");
    foreach (var (key, value) in result.Mssp.OrderBy(kv => kv.Key))
    {
        Console.WriteLine($"  {key,-14} {value}");
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
