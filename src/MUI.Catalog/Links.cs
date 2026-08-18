namespace MUI.Catalog;

/// <summary>
/// A way to reach a game's people, and which of them it is.
/// </summary>
/// <remarks>
/// <para>
/// A kind rather than a field name, because two field names can answer one kind: an email address
/// arrives as MSSP's official <c>CONTACT</c> and also as the unofficial <c>EMAIL</c> that fourteen
/// games in this catalogue publish, and a reader wants one envelope beside the title either way.
/// </para>
/// <para>
/// Everything past <see cref="Discord"/> exists because MSSP has no variable for it. The protocol
/// added <c>DISCORD</c> and stopped there, so a game with a wiki, a forum or a fediverse account has
/// nowhere to say so — which is why those kinds are answered only by <see cref="FieldSource.Owner"/>
/// rows, written by somebody who proved the game is theirs (spec §8.5).
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
/// <see cref="Href"/> is normalised and never the stored value: it has been through
/// <see cref="ExternalUrl"/>, so a surface rendering it into an <c>href</c> does not have to decide
/// whether a stranger's <c>javascript:</c> is safe. The row it came from is untouched and still
/// prints verbatim under "declared by the game" — we refuse to link a value, never to show it.
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
    /// Always false in practice — every field behind a link is hand-typed — and derived rather than
    /// hard-coded anyway, because the one spelling of the measured/declared line is
    /// <see cref="FieldSources"/> and a second one here would be a second thing to keep in step.
    /// </remarks>
    public bool IsMeasured => FieldSources.IsMeasured(Source);
}

/// <summary>
/// Whether a value somebody else typed can be put in an <c>href</c>, and what it looks like when it
/// can.
/// </summary>
/// <remarks>
/// <para>
/// <b>The input is hostile by default.</b> A hundred and thirty-five of the <c>WEBSITE</c> values in
/// this catalogue came off strangers' sockets, and an owner's box is a text field on the public
/// internet. <c>javascript:</c> in an <c>href</c> is script execution on our origin, and Blazor's
/// encoding does not stop it — it encodes the text of an attribute, not the meaning of its scheme.
/// </para>
/// <para>
/// <b>It refuses rather than repairs.</b> Three games in this catalogue publish a <c>WEBSITE</c> with
/// no scheme — <c>www.slothmud.org</c> — and prepending one would be us guessing whether their server
/// answers on TLS and publishing the guess as their address. The value still renders as text where
/// every other declared field does, and the MSSP scorecard on their own page names the missing prefix
/// in the operator's terms. A link we invented that 404s is worse than a line of text that is exactly
/// what they wrote.
/// </para>
/// </remarks>
public static class ExternalUrl
{
    /// <summary>
    /// The longest destination we will store or render.
    /// </summary>
    /// <remarks>
    /// The same bound the enrichment form applies to everything else, restated as a constant here so
    /// the render path does not depend on the write path having run — MSSP rows never went through
    /// that form.
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

            // A CONTACT is an email address in the specification and a contact page in practice —
            // one game in this catalogue publishes an https:// form there. Both are ways to reach
            // the same people, and refusing the second on a technicality would drop a working
            // address to enforce a distinction the reader does not have.
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

        // http and https and nothing else. `javascript:` and `data:` are script; `file:` and
        // `ftp:` point at the reader's own machine or at a protocol no browser here still dials.
        // Compared rather than pattern-matched: Uri.UriSchemeHttp is a static field and not a
        // constant, so it cannot appear in a pattern. Uri lower-cases the scheme it parsed, so an
        // ordinal comparison is the whole check.
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

        // Deliberately not a validator. Six of the CONTACT values here are obfuscated against
        // harvesters — "msocorcim (at) gmail (dot) com" — and the point is only to tell an address
        // apart from a spelling of one, so that the second renders as the text it is rather than as
        // a mailto nobody's client can dial.
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
/// <b>Derived on read, like every other winner on this site.</b> Nothing is stored: an owner's
/// <c>WEBSITE</c> and their game's own report are two rows keyed <c>(game, field, source)</c>, and
/// which of them is a link is <see cref="FieldPrecedence"/>'s answer rather than a column somebody
/// has to keep in step.
/// </para>
/// <para>
/// <b>The icons are navigation and the chips are the record.</b> A link carries its source and age so
/// a surface can say them, but the game page keeps printing the full provenance chip under "declared
/// by the game" — nine chips beside a title is not a page, and one fact rendered twice in two places
/// is two places for it to disagree.
/// </para>
/// </remarks>
public static class QuickLinks
{
    /// <summary>
    /// Which fields answer which kind, in the order the links are rendered.
    /// </summary>
    /// <remarks>
    /// A list per kind rather than a field per kind, for <see cref="LinkKind.Email"/> alone: MSSP's
    /// <c>CONTACT</c> is asked first because it is the official variable, and the unofficial
    /// <c>EMAIL</c> answers only where <c>CONTACT</c> holds nothing we could dial. The fallback runs
    /// across fields and never within one — an owner's row beats their game's report and that is the
    /// end of it, because falling back inside a field would put one value beside the title and a
    /// different one in the list below.
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
