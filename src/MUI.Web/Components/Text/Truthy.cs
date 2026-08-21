namespace MUI.Web.Components;

/// <summary>
/// Reads the querystring flags a person actually types.
/// </summary>
/// <remarks>
/// The design specifies <c>?plain=1</c>, and Blazor's own query binding refuses to parse <c>1</c>
/// as a boolean. These are URLs a reader may type or edit by hand, so the site accepts the obvious
/// spellings rather than 500-ing on the one the design mandates. Anything unrecognised is false —
/// a flag nobody set should never turn a surface on.
/// </remarks>
public static class Truthy
{
    public static bool Is(string? value) =>
        value is not null
        && value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on" or "y";
}
