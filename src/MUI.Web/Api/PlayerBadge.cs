using System.Globalization;
using System.Net;
using System.Text;

using MUI.Catalog;

namespace MUI.Web.Api;

/// <summary>What a badge can be showing. Three states, and never two.</summary>
/// <remarks>
/// The same three-state discipline §5.4 applies to an hour of the heatmap, applied to a number on
/// somebody else's front page. <see cref="Counted"/> covers a measured zero — we got in and nobody
/// was there, which is a real fact about a game and a filled cell — and <see cref="Unknown"/> covers
/// both "we could not count" and "we have not counted recently". Collapsing the second into a zero
/// is rule 4, broken on a page we do not control and cannot correct.
/// </remarks>
public enum BadgeState
{
    Counted,

    Unknown,

    /// <summary>The game has gone dark (§7.5). A live-count badge has no live count to give.</summary>
    Archived,
}

/// <summary>
/// The live player-count badge of spec §8.5 — an owner-published output, on somebody else's page.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the surface with the least room and the most exposure, and the rules do not bend for
/// it.</b> A badge is embedded where we have no other sentence, no footnote and no chance to
/// explain, so the number it shows has to carry its own age and its own state or it is exactly the
/// unlabelled figure the incumbents publish. There is space: "14 now · 4m ago" is nine characters
/// more than "14".
/// </para>
/// <para>
/// <b>An unknown count is never a zero.</b> A game whose <c>WHO</c> we cannot parse renders
/// "players unknown" in grey, not "0 players" in green — the second would be our parser's limit
/// published as a fact about their game, on their own front page, which is rule 5 at its most
/// damaging.
/// </para>
/// <para>
/// No external font, no remote reference, no script. The SVG is self-contained because a badge that
/// fetched anything would put a third-party request on every page that embeds it, and because an
/// <c>&lt;img&gt;</c> does not execute one anyway.
/// </para>
/// </remarks>
public static class PlayerBadge
{
    /// <summary>
    /// How long a badge may be cached by a browser or a CDN.
    /// </summary>
    /// <remarks>
    /// Five minutes: short enough that "live" is not a lie on a page somebody refreshes, long enough
    /// that a game on a popular front page does not turn its readers into our traffic. Deliberately
    /// well inside <c>FieldRegistry.Volatile</c>, the two hours after which a count is stale — a
    /// cache entry that outlived the freshness of what it holds would be publishing a stale number
    /// with a live label.
    /// </remarks>
    public const string CacheControl = "public, max-age=300";

    /// <summary>The site's own accent, which means <em>measured</em> everywhere it appears.</summary>
    private const string Measured = "#35d29a";

    /// <summary>Grey. Never the accent, because there is nothing measured to point at.</summary>
    private const string Absent = "#6b747c";

    private const string Label = "mu*index";

    private const int Height = 20;

    /// <summary>
    /// Reads a summary into the one of three things a badge can say.
    /// </summary>
    /// <remarks>
    /// Archived is decided before the count, and not after. An archived game may still carry a count
    /// from the last time it answered, and rendering it would put a live-sounding number on a page
    /// for a game that stopped answering in 2023.
    /// </remarks>
    public static BadgeReading Read(GameSummary game, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(game);

        if (game.State is LifecycleState.Archived)
        {
            return new BadgeReading(BadgeState.Archived, null, null, game.LastReachableAt);
        }

        // The two halves of a measurement travel together or not at all: a count with no age is a
        // number we cannot label, and an age with no count labels nothing.
        return game is { PlayersNow: { } count, PlayersNowAt: { } at }
            ? new BadgeReading(BadgeState.Counted, count, now - at, game.LastReachableAt)
            : new BadgeReading(BadgeState.Unknown, null, null, game.LastReachableAt);
    }

