namespace MUI.Web;

/// <summary>
/// The claims about this deployment's own identity that only the deployment can make.
/// </summary>
/// <remarks>
/// Configuration rather than constants, for the reason <see cref="SiteUrls"/> gives about hostnames:
/// a compiled-in default would have every fork assert, from its first request, that it is the same
/// entity as somebody else's repository. Empty omits the property, which is a smaller loss than a
/// wrong one.
/// </remarks>
public sealed class SiteIdentityOptions
{
    public const string Section = "Site";

    /// <summary>
    /// Profiles elsewhere that are this same site, emitted as schema.org <c>sameAs</c>.
    /// </summary>
    public List<string> SameAs { get; set; } = [];

    /// <summary>The list as the graph takes it. Blanks are dropped: compose forwards an unset
    /// variable as the empty string, and "set to nothing" must mean the claim is not made.</summary>
    public IReadOnlyList<string> Profiles() =>
    [
        .. SameAs
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase),
    ];
}
