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
    /// </remarks>
    public const string ClientMatrixCaveat =
        "Read off each project's own documentation, not measured by us — a client has no handshake "
        + "for us to observe. \"unknown\" means we looked and did not establish it. It never means no.";

    /// <summary>
    /// The sentence a protocol page's remainder needs, on both surfaces.
    /// </summary>
    public const string ProtocolRemainderCaveat =
        "The games not counted here are not games without the protocol. A game is counted when we "
        + "observed its server offering the option in a handshake; the rest are servers that did not "
        + "offer it to us and servers whose handshake we have not read, and we cannot tell you which.";

    public static string Render(
        ReferenceDocument document,
        CodebaseFigures? codebase = null,
        ProtocolFigures? protocol = null,
        IReadOnlyList<ReferenceDocument>? related = null,
        string tag = Locales.SourceTag)
    {
        ArgumentNullException.ThrowIfNull(document);

        var b = new StringBuilder();

        b.AppendLine($"{document.Title.ToUpperInvariant()}  [{Kind(document.Kind)}]");
        PlainText.Wrap(b, document.Summary);

        if (document.Home is { } home)
        {
            b.AppendLine(home);
        }

        if (document.Platforms.Count > 0)
        {
            b.AppendLine($"Runs on: {string.Join(", ", document.Platforms)}");
        }

        if (codebase is not null)
        {
            AppendCodebase(b, document, codebase, tag);
        }

        if (protocol is not null)
        {
            AppendProtocol(b, document, protocol, tag);
        }

        if (document.Kind is ReferenceKind.Client)
        {
            AppendClientMatrix(b, document);
        }

        b.AppendLine();
        b.AppendLine(ReferenceMarkdown.ToPlainText(document.Body));

        if (related is { Count: > 0 })
        {
            b.AppendLine();
            b.AppendLine("See also");
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
        StringBuilder b, ReferenceDocument document, CodebaseFigures figures, string tag)
    {
        b.AppendLine();
        b.AppendLine("Games we have identified as running this codebase");

        if (figures.Known == 0)
        {
            PlainText.Wrap(b, "None yet. That is a statement about what we have measured, not about "
                + "what exists — a game we have not reached, or whose codebase we could not read, is "
                + "not counted here.", "  ");
            return;
        }

        b.AppendLine($"  {figures.Listed} listed, {figures.Archived} archived");

        if (document.GamesPath is { } path)
        {
            b.AppendLine($"  {LocaleRouting.Link(tag, path)}");
        }

        b.AppendLine(figures.MeasuredProtocols.Count > 0
            ? $"  Measured in their handshakes: {string.Join(", ", figures.MeasuredProtocols)}"
            : "  Nothing was offered in any handshake we have read from them.");
    }

    private static void AppendProtocol(
        StringBuilder b, ReferenceDocument document, ProtocolFigures figures, string tag)
    {
        b.AppendLine();
        b.AppendLine("Measured adoption");

        if (figures.Listed == 0)
        {
            b.AppendLine("  Nothing measured yet.");
            return;
        }

        b.AppendLine($"  {figures.Offering} of {figures.Listed} listed games were observed offering it "
            + $"({Wording.Percent(figures.Share ?? 0)})");

        if (document.GamesPath is { } path)
        {
            b.AppendLine($"  {LocaleRouting.Link(tag, path)}");
        }

        b.AppendLine();
        PlainText.Wrap(b, ProtocolRemainderCaveat, "  ");

        var rows = figures.ByCodebase.Where(r => r.IsMeasured).ToList();

        if (rows.Count == 0)
        {
            return;
        }

        b.AppendLine();
        b.AppendLine("  By codebase, of the games we identified");
        foreach (var row in rows)
        {
            b.AppendLine($"    {row.Codebase,-20} {row.Offering,4} of {row.Identified,-4} offered it");
        }
    }

    private static void AppendClientMatrix(StringBuilder b, ReferenceDocument document)
    {
        b.AppendLine();
        b.AppendLine("Capabilities");
        PlainText.Wrap(b, ClientMatrixCaveat, "  ");
        b.AppendLine();

        var claims = ClientCapabilities.For(document);

        foreach (var claim in claims)
        {
            b.AppendLine($"  {claim.Name,-16} {ClientCapabilities.Word(claim.State)}");

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
            PlainText.Wrap(b, $"{unknown} of {claims.Count} rows are unknown: we did not find the "
                + "project's own documentation saying either way. A short honest table beats a long "
                + "guessed one.", "  ");
        }
    }

    /// <summary>The index, which is the only page in the section that is a list of the others.</summary>
    public static string RenderIndex(ReferenceLibrary library, string tag = Locales.SourceTag)
    {
        ArgumentNullException.ThrowIfNull(library);

        var b = new StringBuilder();

        b.AppendLine("REFERENCE");
        PlainText.Wrap(b, "Hand-written, single-author, and versioned in git. The prose here is ours; "
            + "every number beside it was measured by the crawler and is recomputed on each request. "
            + "This is not a wiki, and there is no way to edit it from this page.");

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
            b.AppendLine(Heading(kind).ToUpperInvariant());

            foreach (var document in documents)
            {
                b.AppendLine($"  {document.Title}");
                b.AppendLine($"    {LocaleRouting.Link(tag, document.Path)}");
                PlainText.Wrap(b, document.Summary, "    ");
            }
        }

        return b.ToString();
    }

    public static string Kind(ReferenceKind kind) => kind switch
    {
        ReferenceKind.Codebase => "codebase",
        ReferenceKind.Client => "client",
        ReferenceKind.Protocol => "protocol",
        _ => "orientation",
    };

    public static string Heading(ReferenceKind kind) => kind switch
    {
        ReferenceKind.Codebase => "Codebases",
        ReferenceKind.Client => "Clients",
        ReferenceKind.Protocol => "Protocols",
        _ => "Start here",
    };
}
