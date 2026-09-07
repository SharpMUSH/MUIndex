using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

using MUI.Catalog;

namespace MUI.Web.Api;

/// <summary>The public read contract, with response schemas derived from the actual wire serializer.</summary>
internal static class OpenApiDocument
{
    // No catalogue reads or per-request schema generation. Relative server URLs also support path bases.
    private static readonly Lazy<string> Document = new(Build);

    public static Task WriteAsync(HttpContext http) =>
        ApiResponse.WriteTextAsync(http, Document.Value, "application/json; charset=utf-8");

    private static string Build()
    {
        var schemas = new JsonObject();
        foreach (var type in new[]
        {
            typeof(ApiIndexView), typeof(GameListView), typeof(GameView), typeof(FeedsView),
            typeof(PresenceSeriesView), typeof(AvailabilitySeriesView), typeof(DumpHeaderView), typeof(ApiProblem),
        })
        {
            var schema = ApiJson.Options.GetJsonSchemaAsNode(type,
                new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true });
            RebaseReferences(schema, $"#/components/schemas/{type.Name}");
            schemas[type.Name] = schema;
        }

        schemas["GameDump"] = new JsonObject
        {
            ["allOf"] = new JsonArray(Ref(nameof(DumpHeaderView)), new JsonObject
            {
                ["type"] = "object",
                ["required"] = new JsonArray("games"),
                ["properties"] = new JsonObject
                {
                    ["games"] = new JsonObject { ["type"] = "array", ["items"] = Ref(nameof(GameView)) },
                },
            }),
        };

        var paths = new JsonObject
        {
            [ApiRoutes.Base] = Operation("apiIndex", "Discover the read API and dataset licence", nameof(ApiIndexView)),
            [ApiRoutes.Games] = Operation("listGames", "Search games with source-labelled facets", nameof(GameListView), ListingParameters(), badRequest: true),
            [ApiRoutes.Games + "/{key}"] = Operation("getGame", "Read a game with provenance and timestamps", nameof(GameView), KeyParameters(), keyed: true),
            [ApiRoutes.Games + "/{key}" + ApiRoutes.PresenceSuffix] = Operation("getPresence", "Read counted and uncountable probes over time", nameof(PresenceSeriesView), WindowParameters(true), keyed: true, badRequest: true),
            [ApiRoutes.Games + "/{key}" + ApiRoutes.AvailabilitySuffix] = Operation("getAvailability", "Read reachability spans from one vantage point", nameof(AvailabilitySeriesView), WindowParameters(false), keyed: true, badRequest: true),
            [ApiRoutes.Feeds] = Operation("getFeeds", "Read discovery and reachability changes", nameof(FeedsView)),
            [ApiRoutes.Dump] = Operation("downloadGames", "Export the catalogue, including archived and adult games, with licence and attribution", "GameDump"),
            [ApiRoutes.DumpLines] = Operation("downloadGameLines", "Export one GameView per line, without a header record; licence is in response headers and /api", null, mediaType: "application/x-ndjson"),
        };
        foreach (var (route, id) in new[]
        {
            (ApiRoutes.DiscoveredRss, "discovered"), (ApiRoutes.WentDarkRss, "wentDark"),
            (ApiRoutes.CameBackRss, "cameBack"),
        })
        {
            paths[route] = Operation(id + "Rss", "Read the " + id + " register as RSS 2.0", null, mediaType: "application/rss+xml");
        }

