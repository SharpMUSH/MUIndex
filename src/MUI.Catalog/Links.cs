namespace MUI.Catalog;

/// <summary>
/// A way to reach a game's people, and which of them it is.
/// </summary>
/// <remarks>
/// <para>
/// A kind rather than a field name, because two field names can answer one kind: an email address
/// arrives as MSSP's official <c>CONTACT</c> and also as the unofficial <c>EMAIL</c>, and a reader
/// wants one envelope beside the title either way.
/// </para>
/// <para>
/// Everything past <see cref="Discord"/> exists because MSSP has no variable for it, so those kinds
/// are answered only by <see cref="FieldSource.Owner"/> rows, written by somebody who proved the
/// game is theirs (spec §8.5).
/// </para>
/// </remarks>
public enum LinkKind
{
    /// <summary>MSSP <c>WEBSITE</c>. The place to go first, so it is rendered first.</summary>
    Website,

    /// <summary>A wiki about the game. No MSSP variable.</summary>
    Wiki,

    /// <summary>A forum or message board. No MSSP variable.</summary>
    Forum,

    /// <summary>MSSP <c>DISCORD</c>.</summary>
    Discord,

    /// <summary>A Telegram group or channel. No MSSP variable.</summary>
    Telegram,

    /// <summary>A Mastodon or other fediverse account. No MSSP variable.</summary>
    Mastodon,

    /// <summary>A Bluesky account. No MSSP variable.</summary>
    Bluesky,

    /// <summary>An X account. No MSSP variable.</summary>
    X,

    /// <summary>
    /// MSSP <c>CONTACT</c>, or the unofficial <c>EMAIL</c> where there is no <c>CONTACT</c>.
    /// </summary>
    /// <remarks>
    /// Last, because it is somebody's inbox. Every other link here reaches a room with a door on it;
    /// this one reaches a person, and the ordering says so without a word of copy.
    /// </remarks>
    Email,
}

/// <summary>
/// One reachable destination a game published, with the provenance every other fact here carries.
/// </summary>
/// <remarks>
/// <see cref="Href"/> is normalised, never the stored value: it has been through
/// <see cref="ExternalUrl"/>, so a surface rendering it does not have to decide whether a
/// stranger's <c>javascript:</c> is safe. The source row is untouched and still prints verbatim —
/// we refuse to link a value, never to show it.
/// </remarks>
public sealed record QuickLink(
    LinkKind Kind,
    string Field,
    string Href,
    string Shown,
    FieldSource Source,
    DateTimeOffset LastConfirmedAt,
    bool IsStale)
{
    /// <summary>Whether somebody observed this, as opposed to a game or its owner reporting it.</summary>
    /// <remarks>
    /// Derived rather than hard-coded (always false in practice) so the measured/declared line
    /// stays owned by <see cref="FieldSources"/> alone, not duplicated here.
    /// </remarks>
    public bool IsMeasured => FieldSources.IsMeasured(Source);
}

/// <summary>
/// Whether a value somebody else typed can be put in an <c>href</c>, and what it looks like when it
/// can.
/// </summary>
/// <remarks>
/// <para>
/// <b>The input is hostile by default.</b> <c>WEBSITE</c> values come off strangers' sockets or an
/// owner's text field. <c>javascript:</c> in an <c>href</c> is script execution on our origin, and
/// Blazor's encoding does not stop it — it encodes the text of an attribute, not the meaning of its
/// scheme.
/// </para>
/// <para>
/// <b>It refuses rather than repairs.</b> A value with no scheme (<c>www.slothmud.org</c>) is not
/// given one — that would be us guessing whether their server answers on TLS and publishing the
/// guess as their address. It still renders as text like every other declared field. A link we
/// invented that 404s is worse than a line of text that is exactly what they wrote.
/// </para>
/// </remarks>
public static class ExternalUrl
{
    /// <summary>
    /// The longest destination we will store or render.
    /// </summary>
    /// <remarks>
    /// Restated as a constant so the render path does not depend on the write path having run —
    /// MSSP rows never went through the enrichment form that applies this bound elsewhere.
    /// </remarks>
    public const int MaxLength = 500;

    /// <summary>
    /// The value as an <c>href</c>, or null if it is not one.
    /// </summary>
    /// <param name="value">What a game or an owner typed. Trimmed here; never trusted.</param>
    /// <param name="shape">
    /// What the field is supposed to hold. <see cref="FieldShape.Text"/> is never a link — asking
    /// this about a <c>GENRE</c> is a bug, and null is the honest answer to it.
    /// </param>
    public static string? Normalise(string? value, FieldShape shape)
    {
        var text = value?.Trim();

        if (string.IsNullOrEmpty(text) || text.Length > MaxLength)
        {
            return null;
        }

        return shape switch
        {
            FieldShape.Url => Web(text),

            // CONTACT is an email address per spec, but sometimes an https:// contact page in
            // practice — both are ways to reach the same people.
            FieldShape.Email => Mail(text) ?? Web(text),
            _ => null,
        };
    }

