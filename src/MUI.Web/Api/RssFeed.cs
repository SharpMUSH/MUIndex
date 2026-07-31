using System.Globalization;
using System.Xml.Linq;

using MUI.Catalog;

namespace MUI.Web.Api;

/// <summary>One of the three liveness registers (spec §9), as a feed.</summary>
public enum FeedRegister
{
    NewlyDiscovered,
    WentDark,
    CameBack,
}

/// <summary>
/// RSS 2.0 for the three liveness feeds — the status-change notification v1 ships (spec §10).
/// </summary>
/// <remarks>
/// <para>
/// RSS and not webhooks: §14 defers webhooks deliberately, and a feed is the version of the same
/// promise that costs a reader no infrastructure and costs us no delivery guarantees. There is no
/// callback surface anywhere in this API and a test says so.
/// </para>
/// <para>
/// The item GUID is derived from the register, the game and the instant, and is marked
/// <c>isPermaLink="false"</c>. It has to be stable across rebuilds or every reader re-notifies on
/// every poll; it has to include the instant because a game that goes dark, comes back and goes dark
/// again is three events about one game and a reader must see all three.
/// </para>
/// </remarks>
public static class RssFeed
{
    public static string Title(FeedRegister register) => register switch
    {
        FeedRegister.NewlyDiscovered => "MUIndex — newly discovered",
        FeedRegister.WentDark => "MUIndex — went dark",
        _ => "MUIndex — came back",
    };

    public static string Description(FeedRegister register) => register switch
    {
        FeedRegister.NewlyDiscovered =>
            "Games MUIndex found and reached for the first time.",
        FeedRegister.WentDark =>
            "Games that stopped answering. Nothing is deleted: the page, the history and the "
            + "probing all continue, and one successful probe reverses it.",
        _ =>
            "Games that answered again after going dark.",
    };

    public static string Path(FeedRegister register) => register switch
    {
        FeedRegister.NewlyDiscovered => ApiRoutes.DiscoveredRss,
        FeedRegister.WentDark => ApiRoutes.WentDarkRss,
        _ => ApiRoutes.CameBackRss,
    };

    public static string Key(FeedRegister register) => register switch
    {
        FeedRegister.NewlyDiscovered => "newly-discovered",
        FeedRegister.WentDark => "went-dark",
        _ => "came-back",
    };

    public static IReadOnlyList<FeedEntry> Entries(LivenessFeeds feeds, FeedRegister register) =>
        register switch
        {
            FeedRegister.NewlyDiscovered => feeds.NewlyDiscovered,
            FeedRegister.WentDark => feeds.WentDark,
            _ => feeds.CameBack,
        };

    /// <summary>A stable, non-permalink item identity. Same event, same GUID, forever.</summary>
    public static string ItemGuid(FeedRegister register, FeedEntry entry) =>
        $"urn:muindex:{Key(register)}:{entry.Slug}:"
        + entry.At.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    public static string Render(
        FeedRegister register,
        IReadOnlyList<FeedEntry> entries,
        Uri origin,
        DateTimeOffset now,
        LicenceView licence)
    {
        XNamespace atom = "http://www.w3.org/2005/Atom";

        var channel = new XElement(
            "channel",
            new XElement("title", Title(register)),
            new XElement("link", Absolute(origin, "/")),
            new XElement("description", Description(register)),
            new XElement("language", "en"),
            new XElement("docs", "https://www.rssboard.org/rss-specification"),
            new XElement("generator", "MUIndex"),
            new XElement("copyright", $"{licence.Attribution} · {licence.Id}"),
            new XElement("lastBuildDate", Rfc1123(now)),
            new XElement(
                atom + "link",
                new XAttribute("rel", "self"),
                new XAttribute("type", "application/rss+xml"),
                new XAttribute("href", Absolute(origin, Path(register)))));

        foreach (var entry in entries.OrderByDescending(e => e.At))
        {
            channel.Add(new XElement(
                "item",
                new XElement("title", entry.Name),
                new XElement("link", Absolute(origin, ApiRoutes.Page(entry.Slug))),
                new XElement("description", entry.Detail),
                new XElement("pubDate", Rfc1123(entry.At)),
                new XElement(
                    "guid",
                    new XAttribute("isPermaLink", "false"),
                    ItemGuid(register, entry))));
        }

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                "rss",
                new XAttribute("version", "2.0"),
                new XAttribute(XNamespace.Xmlns + "atom", atom.NamespaceName),
                channel));

        // A literal newline rather than Environment.NewLine: these bytes are hashed into an ETag, and
        // a feed that changed validator between a Linux host and a Windows one would invalidate every
        // reader's cache for nothing.
        return document.Declaration + "\n" + document;
    }

    /// <summary>
    /// Origin plus a rooted path, by concatenation rather than <see cref="Uri"/> resolution.
    /// </summary>
    /// <remarks>
    /// <c>new Uri(origin, "/g/x")</c> discards the origin's path, which silently drops the path base
    /// when this deployable is mounted under one — and RSS is the one surface where a relative link
    /// is not an option, so a wrong absolute link is what a reader would get.
    /// </remarks>
    private static string Absolute(Uri origin, string path) =>
        origin.ToString().TrimEnd('/') + path;

    private static string Rfc1123(DateTimeOffset at) =>
        at.ToUniversalTime().ToString("R", CultureInfo.InvariantCulture);
}