    /// <summary>The badge as an SVG document.</summary>
    public static string Svg(BadgeReading reading, string gameName)
    {
        ArgumentNullException.ThrowIfNull(reading);

        var value = reading.Text;
        var colour = reading.State is BadgeState.Counted ? Measured : Absent;

        var labelWidth = Width(Label) + 12;
        var valueWidth = Width(value) + 12;
        var total = labelWidth + valueWidth;

        // Everything interpolated below is either ours or escaped. A game's name is MSSP text and
        // therefore attacker-controlled — it reaches the accessible title and nowhere else, and it
        // goes through WebUtility.HtmlEncode on the way, because an SVG is a document and a name
        // containing "</text>" would otherwise be markup.
        var title = WebUtility.HtmlEncode($"{gameName} — {reading.Description}");

        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="{total}" height="{Height}"
                 role="img" aria-label="{title}">
              <title>{title}</title>
              <linearGradient id="s" x2="0" y2="100%">
                <stop offset="0" stop-color="#fff" stop-opacity=".08"/>
                <stop offset="1" stop-opacity=".08"/>
              </linearGradient>
              <rect width="{total}" height="{Height}" rx="3" fill="#1d2125"/>
              <rect x="{labelWidth}" width="{valueWidth}" height="{Height}" rx="3" fill="{colour}"/>
              <rect x="{labelWidth}" width="4" height="{Height}" fill="{colour}"/>
              <rect width="{total}" height="{Height}" rx="3" fill="url(#s)"/>
              <g font-family="Verdana,DejaVu Sans,Geneva,sans-serif" font-size="11">
                <text x="{labelWidth / 2}" y="14" fill="#e8eaec" text-anchor="middle">{Label}</text>
                <text x="{labelWidth + (valueWidth / 2)}" y="14" fill="#0f1113"
                      text-anchor="middle">{WebUtility.HtmlEncode(value)}</text>
              </g>
            </svg>
            """;
    }

    /// <summary>
    /// A badge for a slug we do not have, as a badge.
    /// </summary>
    /// <remarks>
    /// A 404 with a body, rather than an empty one. The reader of this is an operator who has just
    /// pasted the wrong URL into their own site, and a broken-image icon tells them nothing while
    /// this tells them the thing they need to know. The status is still 404 and it is never cached.
    /// </remarks>
    public static string UnknownSvg() =>
        Svg(new BadgeReading(BadgeState.Unknown, null, null, null) { Override = "unknown game" }, "mu*index");

    /// <summary>
    /// How wide a string renders at 11px, near enough to lay a box out around it.
    /// </summary>
    /// <remarks>
    /// An estimate, and it only has to be good enough that the text does not touch the edges: SVG
    /// has no text metrics without a layout engine, and shipping a font to get them would put a
    /// remote asset on every page that embeds this. Digits and lower-case are the common case and
    /// are measured closest; anything wider simply gets a roomier box.
    /// </remarks>
    private static int Width(string text)
    {
        var width = 0d;

        foreach (var c in text)
        {
            width += c switch
            {
                >= 'A' and <= 'Z' => 7.5,
                'i' or 'j' or 'l' or 't' or 'f' or 'r' or '.' or ' ' or '·' => 3.6,
                'm' or 'w' => 9.5,
                _ => 6.3,
            };
        }

        return (int)Math.Ceiling(width);
    }
}

/// <summary>
/// What the badge says, and why. The same reading serves the SVG and the JSON.
/// </summary>
/// <remarks>
/// One reading behind both outputs, so an owner embedding the image and an owner reading the JSON
/// cannot be told two different things about the same moment — which is the same reason the plain
/// surface renders from the page's own view models.
/// </remarks>
public sealed record BadgeReading(
    BadgeState State,
    int? Count,
    TimeSpan? Age,
    DateTimeOffset? LastReachableAt)
{
    /// <summary>Set only by <see cref="PlayerBadge.UnknownSvg"/>, for a slug that names no game.</summary>
    internal string? Override { get; init; }

    /// <summary>
    /// The words on the badge.
    /// </summary>
    /// <remarks>
    /// A measured zero says "0 now", which is a fact we measured and are entitled to publish. An
    /// unmeasured count says "players unknown" and never borrows the shape of a number.
    /// </remarks>
    public string Text => Override ?? State switch
    {
        BadgeState.Counted => $"{Count!.Value.ToString(CultureInfo.InvariantCulture)} now · {Relative()}",
        BadgeState.Archived => "archived",
        _ => "players unknown",
    };

    /// <summary>The same, as a sentence, for the accessible title and the JSON.</summary>
    public string Description => State switch
    {
        BadgeState.Counted => $"{Count} players measured {Relative()} ago",
        BadgeState.Archived => "archived — this game has stopped answering",
        _ => "player count not measured",
    };

    /// <summary>How the API names this state, matching <see cref="PlayerCountState"/> where it can.</summary>
    public string Word => State switch
    {
        BadgeState.Counted => "measured",
        BadgeState.Archived => "archived",
        _ => "unknown",
    };

    /// <summary>
    /// A coarse age, because a badge has room for two characters and not for "4 minutes ago".
    /// </summary>
    /// <remarks>
    /// Rounded down, never up. "1h" for something measured fifty-nine minutes ago overstates its
    /// age, which is the safe direction; rounding the other way would call an hour-old number fresh.
    /// </remarks>
    private string Relative() => Age switch
    {
        null => "?",
        { TotalMinutes: < 1 } => "just now",
        { TotalHours: < 1 } age => $"{(int)age.TotalMinutes}m",
        { TotalDays: < 1 } age => $"{(int)age.TotalHours}h",
        var age => $"{(int)age!.Value.TotalDays}d",
    };
}
