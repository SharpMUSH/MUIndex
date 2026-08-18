using System.Text;

using MUI.Catalog;
using MUI.Web.Localization;
using MUI.Web.Reference;

namespace MUI.Web.Components;

/// <summary>
/// The reference section in plain text.
/// </summary>
/// <remarks>
/// <para>
/// Same rule as the rest of the plain surface: if a fact cannot survive here, its graphic on the
/// main page is decoration. For this section that bites hardest on the matrices — a client
/// capability table carried by a tick, a cross and a blank does not survive, and one carried by
/// <em>yes</em>, <em>no</em> and <em>unknown</em> does.
/// </para>
/// <para>
/// The prose goes through the Markdown renderer's text output and the structured half is rendered
/// here from the same records the graphical page uses, so the two surfaces cannot report different
/// counts. Nothing here re-reads the content files.
/// </para>
/// <para>
/// <b>The tag comes first, as it does on <see cref="PlainText"/>, and it reaches every sentence this
/// file writes — but never the article.</b> The Markdown body is a document somebody owns and is
/// translated as a document rather than as a string; this file renders whichever one the library
/// hands it and puts no word of its own inside it.
/// </para>
/// </remarks>
public static class ReferencePlainText
{
    /// <summary>
    /// The one sentence that keeps a client matrix honest, on both surfaces.
    /// </summary>
    /// <remarks>
    /// A client cannot be probed — there is no handshake of ours to observe — so every cell is a
    /// reading of somebody's documentation. Saying so beside the table is not modesty: this site's
    /// whole claim is that a reader can tell a measurement from an assertion, and a table that looks
    /// like the game pages' measured matrix while being neither would spend that credit.
    /// <para>
    /// The unknown word is passed in rather than written into the sentence, so the sentence and the
    /// cells it is explaining cannot end up quoting different words in the same locale.
    /// </para>
    /// </remarks>
    public static string ClientMatrixCaveat(string tag) =>
        Messages.Say(tag, "reference.capabilities.caveat",
            ("unknown", Messages.For(tag, "reference.capability.unknown")));

    /// <summary>
    /// The sentence a protocol page's remainder needs, on both surfaces.
    /// </summary>
    /// <remarks>
    /// Rule 5 in one paragraph: the games not counted are servers that did not offer it to us
    /// <em>and</em> servers whose handshake we have never read, and a locale that collapsed the two
    /// would publish our own gap as a fact about somebody's game.
    /// </remarks>
    public static string ProtocolRemainderCaveat(string tag) =>
        Messages.For(tag, "reference.protocol.remainder");

    public static string Render(
        string tag,
        ReferenceDocument document,
        CodebaseFigures? codebase = null,
        ProtocolFigures? protocol = null,
        IReadOnlyList<ReferenceDocument>? related = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var b = new StringBuilder();

        b.AppendLine($"{document.Title.ToUpperInvariant()}  [{Kind(tag, document.Kind)}]");
        PlainText.Wrap(b, document.Summary);

        if (document.Home is { } home)
        {
            b.AppendLine(home);
        }

        if (document.Platforms.Count > 0)
        {
            b.AppendLine(Messages.Say(
                tag, "reference.plain.runsOn", ("platforms", string.Join(", ", document.Platforms))));
        }

        if (codebase is not null)
        {
            AppendCodebase(b, tag, document, codebase);
        }

        if (protocol is not null)
        {
            AppendProtocol(b, tag, document, protocol);
        }

        if (document.Kind is ReferenceKind.Client)
        {
            AppendClientMatrix(b, tag, document);
        }

        b.AppendLine();
        b.AppendLine(ReferenceMarkdown.ToPlainText(document.Body));

        if (related is { Count: > 0 })
        {
            b.AppendLine();
            b.AppendLine(Messages.For(tag, "reference.seeAlso"));
            foreach (var other in related)
            {
                b.AppendLine($"  {other.Title} — {LocaleRouting.Link(tag, other.Path)}");
            }
        }

        return b.ToString();
    }

    /// <summary>
    /// The measured half of a codebase page. It says <em>we identified</em> rather than <em>there
    /// are</em>, because a game whose codebase we could not read is not a game running something
    /// else — and the difference is the whole reason the figure is worth printing.
    /// </summary>
    private static void AppendCodebase(
        StringBuilder b, string tag, ReferenceDocument document, CodebaseFigures figures)
    {
        b.AppendLine();
        b.AppendLine(Messages.For(tag, "reference.plain.codebase.heading"));

        if (figures.Known == 0)
        {
            PlainText.Wrap(b, Messages.For(tag, "reference.plain.codebase.none"), "  ");
            return;
        }

        b.AppendLine("  " + Messages.Say(
            tag,
            "reference.plain.codebase.counts",
            ("listed", figures.Listed),
            ("archived", figures.Archived)));

        if (document.GamesPath is { } path)
        {
            b.AppendLine($"  {LocaleRouting.Link(tag, path)}");
        }

        b.AppendLine("  " + (figures.MeasuredProtocols.Count > 0
            ? Messages.Say(
                tag,
                "reference.plain.codebase.offered",
                ("protocols", string.Join(", ", figures.MeasuredProtocols)))
            : Messages.For(tag, "reference.plain.codebase.nothingOffered")));
    }

