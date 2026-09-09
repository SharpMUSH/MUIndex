using System.Text.Json.Nodes;

using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>
/// Who runs this site, as schema.org — the one graph that is not about the catalogue.
/// </summary>
/// <remarks>
/// <b>Not gated on <c>CatalogueSource.IsMeasured</c>, unlike every other graph here.</b>
/// <see cref="GameStructuredData"/> is suppressed over the fixture because its subject is invented;
/// this graph's subject is the site's own name, address and languages, as true over the fixture as
/// over a crawl.
/// <para>
/// No <c>SearchAction</c>: the sitelinks searchbox it fed was retired in November 2024.
/// <c>sameAs</c> comes from <see cref="SiteIdentityOptions"/> and defaults to empty. Addresses are
/// built from the request's origin, for the reason <see cref="SiteUrls"/> gives.
/// </para>
/// </remarks>
public static class SiteStructuredData
{
    private const string Vocabulary = "https://schema.org";

    /// <summary>The fragment that names the publisher, so other graphs can point at it rather than restate it.</summary>
    public const string OrganizationId = "#organization";

    /// <summary>The graph for any page on this site.</summary>
    /// <param name="origin">This site's absolute origin — scheme, authority and path base.</param>
    /// <param name="tag">The locale being answered in, which the description is written in.</param>
    /// <param name="sameAs">Profiles this deployment has said are its own. Usually empty.</param>
    public static string For(Uri origin, string tag, IReadOnlyList<string> sameAs)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(sameAs);

        var root = origin.ToString().TrimEnd('/');
        var home = root + "/";

        var organization = new JsonObject
        {
            ["@type"] = "Organization",
            ["@id"] = root + OrganizationId,
            ["name"] = PreviewCopy.SiteName,
            ["url"] = home,
            ["description"] = Messages.For(tag, "preview.site"),
            ["logo"] = new JsonObject
            {
                ["@type"] = "ImageObject",
                ["url"] = $"{root}/icon-512.png",
                ["width"] = 512,
                ["height"] = 512,
            },
        };

        if (sameAs.Count > 0)
        {
            organization["sameAs"] = new JsonArray([.. sameAs.Select(url => (JsonNode)JsonValue.Create(url)!)]);
        }

        var website = new JsonObject
        {
            ["@type"] = "WebSite",
            ["@id"] = $"{root}#website",
            ["name"] = PreviewCopy.SiteName,
            ["url"] = home,
            ["publisher"] = new JsonObject { ["@id"] = root + OrganizationId },

            // Offered, not All: a planned locale is not one the site answers in.
            ["inLanguage"] = new JsonArray(
                [.. Locales.Offered.Select(locale => (JsonNode)JsonValue.Create(locale.Tag)!)]),
        };

        var document = new JsonObject
        {
            ["@context"] = Vocabulary,
            ["@graph"] = new JsonArray(organization, website),
        };

        return document.ToJsonString();
    }
}
