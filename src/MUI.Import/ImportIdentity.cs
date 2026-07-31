using MUI.Catalog.Persistence;

namespace MUI.Import;

/// <summary>
/// Which game, if any, an imported record is about.
/// </summary>
/// <remarks>
/// <see cref="GameId"/> null is the ordinary case for a day-one backfill and is not a failure: the
/// record's addresses become crawl targets and the game is listed when a host answers for itself
/// (spec §7.2).
/// </remarks>
public sealed record ImportMatch(Guid? GameId, string? MatchedOn)
{
    public static readonly ImportMatch None = new(null, null);
}

/// <summary>
/// Identity on the way in, using exactly one signal: an endpoint we already know.
/// </summary>
/// <remarks>
/// <para>
/// §7.3's full weighted fingerprint is the crawler's, and it is deliberately not used here. Every
/// other signal in that table — <c>NAME</c> + <c>CREATED</c>, a banner hash, <c>WEBSITE</c>,
/// <c>CODEBASE</c> — comes to an importer as text somebody else typed into somebody else's list, and
/// §7.3's own warning is that a naive score over textual signals fuses unrelated games. A previously
/// seen address is the one signal an import can offer that the crawler measured itself.
/// </para>
/// <para>
/// So this matcher has no threshold and no middling band: it matches an address or it does not. The
/// consequence — an import can attach data to a game we already probed, and can never mint one — is
/// exactly spec §7.2's rule, arrived at by having no mechanism to break it.
/// </para>
/// </remarks>
public sealed class ImportIdentity(IEndpointStore endpoints)
{
    private readonly IEndpointStore _endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));

    public async Task<ImportMatch> ResolveAsync(ImportedGame record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        foreach (var endpoint in record.Endpoints)
        {
            var known = await _endpoints
                .ByAddressAsync(endpoint.Host, endpoint.Port, cancellationToken)
                .ConfigureAwait(false);

            if (known is not null)
            {
                return new ImportMatch(known.GameId, $"{endpoint.Host}:{endpoint.Port}");
            }
        }

        return ImportMatch.None;
    }
}
