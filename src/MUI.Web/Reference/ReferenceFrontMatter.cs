namespace MUI.Web.Reference;

/// <summary>
/// The header block at the top of a reference file: <c>key: value</c>, one per line, between two
/// <c>---</c> rules.
/// </summary>
/// <remarks>
/// <para>
/// Hand-rolled, and that is a decision rather than an omission. There are eight keys, no nesting,
/// and no value that is not a string — a YAML parser would be a second dependency, a second syntax
/// for an author to get wrong, and a surface with a decade of parser CVEs behind it, all to read
/// <c>title: Mudlet</c>.
/// </para>
/// <para>
/// <b>An unknown key is an error, not a shrug.</b> The failure this guards against is a mistyped
/// <c>capabilty:</c> silently dropping a row from a matrix — a page that then reports nothing where
/// it meant to report something, which on this site is the one kind of wrong that looks right.
/// Loading happens at startup over our own embedded files, so throwing here cannot be triggered by
/// anything a reader sends, and the content test fails long before a deployment would.
/// </para>
/// </remarks>
public static class ReferenceFrontMatter
{
    private const string Rule = "---";

    public static ReferenceDocument Read(string origin, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        if (lines.Length == 0 || lines[0].Trim() != Rule)
        {
            throw new InvalidOperationException(
                $"{origin}: a reference page opens with a --- front-matter block. "
                + "Without one there is no way to know what the page is about or what to measure against it.");
        }

        var end = Array.FindIndex(lines, 1, l => l.Trim() == Rule);

        if (end < 0)
        {
            throw new InvalidOperationException($"{origin}: the front-matter block is never closed with ---.");
        }

        var fields = new List<(string Key, string Value)>();

        for (var i = 1; i < end; i++)
        {
            var line = lines[i];

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);

            if (colon <= 0)
            {
                throw new InvalidOperationException($"{origin}: '{line.Trim()}' is not a key: value line.");
            }

            fields.Add((line[..colon].Trim().ToLowerInvariant(), line[(colon + 1)..].Trim()));
        }

        string? One(string key) => fields.Where(f => f.Key == key).Select(f => f.Value)
            .FirstOrDefault(v => v.Length > 0);

        IReadOnlyList<string> Many(string key) =>
            [.. fields.Where(f => f.Key == key).Select(f => f.Value).Where(v => v.Length > 0)];

        foreach (var (key, _) in fields)
        {
            if (!Known.Contains(key))
            {
                throw new InvalidOperationException(
                    $"{origin}: '{key}' is not a front-matter key. Known keys: {string.Join(", ", Known)}.");
            }
        }

        var kind = ParseKind(origin, One("kind"));

        return new ReferenceDocument
        {
            Kind = kind,
            Slug = Required(origin, "slug", One("slug")),
            Title = Required(origin, "title", One("title")),
            Summary = Required(origin, "summary", One("summary")),
            Home = One("home"),
            Codebase = One("codebase"),
            Protocol = One("protocol"),
            Platforms = Many("platform"),
            Capabilities = [.. Many("capability").Select(v => Capability(origin, v))],
            SeeAlso = Many("see-also"),
            Body = string.Join('\n', lines[(end + 1)..]).Trim(),
        };
    }

    /// <summary>
    /// <c>capability: NAME | yes | https://source</c> — three fields, and the third is not optional
    /// in any sense that matters. A claim arriving without it is demoted to unknown by
    /// <see cref="CapabilityClaim.Read"/>, which is why the pipe with nothing after it is a legal and
    /// useful thing to write: it records that we looked and did not establish it.
    /// </summary>
    private static CapabilityClaim Capability(string origin, string value)
    {
        var parts = value.Split('|');

        if (parts.Length is < 2 or > 3)
        {
            throw new InvalidOperationException(
                $"{origin}: capability '{value}' should read 'name | yes|no|unknown | source-url'.");
        }

        return CapabilityClaim.Read(parts[0], parts[1], parts.Length == 3 ? parts[2] : null);
    }

    private static readonly string[] Known =
    [
        "kind", "slug", "title", "summary", "home", "codebase", "protocol",
        "platform", "capability", "see-also",
    ];

    private static ReferenceKind ParseKind(string origin, string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "orientation" => ReferenceKind.Orientation,
            "codebase" => ReferenceKind.Codebase,
            "client" => ReferenceKind.Client,
            "protocol" => ReferenceKind.Protocol,
            _ => throw new InvalidOperationException(
                $"{origin}: kind must be one of orientation, codebase, client, protocol."),
        };

    private static string Required(string origin, string key, string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{origin}: '{key}' is required.")
            : value;
}
