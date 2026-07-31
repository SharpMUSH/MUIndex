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
/// <para>
/// One instance rather than one per endpoint, because an ETag is a hash of the bytes we sent: two
/// options objects that disagree about casing or escaping would produce two different hashes for
/// the same facts, and a conditional request would then miss for no reason.
/// </para>
/// <para>
/// Enums go out as camel-cased names — <c>"handshake"</c>, <c>"reachable"</c>, <c>"archived"</c> —
/// because a consumer writing a MUD client should not have to learn our declaration order, and a
/// number would silently re-map if a member were ever inserted.
/// </para>
/// <para>
/// <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/> is deliberate: connect screens are the
/// most Unicode-dense thing this API carries, and escaping every box-drawing character to
/// <c>\uXXXX</c> quadruples the dump for no reader's benefit. It is safe here because this JSON is
/// never interpolated into HTML — it is served as <c>application/json</c> with
/// <c>X-Content-Type-Options: nosniff</c>, which is what stops a browser treating it as a document.
/// </para>
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

            // Never indented: the bytes are hashed, streamed and often megabytes. A consumer who
            // wants it readable has jq.
            WriteIndented = false,

            // A null is a fact here — "we did not measure a count" — so it ships rather than being
            // dropped. A consumer that has to tell a missing key from a null key has been handed
            // our storage's shape instead of an answer.
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        // Frozen at first use, so nothing can add a converter after a body has been hashed with the
        // options as they were. Naming the reflection resolver rather than letting MakeReadOnly
        // populate one keeps that explicit and keeps this file honest about using reflection.
        options.TypeInfoResolver = new DefaultJsonTypeInfoResolver();
        options.MakeReadOnly();
        return options;
    }
}
