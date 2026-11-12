using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MUI.I3;

/// <summary>
/// Undoes the gateway's Latin-1 fallback, one string at a time.
/// </summary>
/// <remarks>
/// <para>
/// The sidecar decodes a whole mudmode <em>packet</em> as UTF-8 and, when that fails, decodes the
/// whole packet as Latin-1 instead (<c>src/network/lpc.py</c>, <c>LPCDecoder.decode</c>). A mudlist
/// packet carries many muds, so one Big5 or Latin-1 name in it turns every UTF-8 name beside it into
/// mojibake: <c>時空幻境</c> arrived as <c>æ\u0099\u0082ç©ºå¹»å¢\u0083</c> and was listed that way.
/// </para>
/// <para>
/// Latin-1 is total and round-trips, so the original bytes are still there. A string whose every
/// character is at most <c>U+00FF</c> is re-encoded to them; if those bytes are well-formed UTF-8 and
/// not plain ASCII they are read as UTF-8, by the same strict rule <c>WireEncoding</c> uses for a
/// telnet session — a lead byte followed by continuation bytes in <c>0x80–0xBF</c> is essentially
/// never formed by legacy text by accident. Anything else is left exactly as it arrived.
/// </para>
/// <para>
/// Repairing the name also repairs the route: the gateway encodes what we send it as UTF-8, so asking
/// <c>who</c> of the mojibake name addressed a mud that does not exist.
/// </para>
/// </remarks>
public static class SidecarText
{
    /// <summary>The string as the mud sent it, where the gateway's fallback can be undone.</summary>
    public static string Recover(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var nonAscii = false;

        foreach (var c in text)
        {
            if (c > 'ÿ')
            {
                return text;
            }

            nonAscii |= c > '\u007F';
        }

        if (!nonAscii)
        {
            return text;
        }

        var bytes = Encoding.Latin1.GetBytes(text);

        return System.Text.Unicode.Utf8.IsValid(bytes) ? Encoding.UTF8.GetString(bytes) : text;
    }
}

/// <summary>Applies <see cref="SidecarText.Recover"/> to every string read off the gateway.</summary>
internal sealed class RecoveringStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() is { } text ? SidecarText.Recover(text) : null;

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
