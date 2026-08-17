using MUI.Catalog;
using MUI.Crawl;
using MUI.I3;

namespace MUI.Crawler;

/// <summary>
/// What an Intermud-3 mudlist entry says a game runs, in the vocabulary <c>game_field</c> stores.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three fields the router hands us on every mud, and all three were being parsed and dropped.</b>
/// <c>I3Mud.Driver</c>, <c>I3Mud.Mudlib</c> and <c>I3Mud.MudType</c> have been modelled since the
/// gateway was written and nothing has ever read them. Asked of the live network on 2026-08-17,
/// <b>179 of 179 entries filled in all three</b>, with versions:
/// </para>
/// <code>
/// The Zone         DGD 1.4.1            WOTFlib 0.90      LPMud
/// CyberASSAULT     CircleMUD            LuminariMUD       CircleMUD
/// Mystic Falls     FluffOS v2.23-ds03   Dead Souls 3.9    LPMud
/// CoffeeMud Local  CoffeeMud v5.11.0.3  CoffeeMud v5…     CoffeeMud
/// </code>
/// <para>
/// 47 of the games this catalogue has bound to an I3 name carry no codebase at all, and the answer
/// was arriving in memory on every cycle.
/// </para>
/// <para>
/// <b>Which field is the codebase, and why it is the driver.</b> <c>CODEBASE</c> means the engine
/// (the same thing MSSP means by it) and the mudlib is the library on top of it, which is a different
/// fact and gets a field of its own rather than being folded in. Dead Souls is not a codebase; it is
/// a mudlib, and FluffOS is what the game runs. Where the two agree — every CoffeeMud on the network
/// writes its own version into both — only one row is written, because a mudlib row repeating the
/// driver is the capability matrix's noise problem in a new place.
/// </para>
/// <para>
/// <b>It is a claim and it is stored as one.</b> <see cref="FieldSource.I3Mudlist"/> is below
/// <see cref="FieldSource.Mssp"/> on the ladder and outside <c>FieldSources.IsMeasured</c>: a game
/// that fills in its own MSSP wins, a value we read off a connect screen loses, and the page says
/// <em>declared</em> either way. The entry is what the mud told a router at some past startup and
/// the router repeated onward, undated — the same class of statement as an MSSP variable arriving
/// down a longer pipe. Migration 0020 said <c>game_field</c>'s vocabulary would not be widened for
/// this; 0023 widened it for <c>NAME</c>, for the reason stated there, and this is that decision
/// applied to the two fields beside it rather than a new one.
/// </para>
/// </remarks>
public static class I3Description
{
    /// <summary>The field the library on top of the driver is stored under.</summary>
    /// <remarks>
    /// New, and named after the thing rather than after I3, because it is not an I3 concept: MSSP has
    /// no variable for it but every LP game has one, and a game that later declares its mudlib some
    /// other way must land on this same field under its own source.
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

        // Only where it says something the driver did not. Every CoffeeMud on the network reports
        // its own version in both fields, and a MUDLIB row repeating CODEBASE verbatim is a second
        // copy of one fact for a reader to reconcile.
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
    /// <see cref="MsspDefaults.IsTemplate"/> and emphatically not <c>IsPlaceholder</c>: the live
    /// network carries <c>Your MUD Name</c> and <c>test</c> in the name field, so nothing polices
    /// what a mud puts in the others either — but <c>IsPlaceholder</c> also refuses <c>FluffOS</c>
    /// and <c>CircleMUD</c>, which is right for a game's name and would delete the answer here.
    /// </remarks>
    private static string? Meaningful(string? value)
    {
        var trimmed = value?.Trim();

        return MsspDefaults.IsTemplate(trimmed) ? null : trimmed;
    }
}
