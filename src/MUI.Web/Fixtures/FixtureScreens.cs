namespace MUI.Web.Fixtures;

/// <summary>
/// Connect screens for the fixture, with the SGR the servers actually send.
/// </summary>
/// <remarks>
/// These exist to exercise the four cases the quotation frame has to survive: a screen that fits, a
/// screen long enough to crop, a screen too small to frame at all, and a screen long enough that the
/// caption has to explain itself. Colour is present because a renderer that is only ever handed
/// plain text is not a renderer that has been tested.
/// </remarks>
internal static class FixtureScreens
{
    private const string Esc = "\u001b[";

    /// <summary>
    /// M*U*S*H, the PennMUSH development server. Twelve rows: fits inside the frame uncropped.
    /// </summary>
    internal static readonly string Mush = Join(
        $"{Esc}36m ╔════════════════════════════════════════════════════════════╗{Esc}0m",
        $"{Esc}36m ║{Esc}0m  {Esc}1;36mM * U * S * H{Esc}0m                                             {Esc}36m║{Esc}0m",
        $"{Esc}36m ║{Esc}0m  {Esc}90mthe PennMUSH development server{Esc}0m                           {Esc}36m║{Esc}0m",
        $"{Esc}36m ╚════════════════════════════════════════════════════════════╝{Esc}0m",
        string.Empty,
        $"   {Esc}37mmush.pennmush.org 4201{Esc}0m   {Esc}90m·{Esc}0m   {Esc}37m4202 (ssl){Esc}0m",
        $"   Running {Esc}33mPennMUSH 1.8.7{Esc}0m",
        string.Empty,
        $"   {Esc}32mconnect{Esc}0m <name> <password>    {Esc}90m— returning players{Esc}0m",
        $"   {Esc}32mcreate{Esc}0m  <name> <password>    {Esc}90m— new characters welcome{Esc}0m",
        string.Empty,
        $"   {Esc}90mContact: mush@gungnir.pennmush.org{Esc}0m");

    /// <summary>
    /// Aardwolf. Thirty rows, so the page crops at twenty-four and offers the rest — and the player
    /// count is in the art, which is the only place this game publishes it.
    /// </summary>
    internal static readonly string Aardwolf = Join(
        $"{Esc}1;34m      .:{Esc}36m*{Esc}34m:.{Esc}0m",
        $"{Esc}1;33m   A A R D W O L F   M U D{Esc}0m",
        $"{Esc}36m   ─────────────────────────────────────────────────────────{Esc}0m",
        string.Empty,
        $"   {Esc}37mThe most active MUD on the internet.{Esc}0m",
        $"   {Esc}32mThere are currently 219 players connected.{Esc}0m",
        string.Empty,
        $"   {Esc}33m[1]{Esc}0m  Enter the game",
        $"   {Esc}33m[2]{Esc}0m  Create a new character",
        $"   {Esc}33m[3]{Esc}0m  Read the news",
        $"   {Esc}33m[4]{Esc}0m  Who is online",
        $"   {Esc}33m[5]{Esc}0m  Help for new players",
        $"   {Esc}33m[6]{Esc}0m  Player-run clans",
        $"   {Esc}33m[7]{Esc}0m  Quests and campaigns",
        $"   {Esc}33m[8]{Esc}0m  Remorts and tiers",
        $"   {Esc}33m[9]{Esc}0m  The Aardwolf wiki",
        string.Empty,
        $"{Esc}36m   ─────────────────────────────────────────────────────────{Esc}0m",
        $"   {Esc}90mAardwolf supports GMCP, MCCP2, MSDP, MSSP and 256 colour.{Esc}0m",
        $"   {Esc}90mTelnet clients: set your terminal to 80 columns.{Esc}0m",
        $"{Esc}36m   ─────────────────────────────────────────────────────────{Esc}0m",
        string.Empty,
        $"   {Esc}35mnews{Esc}0m · {Esc}35mrules{Esc}0m · {Esc}35mareas{Esc}0m · {Esc}35mclans{Esc}0m",
        string.Empty,
        $"   {Esc}90mBy connecting you agree to the rules. Read them: 'help rules'.{Esc}0m",
        $"   {Esc}90mReport problems to the immortals with 'bug'.{Esc}0m",
        string.Empty,
        $"   {Esc}37mEnter your character's name, or 'new' to create one.{Esc}0m",
        string.Empty,
        "   >");

    /// <summary>
    /// Midnight Sun II answers with a bare prompt. Two rows: the hero collapses entirely rather than
    /// framing an almost-empty box and calling it evidence.
    /// </summary>
    internal static readonly string MidnightSun = Join(
        "Midnight Sun II",
        "By what name are you known?");

    /// <summary>
    /// A screen past the 200-row rule. The payload is generated rather than pasted — what is being
    /// exercised is the length, and 214 rows of somebody's ASCII art in a source file would be a
    /// worse fixture, not a better one.
    /// </summary>
    internal static readonly string Enormous = Join(
        [.. Enumerable.Range(1, 214).Select(i => i switch
        {
            1 => $"{Esc}1;35m  B A T M U D{Esc}0m",
            2 => $"{Esc}90m  ────────────────────────────────────────────────{Esc}0m",
            214 => $"{Esc}37m  Enter your name:{Esc}0m",
            _ when i % 12 == 0 => $"{Esc}33m  ── section {i / 12} ──{Esc}0m",
            _ => $"{Esc}90m  news line {i:000} — the intro screen runs to 214 rows{Esc}0m",
        })]);

    private static string Join(params string[] lines) => string.Join('\n', lines);
}