    private static void AppendProtocol(
        StringBuilder b, string tag, ReferenceDocument document, ProtocolFigures figures)
    {
        b.AppendLine();
        b.AppendLine(Messages.For(tag, "reference.protocol.heading"));

        if (figures.Listed == 0)
        {
            b.AppendLine("  " + Messages.For(tag, "reference.protocol.none"));
            return;
        }

        PlainText.Wrap(b, Messages.Say(
            tag,
            "reference.plain.protocol.share",
            ("offering", figures.Offering),
            ("listed", figures.Listed),
            ("percent", Wording.Percent(figures.Share ?? 0))), "  ");

        if (document.GamesPath is { } path)
        {
            b.AppendLine($"  {LocaleRouting.Link(tag, path)}");
        }

        b.AppendLine();
        PlainText.Wrap(b, ProtocolRemainderCaveat(tag), "  ");

        var rows = figures.ByCodebase.Where(r => r.IsMeasured).ToList();

        if (rows.Count == 0)
        {
            return;
        }

        b.AppendLine();
        b.AppendLine("  " + Messages.For(tag, "reference.plain.protocol.byCodebase"));
        foreach (var row in rows)
        {
            // The family name is padded to a column and the sentence beside it is not: the numbers
            // are inside a message now, and a language that puts them in the other order has nowhere
            // to say so if this file is still counting characters into it.
            b.AppendLine($"    {row.Codebase,-20} " + Messages.Say(
                tag,
                "reference.plain.protocol.row",
                ("offering", row.Offering),
                ("identified", row.Identified)));
        }
    }

    private static void AppendClientMatrix(StringBuilder b, string tag, ReferenceDocument document)
    {
        b.AppendLine();
        b.AppendLine(Messages.For(tag, "reference.capabilities.heading"));
        PlainText.Wrap(b, ClientMatrixCaveat(tag), "  ");
        b.AppendLine();

        var claims = ClientCapabilities.For(document);

        foreach (var claim in claims)
        {
            b.AppendLine($"  {claim.Name,-16} {ClientCapabilities.Word(tag, claim.State)}");

            // The source on its own line, unwrapped. A URL broken across two lines is not a URL, and
            // this is the one place the eighty-column rule gives way to a thing being usable.
            if (claim.Source is { } source)
            {
                b.AppendLine($"      {source}");
            }
        }

        var unknown = claims.Count(c => c.State is CapabilityState.Unknown);

        if (unknown > 0)
        {
            b.AppendLine();
            PlainText.Wrap(b, Messages.Say(
                tag,
                "reference.plain.capabilities.unknown",
                ("count", unknown),
                ("total", claims.Count)), "  ");
        }
    }

    /// <summary>The index, which is the only page in the section that is a list of the others.</summary>
    public static string RenderIndex(string tag, ReferenceLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);

        var b = new StringBuilder();

        b.AppendLine(Messages.For(tag, "reference.title").ToUpperInvariant());
        PlainText.Wrap(b, Messages.For(tag, "reference.plain.lede"));

        foreach (var kind in new[]
        {
            ReferenceKind.Orientation, ReferenceKind.Codebase, ReferenceKind.Client, ReferenceKind.Protocol,
        })
        {
            var documents = library.OfKind(kind);

            if (documents.Count == 0)
            {
                continue;
            }

            b.AppendLine();
            b.AppendLine(Heading(tag, kind).ToUpperInvariant());

            foreach (var document in documents)
            {
                b.AppendLine($"  {document.Title}");
                b.AppendLine($"    {LocaleRouting.Link(tag, document.Path)}");
                PlainText.Wrap(b, document.Summary, "    ");
            }
        }

        return b.ToString();
    }

    /// <summary>What one page is — the kicker above its title, and the tag beside it in plain text.</summary>
    public static string Kind(string tag, ReferenceKind kind) => Messages.For(tag, kind switch
    {
        ReferenceKind.Codebase => "reference.kind.codebase",
        ReferenceKind.Client => "reference.kind.client",
        ReferenceKind.Protocol => "reference.kind.protocol",
        _ => "reference.kind.orientation",
    });

    /// <summary>
    /// The heading over a list of them, which is a different job from <see cref="Kind"/> and so a
    /// different id: English writes one word for both and most languages do not.
    /// </summary>
    public static string Heading(string tag, ReferenceKind kind) => Messages.For(tag, kind switch
    {
        ReferenceKind.Codebase => "reference.section.codebase",
        ReferenceKind.Client => "reference.section.client",
        ReferenceKind.Protocol => "reference.section.protocol",
        _ => "reference.section.orientation",
    });
}
