using Microsoft.AspNetCore.Components;

namespace MUI.Streaming.Prototype;

/// <summary>A deliberate insertion point between a rendered document's prefix and suffix.</summary>
/// <remarks>Razor components produce balanced markup; this slot lets the writer insert rows later.</remarks>
public static class HtmlInsertion
{
    private const string Marker = "<!--MUI-STREAM-ROWS-->";
    public static RenderFragment Placeholder { get; } = builder => builder.AddMarkupContent(0, Marker);

    public static (string Prefix, string Suffix)? Split(string document)
    {
        var offset = document.IndexOf(Marker, StringComparison.Ordinal);
        if (offset < 0)
        {
            return null; // Plain mode and validation errors do not render the rows slot.
        }
        if (document.LastIndexOf(Marker, StringComparison.Ordinal) != offset)
        {
            throw new InvalidOperationException("The document must contain exactly one row insertion point.");
        }
        return (document[..offset], document[(offset + Marker.Length)..]);
    }
}
