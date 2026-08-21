using MUI.Crawl;

namespace MUI.Discovery;

/// <summary>
/// Reading an MSSP report without ever treating a codebase default as an answer.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every identity signal goes through <see cref="Meaningful"/>, and that is not polish.</b> Every
/// unedited PennMUSH publishes <c>NAME "PennMUSH"</c>; scored naively they'd all match each other on
/// the strongest textual signal in §7.3's table, and auto-merge would fuse unrelated games into one
/// listing.
/// </para>
/// <para>
/// A placeholder contributes <b>nothing</b> rather than a little — two absences must never score as
/// an agreement. The same applies to <c>CONTACT</c>/<c>WEBSITE</c> (shared across a hosting provider)
/// and <c>CREATED</c> (a year that collides freely), all covered by
/// <see cref="MsspDefaults.IsPlaceholder"/>.
/// </para>
/// </remarks>
public static class MsspReading
{
    /// <summary>The raw value of an MSSP variable, matched case-insensitively as MSSP variables are.</summary>
    /// <remarks>
    /// A variable holds a <em>list</em>, because MSSP lets a server repeat one; identity wants a
    /// scalar. Takes the <b>last</b>, per the spec's own reduction rule ("the last reported value
    /// should be used as the default value"), matching <see cref="ProbeResult.MsspField"/>.
    /// </remarks>
    public static string? Value(IReadOnlyDictionary<string, IReadOnlyList<string>> mssp, string variable)
    {
        ArgumentNullException.ThrowIfNull(mssp);

        foreach (var (name, values) in mssp)
        {
            if (string.Equals(name.Trim(), variable, StringComparison.OrdinalIgnoreCase)
                && values.Count > 0)
            {
                return values[^1];
            }
        }

        return null;
    }

    /// <summary>
    /// The value if somebody answered, or null if it is blank, template text or the codebase's own
    /// default. Never returns an empty string — the two states a caller must tell apart are "answered"
    /// and "did not", and a placeholder is the second.
    /// </summary>
    public static string? Meaningful(IReadOnlyDictionary<string, IReadOnlyList<string>> mssp, string variable)
    {
        var raw = Value(mssp, variable);
        return MsspDefaults.IsPlaceholder(raw) ? null : raw!.Trim();
    }

    /// <summary>
    /// A game's declared name, or null when it is a placeholder <em>or</em> merely restates the
    /// codebase — <c>NAME "PennMUSH 1.8.8p0"</c> is the same non-answer as <c>NAME "PennMUSH"</c>.
    /// </summary>
    public static string? MeaningfulName(IReadOnlyDictionary<string, IReadOnlyList<string>> mssp) =>
        MsspDefaults.MeaningfulName(
            Value(mssp, IdentityMsspVariables.Name),
            Value(mssp, IdentityMsspVariables.Codebase));
}
