using MUI.Catalog;
using MUI.Crawl;
using MUI.I3;

namespace MUI.Crawler;

/// <summary>
/// What an Intermud-3 mudlist entry says a game runs, in the vocabulary <c>game_field</c> stores.
/// </summary>
/// <remarks>
/// <c>I3Mud.Driver</c>, <c>I3Mud.Mudlib</c> and <c>I3Mud.MudType</c> arrive on every mudlist entry and
/// were being dropped unread. <c>CODEBASE</c> means the engine (the same thing MSSP means by it); the
/// mudlib is the library on top of it, a different fact with its own field — Dead Souls is a mudlib,
/// FluffOS is what the game runs. Where the two agree, only one row is written, since a mudlib row
/// repeating the driver is noise. It's a claim and is stored as one: <see cref="FieldSource.I3Mudlist"/>
/// is below <see cref="FieldSource.Mssp"/> on the ladder and outside <c>FieldSources.IsMeasured</c> —
/// the entry is what the mud told a router at some past startup and the router repeated onward,
/// undated, the same class of statement as an MSSP variable arriving down a longer pipe.
/// </remarks>
public static class I3Description
{
    /// <summary>The field the library on top of the driver is stored under.</summary>
    /// <remarks>
    /// Named after the thing rather than after I3: not an I3 concept, MSSP just has no variable for it.
    /// </remarks>
    public const string MudlibField = "MUDLIB";

    /// <summary>MSSP's coarse taxonomy, which is exactly what <c>mud_type</c> carries.</summary>
    public const string FamilyField = FieldObservations.FamilyField;

    /// <summary>
    /// Everything this mudlist entry says about what the game runs. Empty for an entry that filled
    /// nothing in — a placeholder is not a value (see <see cref="MsspDefaults.IsTemplate"/>).
    /// </summary>
    public static IReadOnlyList<FieldObservation> From(I3Mud mud)
    {
        ArgumentNullException.ThrowIfNull(mud);

        var observations = new List<FieldObservation>();

        var driver = Meaningful(mud.Driver);
        var mudlib = Meaningful(mud.Mudlib);
        var family = Meaningful(mud.MudType);

        if (driver is not null)
        {
            observations.Add(new FieldObservation(
                FieldObservations.CodebaseField, FieldSource.I3Mudlist, driver));
        }

        // Only where it says something the driver did not — a MUDLIB row repeating CODEBASE verbatim
        // is a second copy of one fact for a reader to reconcile.
        if (mudlib is not null && !string.Equals(mudlib, driver, StringComparison.OrdinalIgnoreCase))
        {
            observations.Add(new FieldObservation(MudlibField, FieldSource.I3Mudlist, mudlib));
        }

        if (family is not null)
        {
            observations.Add(new FieldObservation(FamilyField, FieldSource.I3Mudlist, family));
        }

        return observations;
    }

    /// <summary>
    /// The value, unless it is blank or one of the strings a codebase ships as its default.
    /// </summary>
    /// <remarks>
    /// <see cref="MsspDefaults.IsTemplate"/>, not <c>IsPlaceholder</c>: <c>IsPlaceholder</c> also
    /// refuses <c>FluffOS</c> and <c>CircleMUD</c>, which is right for a game's name but would delete
    /// the answer here.
    /// </remarks>
    private static string? Meaningful(string? value)
    {
        var trimmed = value?.Trim();

        return MsspDefaults.IsTemplate(trimmed) ? null : trimmed;
    }
}
