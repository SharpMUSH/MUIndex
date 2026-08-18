using System.Globalization;
using System.Net;
using System.Text;

using MUI.Catalog;

namespace MUI.Web.Api;

/// <summary>What a badge can be showing. Three states, and never two.</summary>
/// <remarks>
/// The same three-state discipline §5.4 applies to the heatmap, applied to a number on somebody
/// else's front page. <see cref="Counted"/> covers a measured zero (a real fact); <see cref="Unknown"/>
/// covers both "could not count" and "not counted recently" — collapsing that into a zero is rule 4,
/// broken on a page we do not control.
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
/// <b>The rules do not bend for lack of room.</b> A badge has no footnote and no chance to explain,
/// so the number carries its own age and state, or it's the unlabelled figure the incumbents publish.
/// <b>An unknown count is never a zero</b> — an unparseable <c>WHO</c> renders "players unknown" in
/// grey, never "0 players" in green (rule 5).
/// No external font, no remote reference, no script — self-contained, since a badge that fetched
/// anything would put a third-party request on every page that embeds it.
/// </remarks>
public static class PlayerBadge
{
    /// <summary>
    /// How long a badge may be cached by a browser or a CDN.
    /// </summary>
    /// <remarks>
    /// Five minutes: short enough that "live" isn't a lie, long enough that a popular front page
    /// doesn't turn its readers into our traffic. Well inside <c>FieldRegistry.Volatile</c>'s
    /// two-hour staleness window, so the cache can't outlive the freshness of what it holds.
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
    /// <remarks>Archived is decided before the count, not after — an archived game may still carry a stale count that would otherwise render as live.</remarks>
    public static BadgeReading Read(GameSummary game, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(game);

        if (game.State is LifecycleState.Archived)
        {
            return new BadgeReading(BadgeState.Archived, null, null, game.LastReachableAt);
        }

        // The badge paints the measured accent and writes "measured Xm ago", so a count the game
        // merely asserted in MSSP must not reach it — read off the same ProvenanceChip.IsMeasured
        // ApiMapper reads, so the badge and playersNowState can't disagree. Declared renders as
        // unknown; the game's own claim belongs on its page, labelled as theirs.
        return game is { PlayersNow: { } count, PlayersNowProvenance: { IsMeasured: true } chip }
            ? new BadgeReading(BadgeState.Counted, count, now - chip.LastConfirmedAt, game.LastReachableAt)
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

        // A game's name is MSSP text, attacker-controlled, and an SVG is a document — HtmlEncode is
        // what stops a name containing "</text>" becoming markup.
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
    /// <remarks>A 404 with a body rather than an empty one — a broken-image icon would tell the operator nothing. Still 404, never cached.</remarks>
    public static string UnknownSvg() =>
        Svg(new BadgeReading(BadgeState.Unknown, null, null, null) { Override = "unknown game" }, "mu*index");

    /// <summary>
    /// How wide a string renders at 11px, near enough to lay a box out around it.
    /// </summary>
    /// <remarks>An estimate, good enough that text doesn't touch the edges — SVG has no text metrics without a layout engine, and shipping a font would add a remote asset.</remarks>
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
/// <remarks>One reading behind both outputs, so an owner embedding the image and one reading the JSON can't be told two different things about the same moment.</remarks>
public sealed record BadgeReading(
    BadgeState State,
    int? Count,
    TimeSpan? Age,
    DateTimeOffset? LastReachableAt)
{
    /// <summary>Set only by <see cref="PlayerBadge.UnknownSvg"/>, for a slug that names no game.</summary>
    internal string? Override { get; init; }

    /// <summary>
    /// What the badge says when we could not count, in the one language it has.
    /// </summary>
    /// <remarks>A constant rather than a literal because the owner dashboard quotes it — two copies would let the dashboard promise something the image never draws.</remarks>
    public const string UnknownText = "players unknown";

    /// <summary>What the badge says for a game that stopped answering. Quoted the same way.</summary>
    public const string ArchivedText = "archived";

    /// <summary>
    /// The words on the badge.
    /// </summary>
    /// <remarks>A measured zero says "0 now"; an unmeasured count says "players unknown" and never borrows the shape of a number.</remarks>
    public string Text => Override ?? State switch
    {
        BadgeState.Counted => $"{Count!.Value.ToString(CultureInfo.InvariantCulture)} now · {Relative()}",
        BadgeState.Archived => ArchivedText,
        _ => UnknownText,
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
    /// <remarks>Rounded down, never up — rounding the other way would call an hour-old number fresh.</remarks>
    private string Relative() => Age switch
    {
        null => "?",
        { TotalMinutes: < 1 } => "just now",
        { TotalHours: < 1 } age => $"{(int)age.TotalMinutes}m",
        { TotalDays: < 1 } age => $"{(int)age.TotalHours}h",
        var age => $"{(int)age!.Value.TotalDays}d",
    };
}
