using System.Text.Json;

namespace MUI.Web.Tests.Api;

/// <summary>Reading a response the way a consumer would: as bytes, and as JSON over those bytes.</summary>
public static class Json
{
    public static async Task<(byte[] Body, JsonDocument Document)> ReadAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsByteArrayAsync();
        return (body, JsonDocument.Parse(body));
    }

    public static async Task<JsonElement> ElementAsync(HttpResponseMessage response)
    {
        var (_, document) = await ReadAsync(response);
        return document.RootElement.Clone();
    }

    /// <summary>Every property name in a document, at every depth.</summary>
    public static IEnumerable<string> PropertyNames(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    yield return property.Name;
                    foreach (var nested in PropertyNames(property.Value))
                    {
                        yield return nested;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in PropertyNames(item))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }

    /// <summary>Every string value in a document, at every depth.</summary>
    public static IEnumerable<string> StringValues(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                yield return element.GetString() ?? string.Empty;
                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    foreach (var nested in StringValues(property.Value))
                    {
                        yield return nested;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in StringValues(item))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }
}
