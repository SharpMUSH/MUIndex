namespace MUI.Web.Localization;

/// <summary>
/// A sentence whose word order belongs to the locale and whose markup belongs to the site.
/// </summary>
/// <remarks>
/// <para>
/// Some sentences have a link or an emphasised word inside them. Gluing English round an anchor
/// gives a language that wants the link first, or a different preposition before it, nowhere to say
/// so; formatting the anchor <em>into</em> the string and trusting the result through a
/// <c>MarkupString</c> would make every bundle a place a tag could be put. So the message places a
/// marker, this walks it, and what a translator writes is text either way.
/// </para>
/// <para>
/// The markers are private-use code points, which no translation contains by accident and no script
/// the site offers uses. They are assigned in the order the slots are passed and never appear in
/// what a reader is shown: a marker that survives into the output is a slot the message did not
/// place, and the run for it is simply absent — which is visible rather than silent.
/// </para>
/// <para>
/// This was two copies before it was one file. The random-game empty state placed two links this
/// way and the reference section needed the same thing for its own two sentences, at which point the
/// marker constant, the walk and the "translator writes no markup" argument were about to exist
/// twice.
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
