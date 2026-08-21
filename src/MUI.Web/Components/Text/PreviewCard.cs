namespace MUI.Web.Components;

/// <summary>
/// Which pre-rendered card an unfurler shows beside a link to this site.
/// </summary>
/// <remarks>
/// Five fixed images rather than one rendered per game: an unfurler shows <c>og:title</c> and
/// <c>og:description</c> as the headline and body, with the image as decoration, so the game's name
/// and measured count travel in text (also readable by a screen reader) rather than needing a
/// per-game rasterizer. Five images are enough to say which part of the site a link points to.
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