        return new JsonObject
        {
            ["openapi"] = "3.1.0",
            ["info"] = new JsonObject
            {
                ["title"] = "MUIndex public read API",
                ["version"] = ApiVersion.Current,
                ["description"] = "Read-only MU* catalogue. No authentication required. Null is unknown, never zero. "
                    + "Read playersNowState and provenance with every count. generatedAt is response time; "
                    + "lastConfirmedAt is observation time. Genre and language are descriptive claims, not measurements. "
                    + "Reachability is observed from one vantage point. Use immutable game IDs for stored references. "
                    + "Read /api for this deployment's dataset licence and attribution.",
            },
            ["servers"] = new JsonArray(new JsonObject { ["url"] = ".." }),
            ["externalDocs"] = new JsonObject { ["url"] = "../about/api", ["description"] = "Usage, examples and interpretation" },
            ["paths"] = paths,
            ["components"] = new JsonObject { ["schemas"] = schemas },
        }.ToJsonString(ApiJson.Options);
    }

    private static JsonObject Operation(string id, string summary, string? schema,
        JsonArray? parameters = null, bool keyed = false, bool badRequest = false,
        string mediaType = "application/json")
    {
        parameters ??= [];
        parameters.Add(Parameter("If-None-Match", "Reuse the previous ETag; matching bytes return 304.", location: "header"));
        var responses = new JsonObject
        {
            ["200"] = new JsonObject
            {
                ["description"] = summary,
                ["headers"] = new JsonObject
                {
                    ["ETag"] = Header("Validator for these exact bytes."),
                    ["X-MUIndex-Licence"] = Header("Dataset licence identifier."),
                    ["Link"] = Header("Service description, documentation and configured licence URL."),
                },
                ["content"] = new JsonObject
                {
                    [mediaType] = new JsonObject
                    {
                        ["schema"] = schema is null ? new JsonObject { ["type"] = "string" } : Ref(schema),
                    },
                },
            },
            ["304"] = new JsonObject { ["description"] = "Unchanged; no response body." },
        };
        if (keyed)
        {
            responses["301"] = new JsonObject
            {
                ["description"] = "Former slug; follow Location to the current record.",
                ["headers"] = new JsonObject { ["Location"] = Header("Current API URL.") },
            };
            responses["404"] = Problem("No such game.");
        }
        if (badRequest)
        {
            responses["400"] = Problem("Invalid filter, grain or time window.");
        }
        return new JsonObject
        {
            ["get"] = new JsonObject
            {
                ["operationId"] = id, ["summary"] = summary,
                ["parameters"] = parameters, ["responses"] = responses,
            },
        };
    }

    private static JsonArray ListingParameters()
    {
        var parameters = new JsonArray
        {
            Parameter("q", "Search game names, taglines and codebases."),
            Parameter("limit", "Page size; default 100, clamped to 1–500. Invalid integers use the default.", "integer"),
            Parameter("offset", "Skip this many matches; default 0, negative values clamp to 0.", "integer"),
            Parameter(FacetKeys.Sort, "Ordering; default players.", values: FacetTokens.Sorts),
            Parameter(FacetKeys.Band, "Activity band.", values: FacetTokens.Bands),
            Parameter(FacetKeys.LastSeen, "Last confirmed reachability.", values: FacetTokens.LastSeenBands),
        };
        foreach (var key in new[] { FacetKeys.Archived, FacetKeys.Adult, FacetKeys.Tls })
        {
            parameters.Add(Parameter(key, "Opt in with 1, true, yes or on; off by default."));
        }
        parameters.Add(new JsonObject
        {
            ["name"] = FacetKeys.Protocol, ["in"] = "query",
            ["description"] = "Measured protocol. Repeat for multiple protocols; comma-separated values also accepted.",
            ["style"] = "form", ["explode"] = true,
            ["schema"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
        });
        foreach (var key in new[]
        {
            FacetKeys.Genre, FacetKeys.Language, FacetKeys.Charset, FacetKeys.Codebase,
            FacetKeys.CodebaseVersion, FacetKeys.CodebaseFamily, FacetKeys.Lineage, FacetKeys.Family,
        })
        {
            parameters.Add(Parameter(key, "Choose a value from response facets. Prefix ! to exclude; ~unknown selects missing values. "
                + "Read the facet's evidence field for its source. codebase-family is the legacy alias for codebase."));
        }
        foreach (var key in new[] { FacetKeys.Uncounted, FacetKeys.Unreachable })
        {
            parameters.Add(Parameter(key, "yes selects, !yes excludes; ~unknown is an alias for !yes.", values: ["yes", "!yes", "~unknown"]));
        }
        parameters.Add(Parameter(FacetKeys.Trending, "Weekly count trend; prefix ! to exclude, or ~unknown for no trend."));
        return parameters;
    }

    private static JsonArray KeyParameters() =>
        [Parameter("key", "Immutable game GUID or current slug. Store the GUID for durable references.", location: "path")];

    private static JsonArray WindowParameters(bool presence)
    {
        var parameters = KeyParameters();
        parameters.Add(Parameter("from", "Inclusive ISO 8601 start. Default: 90 days before to (7 days for hourly presence)."));
        parameters.Add(Parameter("to", "Inclusive ISO 8601 end; defaults to response time. Must be at or after from. "
            + "Maximum window: 1826 days, or 90 days for hourly presence."));
        if (presence)
        {
            parameters.Add(Parameter("grain", "Rollup size; defaults to day. Missing buckets are not zero.", values: ["hour", "day"]));
        }
        return parameters;
    }

    private static JsonObject Parameter(string name, string description, string type = "string",
        string location = "query", IReadOnlyList<string>? values = null)
    {
        var schema = new JsonObject { ["type"] = type };
        if (values is not null)
        {
            schema["enum"] = new JsonArray(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
        }
        return new JsonObject
        {
            ["name"] = name, ["in"] = location, ["required"] = location == "path",
            ["description"] = description, ["schema"] = schema,
        };
    }

    private static JsonObject Header(string description) => new()
    {
        ["description"] = description, ["schema"] = new JsonObject { ["type"] = "string" },
    };

    private static JsonObject Problem(string description) => new()
    {
        ["description"] = description,
        ["content"] = new JsonObject { ["application/problem+json"] = new JsonObject { ["schema"] = Ref(nameof(ApiProblem)) } },
    };

    private static JsonObject Ref(string name) => new() { ["$ref"] = $"#/components/schemas/{name}" };

    // Exporter references are relative to each standalone schema's root. Embedding them in OpenAPI
    // must relocate those pointers, including repeated provenance records and collection elements.
    private static void RebaseReferences(JsonNode node, string root)
    {
        if (node is JsonObject obj)
        {
            if (obj["$ref"] is JsonValue reference && reference.TryGetValue<string>(out var pointer)
                && pointer.StartsWith('#'))
            {
                obj["$ref"] = root + pointer[1..];
            }
            foreach (var child in obj.Select(p => p.Value).OfType<JsonNode>())
            {
                RebaseReferences(child, root);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array.OfType<JsonNode>())
            {
                RebaseReferences(child, root);
            }
        }
    }
}
