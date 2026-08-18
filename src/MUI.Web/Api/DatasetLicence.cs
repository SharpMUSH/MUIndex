using Microsoft.Extensions.Options;

namespace MUI.Web.Api;

/// <summary>
/// The terms the published data goes out under, and who is credited in it.
/// </summary>
/// <remarks>
/// Configuration, not a literal: the code's MIT licence and the dataset's are separate decisions, and
/// the dataset licence is still open (spec §15.2). The default is attribution-only — the weakest
/// thing compatible with §10's commitment to republish rather than silo.
/// </remarks>
public sealed class DatasetLicenceOptions
{
    public const string Section = "Dataset";

    public string LicenceId { get; set; } = "CC-BY-4.0";

    public string LicenceName { get; set; } = "Creative Commons Attribution 4.0 International";

    public string? LicenceUrl { get; set; } = "https://creativecommons.org/licenses/by/4.0/";

    public string Attribution { get; set; } = "MUIndex (https://github.com/SharpMUSH/MUIndex)";

    public string Notice { get; set; } =
        "Measurements of publicly reachable game servers, taken by MUIndex's own probes from one "
        + "vantage point. Every value carries the source it came from and when it was last "
        + "confirmed. Player names are never recorded, and no absolute population figure is "
        + "published — per-codebase and per-protocol shares are, totals are not.";

    /// <summary>Third parties whose data was ingested (spec §7.6). Empty until something has actually been imported.</summary>
    public List<AttributionOption> Sources { get; set; } = [];

    public LicenceView View() => new(LicenceId, LicenceName, LicenceUrl, Attribution);
}

public sealed class AttributionOption
{
    public string Name { get; set; } = string.Empty;

    public string? Url { get; set; }

    /// <summary>What we took from them — a seed list, a measured import, a referral source.</summary>
    public string Role { get; set; } = "source";
}

/// <summary>
/// Who is credited in the bulk dump. A seam so an importer can register a live answer later without
/// the dump endpoint learning that an importer exists.
/// </summary>
public interface IAttributionSource
{
    IReadOnlyList<AttributionView> Sources();
}

public sealed class ConfiguredAttributionSource(IOptions<DatasetLicenceOptions> options)
    : IAttributionSource
{
    public IReadOnlyList<AttributionView> Sources() =>
        [.. options.Value.Sources.Select(s => new AttributionView(s.Name, s.Url, s.Role))];
}
