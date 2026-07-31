namespace MUI.Import;

/// <summary>
/// One line of the about page's credit list (spec §7.6: "the about page names every source we
/// ingested", and every imported value carries its originating site).
/// </summary>
public sealed record SourceAttribution(
    string SourceName,
    Uri Uri,
    ImportTier Tier,
    FetchRoute Route,
    string? Note)
{
    /// <summary>
    /// What this source is entitled to do, in the words §7.6 uses. Rendered beside the credit so the
    /// distinction is visible to a reader rather than only to the schema.
    /// </summary>
    public string Entitlement => Tier is ImportTier.Measured
        ? "measured — seeds discovery, and its readings count toward archive grace at half weight"
        : "asserted — seeds discovery and addresses only; no history, no presence, no grace";
}

/// <summary>
/// The directories this build knows how to read.
/// </summary>
/// <remarks>
/// <b>The attribution list is derived from the registry rather than hand-written beside it.</b> A
/// credit list maintained separately from the code goes stale in one direction only — the direction
/// where a source is being read and is not being credited — and §7.6's attribution obligation is the
/// one part of the etiquette a reader can check.
/// </remarks>
public sealed class SourceRegistry(IEnumerable<IDirectorySource> sources)
{
    public IReadOnlyList<IDirectorySource> Sources { get; } =
        (sources ?? throw new ArgumentNullException(nameof(sources))).ToList();

    /// <summary>Every source, credited, whether or not it is currently permitted to fetch.</summary>
    public IReadOnlyList<SourceAttribution> Attributions() =>
        Sources
            .Select(source => new SourceAttribution(
                source.SourceName,
                source.Etiquette.AttributionUri,
                source.Tier,
                EtiquettePlanner.Decide(source.Etiquette).Route,
                source.Etiquette.AttributionNote))
            .OrderBy(attribution => attribution.SourceName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The sources a run would actually read, and the reason for each one it would not.</summary>
    public IReadOnlyList<(IDirectorySource Source, FetchDecision Decision)> Plan() =>
        Sources
            .Select(source => (source, EtiquettePlanner.Decide(source.Etiquette)))
            .ToList();
}
