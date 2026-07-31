using Microsoft.Extensions.Options;

namespace MUI.Web.Api;

/// <summary>
/// The terms the published data goes out under, and who is credited in it.
/// </summary>
/// <remarks>
/// <para>
/// Configuration and not a literal, because the code's licence and the dataset's are two separate
/// decisions: the code is MIT and the dataset licence is still an open question (spec §15.2). A
/// constant in the source would settle by accident a question the design deliberately left open,
/// and would then be wrong in every deployment that answered it differently.
/// </para>
/// <para>
/// The default is attribution-only, which is the weakest thing compatible with §10's actual
/// commitment: republish rather than silo. A rival directory taking this dataset wholesale is a
/// success condition here, so the default must not stand in its way — it only asks to be named.
/// </para>
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

    /// <summary>
    /// Third parties whose data was ingested (spec §7.6). Empty until something has been imported:
    /// crediting a source we have not read yet would be the one kind of unmeasured claim this site
    /// exists not to make.
    /// </summary>
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
