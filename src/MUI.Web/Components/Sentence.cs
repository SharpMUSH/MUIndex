using System.Text;

using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>One run of a sentence: plain text, or the text that fills a named slot.</summary>
/// <param name="Text">What to render.</param>
/// <param name="Slot">
/// Which slot this run is, or null for the prose between them. The caller switches on it to choose
/// an element — an anchor, a <c>&lt;code&gt;</c>, an emphasis — because the element is markup and
/// the words are not.
/// </param>
public sealed record SentencePart(string Text, string? Slot);

/// <summary>
/// A sentence that places its own links, code spans and emphasis.
/// </summary>
/// <remarks>
/// Gluing English text around an anchor fixes the word order, leaving a translator no way to move
/// the link; trusting a formatted anchor through <c>MarkupString</c> makes every message a place to
/// inject a tag. Instead the message places a marker and the caller walks the runs, so a translator
/// writes ordinary text and chooses the order. Markers are Unicode private-use characters, so a run
/// boundary can never collide with translated or operator-typed text.
/// </remarks>
public static class Sentence
{
    /// <summary>The first private-use code point, one per slot in the order they are named.</summary>
    private const char FirstMarker = '\uE000';

    /// <summary>
    /// One message, split into the runs its markup renders.
    /// </summary>
    /// <param name="tag">The locale the sentence is being answered in.</param>
    /// <param name="id">The message id.</param>
    /// <param name="slots">
    /// The argument names the message places as markup rather than as text. Each one appears in the
    /// result as a <see cref="SentencePart"/> carrying that name and no text of its own.
    /// </param>
    /// <param name="args">Ordinary arguments — a count, a game's name — substituted as text.</param>
    public static IReadOnlyList<SentencePart> Place(
        string tag,
        string id,
        IReadOnlyList<string> slots,
        params (string Key, object? Value)[] args)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(args);

        if (slots.Count > 16)
        {
            // Not a real limit so much as a smell: a sentence with seventeen pieces of markup in it
            // is a paragraph pretending to be a string, and no translator can keep it in order.
            throw new ArgumentException("A sentence places at most 16 slots.", nameof(slots));
        }

        var values = args.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal);

        for (var i = 0; i < slots.Count; i++)
        {
            values[slots[i]] = ((char)(FirstMarker + i)).ToString();
        }

        var formatted = Messages.For(tag, id, values);

        var parts = new List<SentencePart>();
        var run = new StringBuilder();

        foreach (var c in formatted)
        {
            var slot = c - FirstMarker;

            if (slot < 0 || slot >= slots.Count)
            {
                run.Append(c);
                continue;
            }

            if (run.Length > 0)
            {
                parts.Add(new SentencePart(run.ToString(), null));
                run.Clear();
            }

            parts.Add(new SentencePart(string.Empty, slots[slot]));
        }

        if (run.Length > 0)
        {
            parts.Add(new SentencePart(run.ToString(), null));
        }

        return parts;
    }
}
