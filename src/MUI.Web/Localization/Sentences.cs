namespace MUI.Web.Localization;

/// <summary>
/// A sentence whose word order belongs to the locale and whose markup belongs to the site.
/// </summary>
/// <remarks>
/// <para>
/// Some sentences carry a link or emphasised word. Gluing English round an anchor gives a language
/// with different word order nowhere to say so, and letting a bundle embed markup via
/// <c>MarkupString</c> would make every translation a place a tag could be put. So the message places
/// a marker, this walks it, and a translator only ever writes text.
/// </para>
/// <para>
/// Markers are private-use code points, so no translation contains one by accident. They are
/// assigned in slot order and never appear in the output — a marker that survives is a slot the
/// message didn't place, and the run for it is simply absent.
/// </para>
/// </remarks>
public static class Sentences
{
    /// <summary>The first marker. One code point per slot, upwards from here.</summary>
    private const char Marker = '\uE000';

    /// <summary>
    /// The most slots one sentence may place. Well past anything readable, and a bound rather than
    /// an open range so a stray private-use character in a translation cannot be read as a slot.
    /// </summary>
    private const int MaxSlots = 8;

    /// <summary>One piece of a sentence: plain text, or the text of a named slot.</summary>
    /// <param name="Text">What the reader sees, already in their language.</param>
    /// <param name="Slot">
    /// The argument name the message placed, or null for the prose between the slots. The caller
    /// switches on this to decide what markup goes round the run.
    /// </param>
    public sealed record Run(string Text, string? Slot);

    /// <summary>
    /// A message, split into the runs its markup renders — the slots in the order the
    /// <em>message</em> puts them, which is the locale's order and not this call's.
    /// </summary>
    public static IReadOnlyList<Run> Place(string tag, string id, params (string Name, string Text)[] slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(slots.Length, MaxSlots);

        var sentence = Messages.For(tag, id, slots
            .Select((slot, i) => (slot.Name, Value: (object?)((char)(Marker + i)).ToString()))
            .ToDictionary(s => s.Name, s => s.Value, StringComparer.Ordinal));

        var runs = new List<Run>();
        var run = new System.Text.StringBuilder();

        foreach (var c in sentence)
        {
            var slot = c - Marker;

            if (slot < 0 || slot >= slots.Length)
            {
                run.Append(c);
                continue;
            }

            if (run.Length > 0)
            {
                runs.Add(new Run(run.ToString(), null));
                run.Clear();
            }

            runs.Add(new Run(slots[slot].Text, slots[slot].Name));
        }

        if (run.Length > 0)
        {
            runs.Add(new Run(run.ToString(), null));
        }

        return runs;
    }

    /// <summary>
    /// The same sentence with the slots substituted rather than marked — for the plain surface,
    /// which has no markup to put round them and must still say every word the page does.
    /// </summary>
    public static string Flat(string tag, string id, params (string Name, string Text)[] slots)
    {
        ArgumentNullException.ThrowIfNull(slots);

        return Messages.For(tag, id, slots.ToDictionary(
            s => s.Name, s => (object?)s.Text, StringComparer.Ordinal));
    }
}
