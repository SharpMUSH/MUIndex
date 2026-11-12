namespace MUI.Crawl;

/// <summary>
/// The terminal the probe says it is, over NAWS (RFC 1073), when a game asks.
/// </summary>
/// <remarks>
/// The probe offers <c>WILL NAWS</c> to every game, and before this it never followed a game's
/// <c>DO NAWS</c> with a size — so a game that wraps to the client's window wrapped to whatever it
/// assumes when nobody says, and the site then laid that screen out at a width of its own choosing.
/// One number now does both jobs: the width reported here is the width <c>MUI.Web</c>'s connect-screen
/// frame folds at, so a game that honours NAWS formats for the grid it is drawn in.
/// </remarks>
public static class TerminalWindow
{
    /// <summary>
    /// The width reported, and the grid a connect screen is laid out at when it asks for nothing
    /// wider. 80 clipped a real cluster of AresMUSH-family banners mid-glyph; 100 clears the widest
    /// line on 809 of the 865 screens held when the site's frame was sized.
    /// </summary>
    public const int Columns = 100;

    /// <summary>
    /// The height reported. 24 is what a terminal that says nothing is taken to be, so a game that
    /// pages by the client's height pages exactly where it did before the probe reported anything.
    /// </summary>
    public const int Rows = 24;
}
