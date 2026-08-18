using System.Text;

using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

using MUI.Web.Components;
using MUI.Web.Localization;

namespace MUI.Web.Reference;

/// <summary>
/// Markdown to HTML, and Markdown to the plain surface.
/// </summary>
/// <remarks>
/// Raw HTML is disabled and images are rewritten to plain links, so a content file can never make the
/// page fetch from a third-party host — this holds for the parser regardless of review. Headings are
/// shifted down one level, since the shell already renders the document title as <c>h1</c>.
/// </remarks>
public static class ReferenceMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseAutoLinks()
        .UsePipeTables()
        .Build();

    /// <param name="tag">
    /// The locale this article is being read in, which its own cross-references are written in.
    /// </param>
    /// <remarks>
    /// Links are localized here, on the parsed document, rather than on the rendered HTML (a rewrite
    /// over markup would need a second HTML parser) — the body reaches the page as one opaque
    /// <c>MarkupString</c>, so nothing downstream can see the anchors to localize them later.
    /// </remarks>
    public static string ToHtml(string markdown, string tag = Locales.SourceTag)
    {
        var document = Markdown.Parse(markdown ?? string.Empty, Pipeline);

        foreach (var image in document.Descendants<LinkInline>().Where(l => l.IsImage))
        {
            // Kept as a link, not deleted: the reference survives even though the fetch does not.
            image.IsImage = false;
        }

        foreach (var link in document.Descendants<LinkInline>())
        {
            link.Url = LocaleRouting.Link(tag, link.Url);
        }

        foreach (var heading in document.Descendants<HeadingBlock>())
        {
            heading.Level = Math.Min(heading.Level + 1, 6);
        }

        return document.ToHtml(Pipeline);
    }

    /// <summary>
    /// The prose as text, wrapped to eighty columns, with headings, lists and literal blocks still
    /// telling themselves apart.
    /// </summary>
    /// <remarks>
    /// Walks the document rather than calling Markdig's own <c>ToPlainText</c>, which collapses every
    /// block — heading, paragraph, bullet, code line — to one unwrapped line, losing structure rather
    /// than rendering it plainly. Internal links are appended in brackets since a reader can act on a
    /// short path; external URLs are not, since they can't be wrapped to eighty columns.
    /// </remarks>
    public static string ToPlainText(string markdown)
    {
        var document = Markdown.Parse(markdown ?? string.Empty, Pipeline);
        var b = new StringBuilder();

        Blocks(b, document, indent: string.Empty);

        return b.ToString().TrimEnd();
    }

    private static void Blocks(StringBuilder b, ContainerBlock container, string indent)
    {
        foreach (var block in container)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    // One blank line before a heading, never two: the block above already left one,
                    // and a second reads as a section break that is not there.
                    if (b.Length > 0 && !b.ToString().EndsWith("\n\n", StringComparison.Ordinal))
                    {
                        b.AppendLine();
                    }

                    PlainText.Wrap(b, Text(heading.Inline).ToUpperInvariant(), indent);
                    b.AppendLine();
                    break;

                case ParagraphBlock paragraph:
                    PlainText.Wrap(b, Text(paragraph.Inline), indent);
                    b.AppendLine();
                    break;

                case ListBlock list:
                    Items(b, list, indent);
                    break;

                case QuoteBlock quote:
                    Blocks(b, quote, indent + "  ");
                    break;

                // A literal block is literal: it is an address to type or a line off a wire, and
                // wrapping it would produce something that does not work when pasted.
                case CodeBlock code:
                    foreach (var line in code.Lines.Lines.Take(code.Lines.Count))
                    {
                        b.Append(indent).Append("    ").AppendLine(line.ToString());
                    }

                    b.AppendLine();
                    break;

                case ContainerBlock nested:
                    Blocks(b, nested, indent);
                    break;
            }
        }
    }

    /// <summary>Items keep their marker and hang under it, so a list is still a list.</summary>
    private static void Items(StringBuilder b, ListBlock list, string indent)
    {
        var number = 1;

        foreach (var item in list.OfType<ListItemBlock>())
        {
            var marker = list.IsOrdered ? $"{number++}. " : "- ";
            var hanging = new string(' ', marker.Length);
            var first = true;

            foreach (var block in item)
            {
                if (block is ParagraphBlock paragraph)
                {
                    Hanging(b, Text(paragraph.Inline), indent, first ? marker : hanging, hanging);
                    first = false;
                    continue;
                }

                Blocks(b, item, indent + hanging);
                break;
            }
        }

        b.AppendLine();
    }

    /// <summary>
    /// Wrapped text whose first line carries the marker and whose continuations line up under it.
    /// </summary>
    /// <remarks>
    /// Wraps once to the continuation indent and overwrites the first line's leading spaces, rather
    /// than wrapping twice — a re-wrap at a different indent breaks full lines again on their last word.
    /// </remarks>
    private static void Hanging(StringBuilder b, string text, string indent, string marker, string hanging)
    {
        var buffer = new StringBuilder();
        PlainText.Wrap(buffer, text, indent + hanging);

        b.Append(indent).Append(marker).Append(buffer.ToString().AsSpan(indent.Length + hanging.Length));
    }

    private static string Text(ContainerInline? inline)
    {
        var b = new StringBuilder();
        Inlines(b, inline);
        return b.ToString();
    }

    private static void Inlines(StringBuilder b, ContainerInline? container)
    {
        if (container is null)
        {
            return;
        }

        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    b.Append(literal.Content.ToString());
                    break;

                case CodeInline code:
                    b.Append(code.Content);
                    break;

                case LineBreakInline:
                    b.Append(' ');
                    break;

                case AutolinkInline auto:
                    b.Append(auto.Url);
                    break;

                case LinkInline link:
                    Inlines(b, link);

                    if (link.Url is { Length: > 0 } url && url[0] == '/')
                    {
                        b.Append(" [").Append(url).Append(']');
                    }

                    break;

                case ContainerInline nested:
                    Inlines(b, nested);
                    break;
            }
        }
    }
}
