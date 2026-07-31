using MUI.Catalog;

namespace MUI.Web.Reference;

/// <summary>
/// The measured half of a codebase page: how many games we have identified as running it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The count and the link are one query.</b> <see cref="ReadAsync"/> asks
/// <see cref="IGameQueries"/> for exactly the filter the page's "see the games" link carries, so
/// "games running this codebase: 47" and the listing a reader lands on cannot disagree. A separate
/// aggregate would have been cheaper and would have introduced the one bug this page exists to avoid
/// — a headline number that no listing reproduces.
/// </para>
/// <para>
/// Archived games are counted <em>and</em> reported separately. Archiving removes a game from the
/// default listing and from nothing else, so a codebase with four live games and thirty archived
/// ones is a fact about the hobby worth stating rather than a smaller number.
/// </para>
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
            new GameFilter { CodebaseFamily = family, IncludeArchived = true },
            cancellationToken);

        return new CodebaseFigures(
            games.Count(g => g.State is not LifecycleState.Archived),
            games.Count(g => g.State is LifecycleState.Archived),

            // What servers of this family actually offered us, most common first. This is the
            // codebase's observed behaviour rather than its documentation's claim, and the two are
            // routinely different — a codebase that ships GMCP support nobody has switched on shows
            // here as the silence it is on the wire.
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
/// <para>
/// <b>Measured only.</b> Every figure here counts <c>capability.*.measured</c> — a server offering
/// the option to our probe. A game's MSSP declaring the same option is an assertion and never
/// reaches this page, which is the difference between this matrix and every protocol table the
/// hobby already has.
/// </para>
/// <para>
/// <b>The complement is not a measurement, and the page has to say so.</b> "12 of 40 PennMUSH games
/// offered CHARSET" leaves 28 games that did not offer it <em>to us</em> — a set that mixes servers
/// which do not implement it with servers we have not yet read a handshake from. Rendering that
/// remainder as "does not support" would be recording our own gap as a fact about somebody's game,
/// which is the rule this whole project is built on. So the matrix carries counts of positive
/// observations and no <c>no</c> column exists to be misread.
/// </para>
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

        // The default listing: archived games are excluded because this is a claim about what the
        // hobby's live servers speak, and a game that stopped answering in 2019 was measured against
        // whatever it ran then.
        var games = await queries.ListAsync(new GameFilter(), cancellationToken);

        bool Offers(GameSummary g) =>
            g.MeasuredProtocols.Contains(protocol, StringComparer.OrdinalIgnoreCase);

        var rows = new List<ProtocolByCodebase>();

        foreach (var codebase in codebases.Where(c => c.Codebase is not null))
        {
            var family = games.Where(g => CodebaseFamily.Matches(g.Codebase, codebase.Codebase)).ToList();

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
/// <see cref="Identified"/> is the denominator and it is the number of games we identified as this
/// codebase — not the number that exist. A family with none identified renders as a sentence saying
/// so, because <c>0 of 0</c> reads as a finding and is nothing of the kind.
/// </remarks>
public sealed record ProtocolByCodebase(string Codebase, string Path, int Identified, int Offering)
{
    public bool IsMeasured => Identified > 0;
}
