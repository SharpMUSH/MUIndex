using System.Text.RegularExpressions;

namespace MUI.Web.Tests;

/// <summary>
/// What a rendered document put in its <c>&lt;head&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// The head is the one part of the site no reader sees and every other program does — a search
/// engine, a chat client unfurling a pasted link, a browser choosing a tab icon. So the assertions
/// about it are assertions about markup rather than about words, and <c>Render.Words</c> is the
/// wrong tool: it collapses exactly the attribute structure that carries the meaning.
/// </para>
/// <para>
/// Deliberately a reader and not a parser. It answers "what is the content of the meta whose name
/// or property is X" and nothing else, because a test that needed more than that would be a test
/// asserting on how the head is laid out rather than on what it says.
/// </para>
/// </remarks>
public static partial class Head
{
    /// <summary>
    /// The content of a <c>&lt;meta&gt;</c>, found by either spelling of its key.
    /// </summary>
    /// <remarks>
    /// Open Graph keys ride on <c>property</c> and Twitter's on <c>name</c>, which is not a
    /// stylistic difference — the two vocabularies were specified separately and consumers look for
    /// their own spelling. Reading both here keeps that out of every call site.
    /// </remarks>
    public static string? Meta(string document, string key)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (Match tag in MetaTag().Matches(document))
        {
            var element = tag.Value;

            if (Attribute(element, "name") == key || Attribute(element, "property") == key)
            {
                return Attribute(element, "content");
            }
        }

        return null;
    }

    /// <summary>The <c>href</c> of the first <c>&lt;link&gt;</c> carrying the given <c>rel</c>.</summary>
    public static string? Link(string document, string rel) => Links(document, rel).FirstOrDefault();

    /// <summary>Every <c>href</c> carrying the given <c>rel</c> — a document may name several icons.</summary>
    public static IReadOnlyList<string> Links(string document, string rel)
    {
        ArgumentNullException.ThrowIfNull(document);

        return
        [
            .. LinkTag().Matches(document)
                .Where(tag => Attribute(tag.Value, "rel") == rel)
                .Select(tag => Attribute(tag.Value, "href"))
                .OfType<string>(),
        ];
    }

    /// <summary>The contents of every <c>application/ld+json</c> block, in document order.</summary>
    public static IReadOnlyList<string> StructuredData(string document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return [.. LdJson().Matches(document).Select(m => m.Groups["json"].Value)];
    }

    private static string? Attribute(string element, string name)
    {
        var match = Regex.Match(
            element,
            $@"\b{name}\s*=\s*""(?<value>[^""]*)""",
            RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1));

        return match.Success ? System.Net.WebUtility.HtmlDecode(match.Groups["value"].Value) : null;
    }

    [GeneratedRegex("<meta\\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex MetaTag();

    [GeneratedRegex("<link\\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkTag();

    [GeneratedRegex(
        "<script[^>]*type=\"application/ld\\+json\"[^>]*>(?<json>.*?)</script>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LdJson();
}
