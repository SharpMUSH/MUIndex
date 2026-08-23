namespace MUI.Catalog;

/// <summary>
/// Which channel first brought an address into the registry.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a fact about our crawl, not about the game.</b> It answers "how did this site come to
/// know about this address, and when" — never "where did this game come from". Any game worth
/// listing appears in several places at once, so the value here records which channel reached us
/// first and nothing more, and every surface that renders it must say so in those words. Spec §7.6
/// rejected an origin field on exactly that ground; the objection is answered by what the value is
/// allowed to claim, not by refusing to store it.
/// </para>
/// <para>
/// Set once, when a crawl target row is created, and never afterwards: the registry's insert
/// collapses onto an existing row and updates depth alone, so a second channel finding a known
/// address cannot overwrite the first.
/// </para>
/// <para>
/// Lives here rather than in <c>MUI.Discovery</c> because <c>GameRecord</c> carries it and the arrow
/// runs <c>MUI.Discovery</c> → <c>MUI.Catalog</c>, never back.
/// </para>
/// </remarks>
public enum DiscoverySource
{
    /// <summary>An address a human operator configured into this deployment.</summary>
    OperatorSeed,

    /// <summary>Somebody handed it to us through the public submission form (§8).</summary>
    Submission,

    /// <summary>Another game's own list named it (§7.2).</summary>
    Referral,

    /// <summary>The Intermud-3 router listed it.</summary>
    I3Mudlist,

    /// <summary>The AresCentral games API listed it.</summary>
    AresCentral,

    /// <summary>
    /// The one-time day-one address backfill (§7.6).
    /// </summary>
    /// <remarks>
    /// Deliberately names no directory. The backfill took host and port from several lists and
    /// recorded which one supplied a given address nowhere at all, so this is the honest ceiling on
    /// what we can say. Nothing in this repository writes it — the importer lives on
    /// <c>import/one-time</c> — and it exists so that branch has a spelling to use.
    /// </remarks>
    Backfill,
}

/// <summary>The database spelling of each <see cref="DiscoverySource"/>, in one place.</summary>
/// <remarks>
/// Text rather than the enum's integer, for the reason <see cref="FieldSource"/> is text: a column a
/// person can read in <c>psql</c> survives a member being inserted in the middle of the enum.
/// </remarks>
public static class DiscoverySources
{
    public static string ToDb(DiscoverySource source) => source switch
    {
        DiscoverySource.OperatorSeed => "operator_seed",
        DiscoverySource.Submission => "submission",
        DiscoverySource.Referral => "referral",
        DiscoverySource.I3Mudlist => "i3_mudlist",
        DiscoverySource.AresCentral => "ares_central",
        DiscoverySource.Backfill => "backfill",
        _ => throw new ArgumentOutOfRangeException(
            nameof(source), source, "No database spelling for this discovery source. Add one."),
    };

    /// <summary>
    /// The source a stored spelling names, or null.
    /// </summary>
    /// <remarks>
    /// Null for an absent value — every row written before the column existed — and null, rather than
    /// a throw, for a spelling this build does not know: during a rollout an older replica renders
    /// pages written by a newer one, and an unknown channel is a line we omit, not a page we fail.
    /// </remarks>
    public static DiscoverySource? From(string? value) => value switch
    {
        "operator_seed" => DiscoverySource.OperatorSeed,
        "submission" => DiscoverySource.Submission,
        "referral" => DiscoverySource.Referral,
        "i3_mudlist" => DiscoverySource.I3Mudlist,
        "ares_central" => DiscoverySource.AresCentral,
        "backfill" => DiscoverySource.Backfill,
        _ => null,
    };
}
