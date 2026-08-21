using System.Text.Json;

namespace MUI.Discovery;

/// <summary>Signals as stored on a merge or review row, so a decision can be explained later.</summary>
public static class IdentitySignals
{
    public static string ToJson(IReadOnlyList<IdentitySignal> signals) => JsonSerializer.Serialize(signals);

    /// <summary>
    /// The reverse of <see cref="ToJson"/> — reading a review row's evidence back, so a merge that
    /// resolves one can carry the same signals forward onto <c>merge_log</c> rather than inventing new
    /// ones. An unreadable or absent payload is an empty list, not a failure: nothing here is evidence
    /// of a defect a caller should crash over.
    /// </summary>
    public static IReadOnlyList<IdentitySignal> FromJson(string? signalsJson)
    {
        if (string.IsNullOrWhiteSpace(signalsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<IdentitySignal>>(signalsJson) ?? [];
        }
        catch (JsonException)
        {
            // The unreadable half of the summary above: corrupted evidence must not block a merge.
            return [];
        }
    }
}
