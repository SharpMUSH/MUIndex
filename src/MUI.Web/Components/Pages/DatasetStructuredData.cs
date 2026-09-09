using System.Text.Json.Nodes;

using MUI.Web.Api;

namespace MUI.Web.Components;

/// <summary>
/// The catalogue as a described dataset, for the indexes that look for one.
/// </summary>
/// <remarks>
/// The site already publishes what makes a dataset one — a bulk export in two formats, an OpenAPI
/// contract, a configured licence and attribution — and said so in no vocabulary a dataset index
/// reads.
/// <para>
/// Gated on the catalogue being measured, unlike <see cref="SiteStructuredData"/>: the distributions
/// named here return the fixture on a deployment with no database. No <c>temporalCoverage</c> and no
/// row count — both would be measurements this page does not hold, and the dump carries the instant
/// it was generated on every row.
/// </para>
/// </remarks>
public static class DatasetStructuredData
{
    private const string Vocabulary = "https://schema.org";

    /// <summary>The graph for the API guide, which is this dataset's documentation page.</summary>
    /// <param name="origin">This site's absolute origin — scheme, authority and path base.</param>
    /// <param name="licence">The terms the export actually goes out under, as configured.</param>
    public static string For(Uri origin, DatasetLicenceOptions licence)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(licence);

        var root = origin.ToString().TrimEnd('/');
        var page = root + ApiRoutes.Documentation;
        var publisher = new JsonObject { ["@id"] = root + SiteStructuredData.OrganizationId };

        var dataset = new JsonObject
        {
            ["@type"] = "Dataset",
            ["@id"] = $"{root}#dataset",
            ["name"] = "MUIndex: measured observations of public MU* game servers",
            ["description"] = licence.Notice,
            ["url"] = page,
            ["isAccessibleForFree"] = true,
            ["creator"] = publisher,
            ["publisher"] = publisher,
            ["license"] = (JsonNode?)licence.LicenceUrl ?? licence.LicenceId,

            ["keywords"] = new JsonArray("MUD", "MUSH", "MUCK", "MOO", "MU*", "telnet", "MSSP", "text games"),

            ["distribution"] = new JsonArray(
                Download($"{root}{ApiRoutes.Dump}", "application/json", "The whole catalogue as one JSON document"),
                Download($"{root}{ApiRoutes.DumpLines}", "application/x-ndjson", "The same, one game per line")),

            ["measurementTechnique"] = "Direct connection to each game's published address, at "
                + "intervals, from one vantage point. Values a server declared about itself (MSSP) "
                + "are recorded separately from values observed on the wire, and never merged.",
        };

        var catalogue = new JsonObject
        {
            ["@type"] = "DataCatalog",
            ["@id"] = $"{root}#catalog",
            ["name"] = "MUIndex",
            ["url"] = page,
            ["publisher"] = publisher,
            ["dataset"] = new JsonObject { ["@id"] = $"{root}#dataset" },
        };

        dataset["includedInDataCatalog"] = new JsonObject { ["@id"] = $"{root}#catalog" };

        var document = new JsonObject
        {
            ["@context"] = Vocabulary,
            ["@graph"] = new JsonArray(dataset, catalogue),
        };

        return document.ToJsonString();
    }

    private static JsonObject Download(string url, string format, string name) => new()
    {
        ["@type"] = "DataDownload",
        ["contentUrl"] = url,
        ["encodingFormat"] = format,
        ["name"] = name,
    };
}
