namespace MUI.Web;

/// <summary>
/// The claims about this deployment's own identity that only the deployment can make.
/// </summary>
/// <remarks>
/// <para>
/// Configuration rather than constants, for the reason <see cref="SiteUrls"/> states about hostnames
/// and <c>DatasetLicenceOptions</c> states about the licence: this software is deployable by anyone,
/// and a compiled-in default here would have a fork assert, from its first request, that it is the
/// same entity as somebody else's GitHub repository or social profile. That is a claim about a third
/// party, and a claim about a third party is satisfied by a caller who can make it, never by a
/// default.
/// </para>
/// <para>
/// Empty by default, and an empty list means the property is simply omitted from the graph — a
/// missing <c>sameAs</c> is a smaller loss than a wrong one.
/// </para>
/// </remarks>
public sealed class SiteIdentityOptions
{
    public const string Section = "Site";

    /// <summary>
    /// Profiles elsewhere that are this same site — a repository, a forum account, a fediverse handle.
    /// </summary>
    /// <remarks>
    /// Emitted as schema.org <c>sameAs</c> on the site's <c>Organization</c> node, which is what a
    /// search engine reads to tie a brand query to a site it already knows.
    /// </remarks>
    public List<string> SameAs { get; set; } = [];

    /// <summary>The list as the graph takes it, with blanks and duplicates dropped.</summary>
    public IReadOnlyList<string> Profiles() =>
    [
        .. SameAs
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase),
    ];
}
