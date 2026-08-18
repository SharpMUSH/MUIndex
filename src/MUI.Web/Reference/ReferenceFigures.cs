using MUI.Catalog;

namespace MUI.Web.Reference;

/// <summary>
/// The measured half of a codebase page: how many games we have identified as running it.
/// </summary>
/// <remarks>
/// <see cref="ReadAsync"/> derives the count from the same <see cref="IGameQueries"/> filter as the
/// page's "see the games" link, so the headline number and the listing cannot disagree. Archived
/// games are counted separately rather than dropped, per spec §7.5.
/// </remarks>
public sealed record CodebaseFigures(int Listed, int Archived, IReadOnlyList<string> MeasuredProtocols)
{
    public static readonly CodebaseFigures None = new(0, 0, []);

    public int Known => Listed + Archived;

    public static async Task<CodebaseFigures> ReadAsync(
        IGameQueries queries,
        string family,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queries);

        var games = await queries.ListAsync(
            new GameFilter { Codebase = FacetChoice.Of(family), IncludeArchived = true },
            cancellationToken);

        return new CodebaseFigures(
            games.Count(g => g.State is not LifecycleState.Archived),
            games.Count(g => g.State is LifecycleState.Archived),

            // Observed on the wire, most common first — not the codebase's documented capabilities.
            [.. games
                .SelectMany(g => g.MeasuredProtocols)
                .GroupBy(p => p, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => g.Key)]);
    }
}

/// <summary>
/// The measured half of a protocol page: who was observed offering it, in the handshake.
/// </summary>
/// <remarks>
/// Measured only: every figure counts <c>capability.*.measured</c> (a server observed offering the
/// option), never an MSSP assertion of the same. The complement is not a measurement — "12 of 40
/// offered CHARSET" does not mean the other 28 lack it, some are simply unprobed — so there is
/// deliberately no "does not support" column to misread that gap as a fact about the game.
/// </remarks>
public sealed record ProtocolFigures(int Offering, int Listed, IReadOnlyList<ProtocolByCodebase> ByCodebase)
{
    public static readonly ProtocolFigures None = new(0, 0, []);

    /// <summary>The share of the listed catalogue observed offering it. Null when nothing is listed.</summary>
    public double? Share => Listed == 0 ? null : (double)Offering / Listed;

    public static async Task<ProtocolFigures> ReadAsync(
        IGameQueries queries,
        string protocol,
        IReadOnlyList<ReferenceDocument> codebases,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(codebases);

        // Default listing excludes archived games: this is a claim about what live servers speak.
        var games = await queries.ListAsync(new GameFilter(), cancellationToken);

        bool Offers(GameSummary g) =>
            g.MeasuredProtocols.Contains(protocol, StringComparer.OrdinalIgnoreCase);

        var rows = new List<ProtocolByCodebase>();

        foreach (var codebase in codebases.Where(c => c.Codebase is not null))
        {
            var family = games
                .Where(g => string.Equals(
                    CodebaseFamily.For(g.Codebase), codebase.Codebase, StringComparison.OrdinalIgnoreCase))
                .ToList();

            rows.Add(new ProtocolByCodebase(
                codebase.Title,
                codebase.Path,
                family.Count,
                family.Count(Offers)));
        }

        return new ProtocolFigures(
            games.Count(Offers),
            games.Count,
            [.. rows.OrderByDescending(r => r.Offering).ThenBy(r => r.Codebase, StringComparer.OrdinalIgnoreCase)]);
    }
}

/// <summary>
/// One row of a protocol page's implementation matrix: a codebase family, and how much of it we have
/// observed offering the option.
/// </summary>
/// <remarks>
/// <see cref="Identified"/> is the number of games we identified as this codebase, not the number
/// that exist. A family with none identified renders as a sentence, since <c>0 of 0</c> reads as a
/// finding and is not one.
/// </remarks>
public sealed record ProtocolByCodebase(string Codebase, string Path, int Identified, int Offering)
{
    public bool IsMeasured => Identified > 0;
}
