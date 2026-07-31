namespace MUI.Import;

/// <summary>
/// What one import run did.
/// </summary>
/// <remarks>
/// <see cref="Rejected"/> counts rows a source offered that its tier is not entitled to write. Spec
/// §7.6's asserted tier offering history is not an error to swallow, it is a number to print — a
/// silent refusal reads exactly like a source that had nothing to offer, and only one of those is
/// worth an email.
/// </remarks>
public sealed record ImportReport(
    string Source,
    ImportTier Tier,
    int GamesSeen,
    int TargetsAdded,
    int EndpointsWritten,
    int FieldsWritten,
    int PresenceRows,
    int AvailabilityRows,
    int Rejected,
    int Matched,
    IReadOnlyList<string> Notes)
{
    /// <summary>Games whose addresses we had never seen: seeded for the crawler, and not listed.</summary>
    public int Unmatched => GamesSeen - Matched;

    public override string ToString() =>
        $"{Source} [{(Tier is ImportTier.Measured ? "imported_measured" : "imported_asserted")}]: "
        + $"{GamesSeen} listed, {Matched} matched an address we already know, {Unmatched} seeded only; "
        + $"{TargetsAdded} crawl targets, {EndpointsWritten} endpoints, {FieldsWritten} fields, "
        + $"{PresenceRows} presence, {AvailabilityRows} reachable spans, {Rejected} refused.";
}
