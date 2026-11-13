namespace MUI.Web.Components;

/// <summary>
/// The webfont a double-width connect screen is drawn in, so every reader gets the same grid.
/// </summary>
/// <remarks>
/// <para>
/// Fontsource's Noto Sans SC/TC/JP/KR from jsDelivr, pinned to an exact version and checked with
/// subresource integrity. Each stylesheet declares about a hundred <c>unicode-range</c> slices, so a
/// browser fetches only the slices holding the characters a screen uses — a Big5 banner pulls a
/// handful of 20–35 KB files, never the whole face. Han, box drawing, <c>●</c> and the fullwidth
/// forms are all one em wide in these faces; ASCII is taken by <c>MUI Mono Latin</c> in
/// <c>app.css</c> at exactly half that, so the 2:1 grid the art was drawn on holds.
/// </para>
/// <para>
/// The region is chosen here, once, from the screen's language, and stamped on the
/// <c>&lt;pre&gt;</c> as <c>data-han</c> for the stylesheet to key on. Not <c>:lang()</c>: a UTF-8
/// screen inherits the page's language, so a reader on the Japanese UI would select the JP family
/// while this linked the SC stylesheet, and fall through to whatever their machine has.
/// </para>
/// </remarks>
public static class ScreenFonts
{
    /// <summary>One region's stylesheet, and the hash that pins its content.</summary>
    public sealed record Face(string Han, string Href, string Integrity);

    private const string Version = "5.3.1";

    private static readonly Face Simplified = Of("sc", "sha384-3J1mF7lN+W9OoArkGF3o+9NCyelrsENlSRZej5EIR68CXdSR6Kj99P9MuENhw6hD");
    private static readonly Face Traditional = Of("tc", "sha384-X6XRC062QGnH48n64y+H6zP8JzoQKVtcSpIZbr+2W8RQ8VjVKZur7vLT3FuFzkjD");
    private static readonly Face Japanese = Of("jp", "sha384-AddiZjKc0Y/yZsNpuffruREs7CrjbSKlAMPc6my23UeXmDGNw0CnPbNrTm1YSOAk");
    private static readonly Face Korean = Of("kr", "sha384-nQyA+RiZLm5dIoII5WNdqiAd7A+WXyZlOeB0ZzJcWwf48JdjNSzhUj6mqQJk+j2A");

    /// <summary>
    /// The face for a screen's language. Simplified Chinese when the language says nothing — a UTF-8
    /// screen, which carries every script — because most of the CJK catalogue is GBK.
    /// </summary>
    public static Face For(string? language) => language switch
    {
        "zh-Hant" => Traditional,
        "ja" => Japanese,
        "ko" => Korean,
        _ => Simplified,
    };

    private static Face Of(string han, string integrity) =>
        new(han, $"https://cdn.jsdelivr.net/npm/@fontsource/noto-sans-{han}@{Version}/400.css", integrity);
}
