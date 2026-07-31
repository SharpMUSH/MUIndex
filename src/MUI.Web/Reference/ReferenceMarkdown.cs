using System.Text;

using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

using MUI.Web.Components;

namespace MUI.Web.Reference;

/// <summary>
/// Markdown to HTML, and Markdown to the plain surface.
/// </summary>
/// <remarks>
/// <para>
/// <b>The pipeline is the "no external hosts" guarantee, not a coding convention.</b> Raw HTML is
/// disabled, so a content file cannot carry a <c>&lt;script src&gt;</c>, an iframe or a remote font
/// even by accident; and an image — the one remaining piece of syntax that makes a browser fetch
/// from somewhere else — is rewritten into an ordinary link to the same URL. A reader still gets to
/// the picture; the page never reaches for it. Both are properties of the parser, so they hold for
/// content nobody re-reviewed as well as for content somebody did.
/// </para>
/// <para>
/// Headings are shifted down one level on the way out. The page's <c>h1</c> is the document title,
/// which the shell renders, so a <c>#</c> in the body would produce a second first-level heading and
/// break the outline every screen reader navigates by.
/// </para>
/// </remarks>
public static class ReferenceMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseAutoLinks()
        .UsePipeTables()
        .Build();

    public static string ToHtml(string markdown)
    {
        var document = Markdown.Parse(markdown ?? string.Empty, Pipeline);

        foreach (var image in document.Descendants<LinkInline>().Where(l => l.IsImage))
        {
            // Kept as a link rather than deleted: the author meant to point at something, and
            // silently dropping it would lose the reference as well as the fetch.
            image.IsImage = false;
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
    /// <para>
    /// This walks the document rather than calling Markdig's own <c>ToPlainText</c>, and the reason
    /// is the rule the plain surface exists to enforce. <c>ToPlainText</c> collapses every block to
    /// one unwrapped line each: a heading, a paragraph and a bullet come out identical, a `telnet
    /// host port` example lands mid-sentence, and every line inherits whatever width the author's
    /// editor happened to wrap the source at. That is not a plain rendering of the page — it is the
    /// page with its structure removed, which is the failure this surface is the test for.
    /// </para>
    /// <para>
    /// Inline links render as their text. A site-internal target is appended in brackets, because in
    /// plain text a path is something a reader can act on and it is short; an external URL is not
    /// appended, because a long one cannot be wrapped and would break the eighty-column rule for a
    /// reference the page's own header and citation list already carry.
    /// </para>
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
    /// Done by wrapping to the continuation indent and then overwriting its first line's leading
    /// spaces, rather than by wrapping twice — text wrapped at eighty and then re-wrapped with two
    /// more columns of indent comes out ragged, because every line that was exactly full breaks
    /// again on its last word.
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
