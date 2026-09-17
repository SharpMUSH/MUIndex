using MUI.Crawl;

// One probe, printed: point it at a host and see exactly what an anonymous connection gets told.
// Never authenticates — TelnetProbe.PermittedCommands is the whole of what goes on the wire.

// Whether to probe this address as one the catalogue already lists, which is the crawl loop's
// ordinary case and decides whether a count stated on the connect screen is allowed to stand in for
// a pre-login WHO (ProbeTarget.AwaitingCorroboration). Off by default because this tool has no
// catalogue to ask, and a caller that cannot answer keeps the probe asking.
var listed = args.Contains("--listed", StringComparer.Ordinal);

// Flags are pulled out first so they can be written anywhere without displacing a positional.
var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();

var host = positional.Length > 0 ? positional[0] : "mush.pennmush.org";
var port = positional.Length > 1 && int.TryParse(positional[1], out var p) ? p : 4201;

// The third argument is the CHARSET override a staff row would carry, so the encoding a game needs
// can be tried against the real server before anybody writes it down. Names are .NET's:
// gbk, big5, euc-kr, iso-8859-1. Anything this runtime does not know is ignored, not fatal.
var charset = positional.Length > 2 ? positional[2] : null;

// The operator's CHARSET-MSSP, for a game whose report is not in its screen's encoding.
var msspCharset = positional.Length > 3 ? positional[3] : null;

// §11: the same contact address the deployable announces, so a probe run by hand still identifies us.
var options = Environment.GetEnvironmentVariable("MUI_CRAWL_INFO_URL") is { Length: > 0 } contact
    ? new ProbeOptions { InfoUrl = contact }
    : new ProbeOptions();

options.Validate();

var result = await new TelnetProbe(options).ProbeAsync(new ProbeTarget(host, port)
{
    Charset = charset,
    MsspCharset = msspCharset,
    AwaitingCorroboration = !listed,
});

Console.WriteLine($"target        {result.Host}:{result.Port}");
Console.WriteLine($"outcome       {result.Outcome}");
// Measured rather than asked for: a plain dial that heard nothing is retried behind a handshake, so
// this says which door actually answered. `tls` means one completed, never that a certificate was
// checked — nothing here verifies one.
Console.WriteLine($"transport     {result.Transport.ToString().ToLowerInvariant()}");
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

// Whether the server marks where its prompts end — EOR, or the bare IAC GA that a default NVT uses.
// Worth printing beside the options because GA is *not* an option: a server can mark every prompt
// and negotiate nothing at all, which is the ordinary case in this hobby.
Console.WriteLine($"prompts       {(result.Negotiation.SendsPromptMarkers ? "marked (EOR or GA)" : "unmarked")}");

// A WhoMenu is not an unanswered gate: the menu *is* this game's permanent connect screen. Reported
// as detected rather than as taken, because that is all this can honestly know — the probe selects the
// option only while the socket is live, and a server that closed after printing its menu was never
// asked. Read the `who` line above for what the selection actually yielded.
Console.WriteLine($"banner        {result.Banner?.Length ?? 0} chars"
    + (LoginPromptGate.Classify(result.Banner) switch
    {
        { Category: LoginPromptCategory.WhoMenu } => " — a menu with a who's-online option",
        not null => " — still a gate, unanswered",
        _ => string.Empty,
    }));

if (result.BannerPlayerCount is { } fromBanner)
{
    // Two different facts arrive on this property: a count read off the screen, and the count that
    // bought a skipped WHO when the screen stated none (TelnetProbe.PublishedCountAsync). Printing
    // the second as "stated in the connect screen" reads an MSSP figure back as somebody's banner —
    // which is the diagnostic this tool exists to make legible, so it has to say which it has.
    Console.WriteLine($"banner count  {fromBanner} "
        + (BannerCount.Find(result.Banner) is not null
            ? "(stated in the connect screen)"
            : "(already published when WHO came up, so WHO was not asked)"));
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

if (result.Negotiation.McpPackages.Count > 0)
{
    Console.WriteLine($"mcp           {string.Join(", ", result.Negotiation.McpPackages)}");
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
