using System.Text;
using MUI.Catalog;

namespace MUI.Web.Components;

/// <summary>
/// The plain rendering of a game page.
/// </summary>
/// <remarks>
/// <para>
/// Served at <c>?plain=1</c> and automatically to text browsers. It is not a courtesy: it is the
/// test of whether a fact is really being communicated. If something cannot survive here, its
/// graphic on the main page is decoration.
/// </para>
/// <para>
/// It renders from the same <see cref="GamePage"/> the graphical page uses, which is what bounds
/// its maintenance cost — the main page is this one with graphics added, not a second document that
/// has to be kept in step.
/// </para>
/// </remarks>
public static class PlainText
{
    public static string Render(GamePage page, DateTimeOffset now)
    {
        var b = new StringBuilder();
        var s = page.Summary;

        b.Append(s.Name.ToUpperInvariant());
        b.Append(s.State is LifecycleState.Archived ? " [archived]" : s.IsClaimed ? " [claimed]" : " [unclaimed]");
        b.AppendLine();

        foreach (var e in page.Endpoints)
        {
            b.AppendLine($"telnet {e.Host} {e.Port}{(e.TlsMeasured ? " · tls measured" : string.Empty)}");
        }

        b.AppendLine();

        // Every state spelled as a word. "Unknown" is written out rather than left blank, because a
        // blank reads as zero to a human exactly as it does to a parser.
        b.AppendLine(s.PlayersNow is { } n
            ? $"Players now: {n}"
            : "Players now: unknown (no count could be measured)");

        if (page.ReachableFraction is { } r)
        {
            b.AppendLine($"Reachable: {r:P1} of the last 90 days");
        }

        if (page.LongestOutage is { } o)
        {
            b.AppendLine($"Longest outage: {o.TotalHours:F0}h");
        }

        b.AppendLine();
        b.AppendLine($"Capabilities ({page.DisagreementCount} disagree)");
        foreach (var c in page.Capabilities)
        {
            var flag = c.Disagrees ? "  ** disagree" : string.Empty;
            b.AppendLine($"  {c.Protocol,-10} measured: {Word(c.Measured),-7} declared: {Word(c.Declared)}{flag}");
        }

        b.AppendLine();
        b.AppendLine("What the game says about itself");
        foreach (var (name, chip) in page.Declared)
        {
            var age = Relative.Format(now - chip.LastConfirmedAt);
            var how = chip.IsMeasured ? "measured" : "declared";
            b.AppendLine($"  {name,-10} {chip.Value}  ({how}, {age}{(chip.IsStale ? ", stale" : string.Empty)})");
        }

        return b.ToString();
    }

    /// <summary>Colour is never the only carrier of a state, and here there is no colour at all.</summary>
    private static string Word(CapabilityState state) => state switch
    {
        CapabilityState.Present => "yes",
        CapabilityState.Absent => "NO",
        _ => "-",
    };
}
