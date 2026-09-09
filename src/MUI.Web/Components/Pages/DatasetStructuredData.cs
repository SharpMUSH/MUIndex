using System.Text.Json.Nodes;

using MUI.Web.Api;

namespace MUI.Web.Components;

/// <summary>
/// The catalogue as a described dataset, for the indexes that look for one.
/// </summary>
/// <remarks>
/// <para>
/// This site already publishes what a dataset is: a bulk export in two formats, an OpenAPI contract
/// describing every field, a licence, and an attribution string — all of it configured rather than
/// asserted. What it had not done is say so in the vocabulary the dataset indexes read, which is why
/// a catalogue of a few thousand measured game servers was discoverable only as a website.
/// </para>
/// <para>
/// <b>Gated on the catalogue being measured, unlike <see cref="SiteStructuredData"/>.</b> The
/// distributions this node names return the fixture on a deployment with no database, and there is
/// no property here meaning "these rows are invented" — the same argument that suppresses
/// <see cref="GameStructuredData"/>.
/// </para>
/// <para>
/// <b>No <c>temporalCoverage</c> and no row count.</b> Both would be measurements, and neither is
/// one this page holds: the coverage of a catalogue that never retires a host is open-ended at both
/// ends, and a count written here would be a number nothing re-measures. The dump itself carries the
/// instant it was generated, on every row, which is where a consumer should read it.
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

            // The words somebody would actually search a dataset index for. Not keyword stuffing:
            // each is a name for the thing the rows are about, and there is no other place in this
            // vocabulary to put them.
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
