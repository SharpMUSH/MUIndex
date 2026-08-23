using System.Text.Json.Serialization;

namespace MUI.Ares;

/// <summary>
/// One game as AresCentral lists it.
/// </summary>
/// <remarks>
/// Every string is nullable because every one of them is a third party's field that may be absent or
/// blank, and a record that pretends otherwise turns one thin listing into a failed pass for
/// everybody. <see cref="Port"/> is not: the hub sends a number, and a 0 means "nothing to dial",
/// which is <c>AresCycle</c>'s business rather than the deserialiser's.
/// <para>
/// <see cref="LastPing"/> stays a string. It arrives as <c>MM/DD/YYYY</c> in an unstated timezone,
/// and parsing it into a <c>DateTimeOffset</c> would invent a precision the hub never sent. It is
/// somebody else's measurement and is used for nothing — see <c>AresCycle</c>.
/// </para>
/// </remarks>
public sealed record AresListedGame(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("hostname")] string? Hostname,
    [property: JsonPropertyName("port")] int Port,
    [property: JsonPropertyName("genre")] string? Genre,
    [property: JsonPropertyName("website")] string? Website,
    [property: JsonPropertyName("last_ping")] string? LastPing,
    [property: JsonPropertyName("status")] string? Status);

/// <summary>The games AresCentral lists.</summary>
public interface IAresGames
{
    /// <summary>
    /// Every game the hub currently lists.
    /// </summary>
    /// <remarks>
    /// <b>Throws rather than returning an empty list when the request fails.</b> An empty list is a
    /// real answer meaning the hub lists nothing, and a caller that cannot tell the two apart will
    /// read a failed fetch as every game having been delisted at once.
    /// </remarks>
    Task<IReadOnlyList<AresListedGame>> ListAsync(CancellationToken ct = default);
}
