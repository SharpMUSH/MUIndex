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
/// <para>
/// <b>Neither half of the obvious approach works.</b> Gluing English around an anchor gives a
/// language that wants the link earlier, or a different preposition before it, nowhere to say so:
/// the markup owns the word order and a translator only ever receives the fragments. Formatting the
/// anchor <em>into</em> the string and trusting the result through a <c>MarkupString</c> fixes that
/// and makes every bundle a place somebody could put a tag.
/// </para>
/// <para>
/// So the message places a marker and the caller walks the runs. What a translator writes is text
/// either way, and the word order is theirs. <c>RandomGame</c> invented this for its empty state and
/// kept it private; the owner dashboard, the claim page and the owner panel each need it two or
/// three times over, and a fourth private copy is how the escaping stops matching.
/// </para>
/// <para>
/// The markers are Unicode private-use characters, which is what makes the split safe: no message,
/// no game name, no hostname and no token can contain one, so a run boundary is never something a
/// translator or an operator typed.
/// </para>
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
