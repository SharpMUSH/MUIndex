using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace MUI.Web.Api;

/// <summary>
/// The one <see cref="JsonSerializerOptions"/> every API surface serialises through — the bodies,
/// the streamed dump, and the bytes an ETag is hashed over.
/// </summary>
/// <remarks>
/// One shared instance: options that disagreed on casing or escaping would hash the same facts to
/// different ETags. Enums serialize as camelCase names rather than ordinal numbers, so a consumer
/// isn't tracking our declaration order. <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/> is
/// safe because this is served as <c>application/json</c> with <c>X-Content-Type-Options: nosniff</c>
/// and never interpolated into HTML.
/// </remarks>
public static class ApiJson
{
    public static readonly JsonSerializerOptions Options = Build();

    private static JsonSerializerOptions Build()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,

            // Never indented: the bytes are hashed and streamed.
            WriteIndented = false,

            // A null is a fact ("we did not measure this") and must ship, not be dropped — a missing
            // key vs. a null key would otherwise mean the same thing to a consumer.
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        // Frozen at first use so nothing can add a converter after a body has been hashed against it.
        options.TypeInfoResolver = new DefaultJsonTypeInfoResolver();
        options.MakeReadOnly();
        return options;
    }
}