    /// <summary>Whether this value would render as a link. The question the linter asks.</summary>
    public static bool IsLinkable(string? value, FieldShape shape) => Normalise(value, shape) is not null;

    private static string? Web(string text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return null;
        }

        // http and https only: `javascript:` and `data:` are script, `file:` and `ftp:` point
        // elsewhere. Compared rather than pattern-matched because Uri.UriSchemeHttp is a static
        // field, not a constant, so it cannot appear in a pattern.
        var scheme = uri.Scheme;

        if ((!string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
             && !string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
            || uri.Host.Length == 0)
        {
            return null;
        }

        // https://user:pass@evil.example/ renders in a browser's status bar as the text before the
        // @, which is the whole of the trick. Nobody's website needs credentials in its address.
        return uri.UserInfo.Length > 0 ? null : uri.AbsoluteUri;
    }

    private static string? Mail(string text)
    {
        var address = text.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            ? text["mailto:".Length..]
            : text;

        // Deliberately not a validator: some CONTACT values are obfuscated against harvesters
        // ("name (at) example (dot) com"), and the point is only to tell an address apart from a
        // spelling of one, so the spelling renders as text rather than as a dead mailto.
        var at = address.IndexOf('@', StringComparison.Ordinal);

        if (at <= 0 || at != address.LastIndexOf('@') || address.AsSpan().ContainsAny(" \t\r\n"))
        {
            return null;
        }

        var domain = address[(at + 1)..];

        return domain.Length > 2 && domain.Contains('.', StringComparison.Ordinal)
               && !domain.StartsWith('.') && !domain.EndsWith('.')
            ? "mailto:" + address
            : null;
    }
}

/// <summary>
/// The links beside a game's name, built from the rows it already has (spec §5.1, §8.5).
/// </summary>
/// <remarks>
/// <para>
/// Derived on read, nothing stored: an owner's <c>WEBSITE</c> and their game's own report are two
/// rows keyed <c>(game, field, source)</c>, and which wins is <see cref="FieldPrecedence"/>'s
/// answer rather than a column kept in step by hand.
/// </para>
/// <para>
/// The icons are navigation, the chips are the record: a link carries its source and age so a
/// surface can say them, but the game page still prints the full provenance chip — one fact
/// rendered twice in two places is two places for it to disagree.
/// </para>
/// </remarks>
public static class QuickLinks
{
    /// <summary>
    /// Which fields answer which kind, in the order the links are rendered.
    /// </summary>
    /// <remarks>
    /// A list per kind, needed only for <see cref="LinkKind.Email"/>: <c>CONTACT</c> is asked first,
    /// <c>EMAIL</c> only where <c>CONTACT</c> holds nothing dialable. The fallback runs across
    /// fields and never within one — falling back inside a field would put one value beside the
    /// title and a different one in the list below.
    /// </remarks>
    private static readonly (LinkKind Kind, string[] Fields)[] Sources =
    [
        (LinkKind.Website, ["WEBSITE"]),
        (LinkKind.Wiki, ["WIKI"]),
        (LinkKind.Forum, ["FORUM"]),
        (LinkKind.Discord, ["DISCORD"]),
        (LinkKind.Telegram, ["TELEGRAM"]),
        (LinkKind.Mastodon, ["MASTODON"]),
        (LinkKind.Bluesky, ["BLUESKY"]),
        (LinkKind.X, ["X"]),
        (LinkKind.Email, ["CONTACT", "EMAIL"]),
    ];

    /// <summary>Every field name a link can be built from, for a caller that has to ask in SQL.</summary>
    public static IReadOnlyList<string> Fields { get; } = [.. Sources.SelectMany(source => source.Fields)];

    /// <summary>
    /// The links for one game, in render order.
    /// </summary>
    /// <param name="rows">Every stored field for one game, of every source.</param>
    /// <param name="registry">Which fields hold an address, and when a value has aged out.</param>
    /// <param name="now">For the staleness question, which is asked per field and not globally.</param>
    public static IReadOnlyList<QuickLink> From(
        IEnumerable<GameField> rows,
        IFieldRegistry registry,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(registry);

        var byField = rows
            .Where(row => row.Value.Length > 0)
            .GroupBy(row => row.Field, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.AsEnumerable(), StringComparer.OrdinalIgnoreCase);

        var links = new List<QuickLink>(Sources.Length);

        foreach (var (kind, fields) in Sources)
        {
            foreach (var field in fields)
            {
                if (!byField.TryGetValue(field, out var candidates)
                    || FieldPrecedence.Winner(candidates) is not { } winner)
                {
                    continue;
                }

                var shape = registry.Find(winner.Field)?.Shape ?? FieldShape.Text;

                if (ExternalUrl.Normalise(winner.Value, shape) is not { } href)
                {
                    continue;
                }

                links.Add(new QuickLink(
                    kind,
                    winner.Field,
                    href,
                    winner.Value,
                    winner.Source,
                    winner.LastConfirmedAt,
                    registry.IsStale(winner.Field, winner.LastConfirmedAt, now)));

                break;
            }
        }

        return links;
    }
}
