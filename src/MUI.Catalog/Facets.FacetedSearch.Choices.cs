namespace MUI.Catalog;

/// <summary>
/// The declarative half of <see cref="FacetedSearch"/>: <see cref="ChoiceFacet"/>, the shape one
/// choice facet is described in, and <see cref="Choices"/>, every choice facet in the order the
/// panel shows them.
/// </summary>
public static partial class FacetedSearch
{
    /// <summary>
    /// One choice facet: which of its values a game is in, and what the filter says about it.
    /// </summary>
    /// <remarks>
    /// <see cref="TokensOf"/> returns a list because values can nest — reached an hour ago is in
    /// both "last 24 hours" and "last 7 days". Every other facet returns a single token, or null.
    /// </remarks>
    private sealed record ChoiceFacet(
        string Key,
        FacetEvidence Evidence,
        Func<GameFacetRow, IReadOnlyList<string?>> TokensOf,
        Func<GameFilter, FacetChoice?> SelectionOf,
        IReadOnlyList<string>? Bounded = null);

    /// <summary>
    /// Every choice facet, in the order the panel shows them: what we measured, then the one thing we
    /// concluded, then what the game says about itself. The order is editorial and the labelling is
    /// not — a reader has to be able to see which part of the panel is evidence.
    /// </summary>
    /// <remarks>
    /// <see cref="FacetKeys.Lineage"/> sits ahead of the declared half rather than the end of the
    /// measured one because it's a conclusion drawn from the codebase string beneath it.
    /// <see cref="FacetKeys.Uncounted"/> and <see cref="FacetKeys.Unreachable"/> are two separate
    /// facets, not two values of one, because a game can be either, both, or neither.
    /// </remarks>
    private static readonly ChoiceFacet[] Choices =
    [
        new(
            FacetKeys.Band,
            FacetEvidence.Measured,
            r => [FacetTokens.Of(r.Band)],
            f => f.Band is { } band ? FacetChoice.Of(FacetTokens.Of(band)) : null,
            FacetTokens.Bands),
        new(
            FacetKeys.LastSeen,
            FacetEvidence.Measured,
            r => FacetTokens.Reaching(r.LastSeen),
            f => f.LastSeen is { } seen ? FacetChoice.Of(FacetTokens.Of(seen)) : null,
            FacetTokens.LastSeenBands),
        new(
            FacetKeys.Uncounted,
            FacetEvidence.Measured,
            r => [r.Uncounted ? FacetTokens.Yes : null],
            f => f.Uncounted,
            FacetTokens.YesOnly),
        new(
            FacetKeys.Unreachable,
            FacetEvidence.Measured,
            r => [r.Unreachable ? FacetTokens.Yes : null],
            f => f.Unreachable,
            FacetTokens.YesOnly),
        new(FacetKeys.Charset, FacetEvidence.Measured, r => [r.Charset], f => f.Charset),
        new(
            FacetKeys.Lineage,
            FacetEvidence.Derived,
            r => [CodebaseLineage.Of(r.Codebase)],
            f => f.Lineage,
            CodebaseLineage.All),
        new(
            FacetKeys.Codebase,
            FacetEvidence.Declared,
            r => [CodebaseFamily.For(r.Codebase)],
            f => f.Codebase),
        new(
            FacetKeys.CodebaseVersion,
            FacetEvidence.Declared,
            r => [r.Codebase],
            f => f.CodebaseVersion),
        new(FacetKeys.Family, FacetEvidence.Declared, r => [r.Family], f => f.Family),
        new(
            FacetKeys.Trending,
            FacetEvidence.Derived,
            r => [r.Growth is { } growth ? FacetTokens.Of(growth) : null],
            f => f.Trending,
            FacetTokens.GrowthDirections),
        new(FacetKeys.Genre, FacetEvidence.Declared, r => [r.Genre], f => f.Genre),
        new(FacetKeys.Language, FacetEvidence.Declared, r => [r.Language], f => f.Language),
    ];
}
