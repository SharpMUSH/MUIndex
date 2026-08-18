using System.Collections.Frozen;
using System.Reflection;

using MUI.Web.Localization;

namespace MUI.Web.Reference;

/// <summary>
/// The hand-written reference section, read once from Markdown files embedded in this assembly.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why Markdown files and not Razor pages.</b> Spec §9 wants this content curated, single-author
/// and versioned in git — that is the whole argument for having it at all, since it is how wiki
/// value is obtained without wiki governance. Governance here means <em>review</em>, and review of
/// prose only works if the diff reads as prose. A codebase description inside a component arrives in
/// a pull request as escaped string literals interleaved with layout, which nobody proof-reads; the
/// same paragraph in a <c>.md</c> file arrives as the sentence that changed. It also keeps the
/// section editable by somebody who does not write C#, which for curated content is the difference
/// between a page that gets corrected and one that goes stale.
/// </para>
/// <para>
/// The cost is one dependency and a parser, and it is paid deliberately. Razor would have avoided
/// both, at the price of putting the content and the rendering shell in one file — the one thing the
/// section may not do, because then every editorial fix recompiles the layout and every layout
/// change touches the prose.
/// </para>
/// <para>
/// <b>Embedded rather than read from disk.</b> The content ships with the binary, so there is no
/// content root to resolve, no file that can be missing in one deployment and present in another,
/// and no way for the running site to serve something that is not in the repository. "Editable by a
/// non-programmer" means editable in a pull request, which is exactly what versioned-in-git meant.
/// </para>
/// <para>
/// <b>No count is loaded from here.</b> Every number a reference page shows comes from
/// <see cref="MUI.Catalog.IGameQueries"/> on the request that renders it. The prose is ours; the
/// figures are measured, and the two never swap places.
/// </para>
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
    /// <para>
    /// <b>A translation supplies prose and nothing else.</b> The document handed out is the English
    /// record with its title, summary and body replaced — so the slug, the kind, the
    /// <c>see-also</c> graph, the protocol name and the upstream link come from one file in one
    /// language, whatever a translator does to their copy. A slug is a URL and a URL has one page;
    /// this makes that true by construction rather than by asking forty files to agree.
    /// </para>
    /// <para>
    /// An article with no translation is served in English rather than withheld. The alternative is
    /// a reference section that is missing pages in four languages, and a missing page is worse
    /// than a page in the wrong language: one of them still answers the question.
    /// </para>
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

        // MSBuild builds a manifest name out of the file's path and replaces what cannot appear in
        // an identifier, so `content/reference/zh-Hans/` is embedded as `…reference.zh_Hans.…`. The
        // tag is BCP-47 and its only such character is the dash. Without this, Chinese was the one
        // locale whose articles loaded and were never found — three locales working is exactly the
        // evidence that hides it, which is why the test below names zh-Hans rather than "a locale".
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
    /// Whether a resource name names a translation rather than a source article.
    /// </summary>
    /// <remarks>
    /// The file names carry dashes and never dots, so everything between <c>.reference.</c> and
    /// <c>.md</c> is one segment for a source article and two for a translation — and the extra
    /// segment is the tag. <c>zh-Hans</c> survives that because its dash is not a separator here.
    /// </remarks>
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
