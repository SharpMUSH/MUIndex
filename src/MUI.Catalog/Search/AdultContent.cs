namespace MUI.Catalog;

/// <summary>
/// Whether a game has declared adult content, from the two MSSP variables that say so.
/// </summary>
/// <remarks>
/// <para>
/// One predicate, because two implementations of <see cref="IGameQueries"/> feed one
/// <see cref="FacetedSearch"/> (Postgres reads it off <c>game_field</c> rows, the demo fixture
/// assigns it) — a second spelling of this rule risks the two disagreeing about which games a
/// default hides.
/// </para>
/// <para>
/// Both inputs are declared, neither is a measurement — we've read what the operator typed into
/// <c>mush.cnf</c>, not inspected the game. Hiding it by default is our own listing decision and may
/// never be rendered as a fact we established about the game (rule 5).
/// </para>
/// <para>
/// <c>ADULT MATERIAL</c> is read as well as <c>GENRE</c> because they catch different games: MSSP
/// gives <c>GENRE</c> one value, so a game whose setting is Fantasy and content is adult has only the
/// flag to say so.
/// </para>
/// </remarks>
public static class AdultContent
{
    /// <summary>The <c>GENRE</c> value MSSP enumerates for this, matched whole.</summary>
    /// <remarks>
    /// An equality rather than a substring. <c>GENRE</c> holds one value from a short list, and a
    /// game calling itself <c>Adult Swim Parody</c> has declared a cartoon and not its content.
    /// </remarks>
    public const string Genre = "Adult";

    /// <summary>The MSSP variable whose whole purpose is this question.</summary>
    public const string Field = "ADULT MATERIAL";

    public static bool Declared(string? genre, string? adultMaterial) =>
        string.Equals(genre?.Trim(), Genre, StringComparison.OrdinalIgnoreCase)
        || IsYes(adultMaterial);

    /// <summary>
    /// MSSP's booleans are <c>1</c> and <c>0</c>, and the words are accepted because games type them.
    /// </summary>
    /// <remarks>
    /// Anything else — including nothing at all — is <c>false</c>, and that is not a claim that the
    /// game said no. A variable we cannot read is a variable we cannot read; the only thing that
    /// follows is that this listing has no reason to hide the game.
    /// </remarks>
    private static bool IsYes(string? value) =>
        value?.Trim().ToLowerInvariant() is "1" or "true" or "yes";
}
