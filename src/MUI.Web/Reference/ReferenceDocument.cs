using MUI.Catalog;

namespace MUI.Web.Reference;

/// <summary>
/// What a reference page is about, which decides both its URL and what the shell puts around it.
/// </summary>
/// <remarks>
/// Not four templates: a codebase page and a protocol page carry the same prose and differ only in
/// which measurement is joined to it, so the kind is data rather than a component per section.
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
/// A client cannot be probed, so the source is the load-bearing part of the record. An unsourced
/// claim is always <see cref="CapabilityState.Unknown"/> — <see cref="Read"/> enforces this rather
/// than relying on authors to leave a claim out, in both directions ("does not support X" is as much
/// a claim as the opposite).
/// </remarks>
public sealed record CapabilityClaim(string Name, CapabilityState State, string? Source)
{
    /// <summary>
    /// A claim as an author wrote it, with an unsourced assertion demoted to unknown rather than
    /// thrown — the demoted cell is correct (we do not know it), and it renders visibly as a dash.
    /// </summary>
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
/// No count lives here: the prose says what a codebase is, but how many games run it always comes
/// from <see cref="IGameQueries"/> at request time, so a hand-typed count can never appear on the
/// page.
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
    /// spells it, since adoption figures count <c>capability.*.measured</c> rows by that name.
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
        ? $"/games?{FacetKeys.Codebase}={Uri.EscapeDataString(family)}"
        : Protocol is { } protocol
            ? $"/games?protocol={Uri.EscapeDataString(protocol)}"
            : null;
}
