using System.Collections.Frozen;
using System.Reflection;

using MUI.Web.Localization;

namespace MUI.Web.Reference;

/// <summary>
/// The hand-written reference section, read once from Markdown files embedded in this assembly.
/// </summary>
/// <remarks>
/// Markdown rather than Razor, per spec §9: prose reviews cleanly as a diff and stays editable by a
/// non-programmer, which a component's escaped string literals do not. Embedded rather than read from
/// disk, so content ships with the binary and can't go missing in one deployment. No count is loaded
/// from here — every number comes from <see cref="MUI.Catalog.IGameQueries"/> at request time.
/// </remarks>
public sealed class ReferenceLibrary
{
    private readonly FrozenDictionary<string, ReferenceDocument> _byPath;

    private ReferenceLibrary(IReadOnlyList<ReferenceDocument> documents)
    {
        Documents = documents;
        _byPath = documents.ToFrozenDictionary(d => d.Path, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Every page, in title order within kind.</summary>
    public IReadOnlyList<ReferenceDocument> Documents { get; }

    /// <summary>
    /// The library as shipped. Built once: parsing a dozen small files per request would be a waste,
    /// and the content cannot change while the process is running.
    /// </summary>
    public static ReferenceLibrary Shipped { get; } = Load(typeof(ReferenceLibrary).Assembly);

    /// <summary>
    /// The library a reader in this locale gets: their language where we have it, English where we
    /// do not.
    /// </summary>
    /// <remarks>
    /// A translation supplies only title, summary and body; slug, kind, see-also graph, protocol and
    /// upstream link always come from the English record, by construction. An untranslated article is
    /// served in English rather than withheld — a page in the wrong language still answers the question.
    /// </remarks>
    public static ReferenceLibrary For(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        return Localized.GetOrAdd(tag, static t =>
        {
            if (string.Equals(t, Locales.SourceTag, StringComparison.OrdinalIgnoreCase))
            {
                return Shipped;
            }

            var translated = Load(typeof(ReferenceLibrary).Assembly, t)
                .Documents.ToDictionary(d => d.Path, StringComparer.OrdinalIgnoreCase);

            return new ReferenceLibrary(
            [
                .. Shipped.Documents.Select(english =>
                    translated.TryGetValue(english.Path, out var localized)
                        ? english with
                        {
                            Title = localized.Title,
                            Summary = localized.Summary,
                            Body = localized.Body,
                        }
                        : english),
            ]);
        });
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ReferenceLibrary> Localized =
        new(StringComparer.OrdinalIgnoreCase);

    public static ReferenceLibrary Load(Assembly assembly) => Load(assembly, tag: null);

    /// <param name="tag">
    /// The locale directory to read, or null for the English articles at the top level. The two are
    /// told apart by the resource name: a translation carries its tag as a segment before the file
    /// name, so <c>…reference.de.protocol-mssp.md</c> is German and <c>…reference.protocol-mssp.md</c>
    /// is the source.
    /// </param>
    public static ReferenceLibrary Load(Assembly assembly, string? tag)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        // MSBuild replaces characters that can't appear in a manifest identifier, so
        // `content/reference/zh-Hans/` embeds as `…reference.zh_Hans.…` — the dash must be
        // translated here or that locale silently loads zero articles.
        var prefix = tag is null ? ".reference." : $".reference.{tag.Replace('-', '_')}.";

        var documents = new List<ReferenceDocument>();

        foreach (var resource in assembly.GetManifestResourceNames()
            .Where(n => n.Contains(prefix, StringComparison.OrdinalIgnoreCase)
                && n.EndsWith(".md", StringComparison.Ordinal)
                && (tag is not null || !IsLocalized(n)))
            .Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"{resource} is listed but could not be opened.");
            using var reader = new StreamReader(stream);

            documents.Add(ReferenceFrontMatter.Read(resource, reader.ReadToEnd()));
        }

        var duplicate = documents
            .GroupBy(d => d.Path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Two reference pages claim {duplicate.Key}. A slug is a URL and a URL has one page.");
        }

        return new ReferenceLibrary([.. documents.OrderBy(d => d.Kind).ThenBy(d => d.Title, StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>
    /// Whether a resource name names a translation rather than a source article: file names never
    /// carry dots, so a translation's extra tag segment is what shows up as one.
    /// </summary>
    private static bool IsLocalized(string resource)
    {
        const string Marker = ".reference.";

        var start = resource.IndexOf(Marker, StringComparison.Ordinal) + Marker.Length;
        var rest = resource[start..^".md".Length];

        return rest.Contains('.', StringComparison.Ordinal);
    }

    public IReadOnlyList<ReferenceDocument> OfKind(ReferenceKind kind) =>
        [.. Documents.Where(d => d.Kind == kind)];

    /// <summary>The page at a path, or null. Missing is a 404 and never an empty page.</summary>
    public ReferenceDocument? Find(string path) => _byPath.GetValueOrDefault(path);

    public ReferenceDocument? Find(ReferenceKind kind, string slug) =>
        Documents.FirstOrDefault(d => d.Kind == kind
            && string.Equals(d.Slug, slug, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The pages a <c>see-also</c> names, in the order they were named. A reference that names
    /// nothing we ship is dropped rather than rendered as a dead link — the content test is what
    /// catches the typo, and a reader should not be the one to find it.
    /// </summary>
    public IReadOnlyList<ReferenceDocument> Related(ReferenceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return [.. document.SeeAlso.Select(r => Find("/reference/" + r.TrimStart('/'))).OfType<ReferenceDocument>()];
    }
}
