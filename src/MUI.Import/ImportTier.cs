using MUI.Catalog;

namespace MUI.Import;

/// <summary>
/// Whether the directory we are reading measured a game or merely wrote it down (spec §7.6).
/// </summary>
/// <remarks>
/// The same measured-versus-declared spine that runs through the rest of the design, applied one
/// level up. A third party that ran its own probe produced a measurement, and a measurement is worth
/// more than a self-report — that is the whole argument of this project, and it does not stop
/// applying because somebody else did the probing. A hand-maintained list is an assertion and is
/// treated as one.
/// <para>
/// The tier belongs to the <em>site</em>, never to a call or a record: a source that pings is
/// <see cref="Measured"/> for everything it yields and one that does not is <see cref="Asserted"/>
/// for everything it yields. There is no third state and no per-field override, because a per-field
/// override is how a hand-maintained list ends up with one row of history.
/// </para>
/// </remarks>
public enum ImportTier
{
    /// <summary>The TinTin++ MSSP crawler, MudStats, MudVerse, Grapevine — sites that actively probe.</summary>
    Measured,

    /// <summary>The MUD Connector, MUSHCode lists, hand-maintained pages.</summary>
    Asserted,
}

/// <summary>
/// The tier's consequences, in one place. Nothing else in this assembly may re-derive them.
/// </summary>
public static class ImportTierMap
{
    /// <summary>
    /// The <see cref="FieldSource"/> every value from this tier carries. Both members already exist
    /// on that enum at the bottom of §5.1's precedence ladder; this mapping never invents one.
    /// </summary>
    public static FieldSource SourceFor(ImportTier tier) => tier switch
    {
        ImportTier.Measured => FieldSource.ImportedMeasured,
        ImportTier.Asserted => FieldSource.ImportedAsserted,
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };

    /// <summary>
    /// Whether this tier may populate <c>availability_interval</c> and <c>presence_sample</c> rows.
    /// The asserted tier seeds discovery and endpoints only: no history, no presence, no grace.
    /// </summary>
    /// <remarks>
    /// <b>This predicate is documentation, not enforcement.</b> The enforcement is
    /// <see cref="AssertedHistorySink"/>, which holds no writer, no store and no clock, and so cannot
    /// write history even if somebody forgets to consult this method.
    /// </remarks>
    public static bool MayWriteHistory(ImportTier tier) => tier is ImportTier.Measured;
}
