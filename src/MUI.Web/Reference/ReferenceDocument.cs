using MUI.Catalog;

namespace MUI.Web.Reference;

/// <summary>
/// What a reference page is about, which decides both its URL and what the shell puts around it.
/// </summary>
/// <remarks>
/// The four kinds are not four templates. A codebase page and a protocol page carry the same prose
/// and differ only in which measurement is joined to it — games in the family, or games observed
/// offering the option — so the kind is data rather than a component per section.
/// </remarks>
public enum ReferenceKind
{
    /// <summary>MUSH vs MUD vs MUCK vs MOO, and "you want collaborative RP → start here".</summary>
    Orientation,

    Codebase,

    Client,

    Protocol,
}

/// <summary>
/// One hand-written claim about a client, and where we read it.
/// </summary>
/// <remarks>
/// <para>
/// A client is not something this site can probe. There is no handshake to observe, so every cell of
/// a client matrix is somebody's reading of somebody else's documentation — which makes the source
/// the load-bearing part of the record, not a footnote on it.
/// </para>
/// <para>
/// <b>An unsourced claim is <see cref="CapabilityState.Unknown"/>, and the type is what makes that
/// true rather than a rule an author is asked to keep.</b> <see cref="Read"/> drops any state that
/// arrives without a source, in both directions: "this client does not do MXP" is as much a claim as
/// the opposite one, and asserting an absence from silence is the exact defect this project spent a
/// week removing from its heatmap. A short honest matrix is the goal; a long one is not.
/// </para>
/// </remarks>
public sealed record CapabilityClaim(string Name, CapabilityState State, string? Source)
{
    /// <summary>
    /// A claim as an author wrote it, with an unsourced assertion demoted to unknown.
    /// </summary>
    /// <remarks>
    /// It demotes rather than throwing because the alternative is a site that will not boot over an
    /// editorial slip, and because the demoted cell is <em>correct</em>: we do not know it. The
    /// author's mistake is visible on the page as a dash, and a test over the shipped content
    /// catches it before it gets there.
    /// </remarks>
    public static CapabilityClaim Read(string name, string? state, string? source)
    {
        var cited = string.IsNullOrWhiteSpace(source) ? null : source.Trim();

        return new CapabilityClaim(name.Trim(), cited is null ? CapabilityState.Unknown : Parse(state), cited);
    }

    /// <summary>Whether this cell asserts anything at all. Unknown asserts nothing.</summary>
    public bool IsClaimed => State is not CapabilityState.Unknown;

    private static CapabilityState Parse(string? state) => state?.Trim().ToLowerInvariant() switch
    {
        "yes" or "true" => CapabilityState.Present,
        "no" or "false" => CapabilityState.Absent,

        // Anything else, including the empty string and a typo, is the state that claims nothing.
        _ => CapabilityState.Unknown,
    };
}

/// <summary>
/// One reference page: prose we wrote, plus the handful of structured facts the shell needs to join
/// a measurement to it.
/// </summary>
/// <remarks>
/// <para>
/// <b>No count lives here.</b> The prose says what a codebase is; how many games run it comes from
/// <see cref="IGameQueries"/> on every request. That split is the whole point of the section — a
/// codebase page whose "47 games" is a number somebody typed is the failure mode, and it is
/// indistinguishable on the page from the honest version, which is why it has to be impossible
/// rather than discouraged.
/// </para>
/// <para>
/// The structured half is deliberately tiny: what this page is about, what to match it against, and
/// for clients the matrix, which cannot be prose because "unknown never renders as no" has to be
/// enforced by something.
/// </para>
/// </remarks>
public sealed record ReferenceDocument
{
    public required ReferenceKind Kind { get; init; }

    public required string Slug { get; init; }

    public required string Title { get; init; }

    /// <summary>One line, for the index. Not the first paragraph — a list of first paragraphs reads badly.</summary>
    public required string Summary { get; init; }

    /// <summary>The project's own page, where there is one. Never a mirror and never a wiki about it.</summary>
    public string? Home { get; init; }

    /// <summary>
    /// The codebase family this page is about, matched by <see cref="CodebaseFamily"/>. Set on
    /// codebase pages; the count and the listing link are both derived from it, so they are one fact.
    /// </summary>
    public string? Codebase { get; init; }

    /// <summary>
    /// The protocol this page is about, spelled as <see cref="MUI.Catalog.Persistence.CapabilityFields"/>
    /// spells it — because the adoption figures are a count of <c>capability.*.measured</c> rows and a
    /// page naming the option any other way would silently report zero.
    /// </summary>
    public string? Protocol { get; init; }

    public IReadOnlyList<string> Platforms { get; init; } = [];

    /// <summary>Hand-written, sourced, and unknown wherever it is not. Client pages only.</summary>
    public IReadOnlyList<CapabilityClaim> Capabilities { get; init; } = [];

    /// <summary>Slugs of other reference pages, in the form <c>kind/slug</c>.</summary>
    public IReadOnlyList<string> SeeAlso { get; init; } = [];

    /// <summary>The prose, as written. Rendered per request; never stored as HTML.</summary>
    public required string Body { get; init; }

    /// <summary>Where this page lives. One spelling, so a link and a route cannot drift apart.</summary>
    public string Path => Kind switch
    {
        ReferenceKind.Codebase => $"/reference/codebases/{Slug}",
        ReferenceKind.Client => $"/reference/clients/{Slug}",
        ReferenceKind.Protocol => $"/reference/protocols/{Slug}",
        _ => $"/reference/{Slug}",
    };

    /// <summary>The listing of games running this codebase — the same filter the count is taken over.</summary>
    public string? GamesPath => Codebase is { } family
        ? $"/games?codebase-family={Uri.EscapeDataString(family)}"
        : Protocol is { } protocol
            ? $"/games?protocol={Uri.EscapeDataString(protocol)}"
            : null;
}
