using System.Text.Json.Nodes;

using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>
/// Who runs this site, as schema.org — the one graph that is not about the catalogue.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not gated on <c>CatalogueSource.IsMeasured</c>, and that is the difference from every other
/// graph here.</b> <see cref="GameStructuredData"/> is suppressed over the fixture because its
/// subject is invented and the vocabulary has no field meaning "unmeasured". This graph's subject is
/// the site itself — its name, its address, its logo, the languages it answers in — and every one of
/// those is as true over the fixture as over a live crawl. Suppressing it would withhold a true
/// statement, not withhold a false one.
/// </para>
/// <para>
/// <b>No <c>SearchAction</c>.</b> The sitelinks searchbox it fed was deprecated in October 2024 and
/// retired that November; the markup now describes a feature that no longer renders. The rest of
/// <c>WebSite</c> is still read, which is why the node stays.
/// </para>
/// <para>
/// <b><c>sameAs</c> is configured and defaults to empty.</b> It is the claim "this site and that
/// profile are the same entity", and a compiled-in default would have every fork of this software
/// assert it about somebody else's repository from its first deploy — the shape of mistake
/// <c>ContactedMaintainer</c> already made once. A deployment that owns such a profile names it.
/// </para>
/// <para>
/// Addresses are built from the request's own origin for the reason <see cref="SiteUrls"/> gives:
/// nothing here knows the site's public hostname, and a configured one would be wrong on every
/// mirror and preview environment.
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

            // The languages the site actually answers in, which is what the hreflang alternates in
            // the same head already say. A planned locale is not one of them.
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
