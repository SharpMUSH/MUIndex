namespace MUI.Web.Components;

/// <summary>
/// Which pre-rendered card an unfurler shows beside a link to this site.
/// </summary>
/// <remarks>
/// <para>
/// <b>There are five images and not one per game, deliberately.</b> Rendering a card per game would
/// mean a rasterizer in the dependency list and a cache whose contents go stale every crawl cycle —
/// and it would buy very little, because every unfurler worth the name renders <c>og:title</c> and
/// <c>og:description</c> as the headline and the body of the card and treats the image as the
/// decoration beside them. The game's name, its measured count and the age of that measurement all
/// travel in the text, where they are also readable by somebody using a screen reader.
/// </para>
/// <para>
/// So what the image has to do is say which part of the site this is, at a glance, in a chat client
/// where the link is one of forty. Five is enough for that.
/// </para>
/// </remarks>
public enum PreviewCard
{
    /// <summary>The default. Everything that is not one of the four below.</summary>
    Site,

    Game,

    Archive,

    Rankings,

    Reference,
}
