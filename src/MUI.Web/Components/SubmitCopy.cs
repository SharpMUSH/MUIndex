using System.Globalization;

using MUI.Discovery;

namespace MUI.Web.Components;

/// <summary>
/// The words on the submission form, and the one sentence it answers with.
/// </summary>
/// <remarks>
/// <para>
/// Here rather than in the page because the plain surface renders the same words (spec §9), and a
/// form whose refusal reads differently in a text browser is a form with two policies. Nothing here
/// touches a database or a socket: this maps a <see cref="SubmissionOutcome"/> to English, and
/// <see cref="SubmissionService"/> is what decides.
/// </para>
/// <para>
/// <b>No refusal here explains itself with an address.</b> The receipt carries a detail naming what
/// the host resolved to, and it is for an operator's log — showing it to the submitter would turn
/// this form into a free scan of whatever network the crawler happens to run inside, which is the
/// exact thing §7.2's gate exists to prevent.
/// </para>
/// </remarks>
public static class SubmitCopy
{
    public const string Title = "Submit a game";

    public const string Lede =
        "Tell us where a game is. A host and a port, and that is the whole form — everything else "
        + "on this site is measured by our own crawler, so there is nothing here for you to fill in "
        + "about the game and nothing here for anybody to claim about it.";

    /// <summary>What happens to an address after it is submitted, in the order it happens.</summary>
    /// <remarks>
    /// The third point is the one people are surprised by, so it is stated on the form rather than
    /// discovered afterwards: a submission does not put a listing up.
    /// </remarks>
    public static IReadOnlyList<string> Points { get; } =
    [
        "We resolve the address before we dial it, and refuse it if any of what it resolves to is "
        + "somewhere other than the public internet. That is a decision about where our own socket "
        + "may go, and it is never written into anything as a fact about a game.",

        "If it answers, we read what the server says for itself — its name, what it runs, who is on "
        + "— and keep reading it on its own schedule, for ever. Nothing is ever deleted here, so an "
        + "address only has to be given once.",

        "Nothing about it appears on this site until somebody proves they run it. We took the "
        + "address from a stranger, and a stranger's word is not evidence: claiming takes a passkey "
        + "and one line published on the game itself, and then the listing is live.",

        "An address we already have collapses onto what is already here. Sending it twice makes no "
        + "second listing and does not bring the next probe forward.",
    ];

    /// <summary>The label on each box, so the rendered form and the plain one cannot drift.</summary>
    public const string HostLabel = "Host";

    public const string PortLabel = "Port";

    public const string HostHint = "mud.example.org — or paste mud.example.org:4201 and leave the port empty";

    public const string SubmitLabel = "Submit this address";

    /// <summary>What the site says when it has no database and therefore no registry to write into.</summary>
    public const string NoCatalogue =
        "Submitting needs a database behind it, and this site is running on the demo fixture. There "
        + "is no crawl registry here to put an address into, so the form is absent rather than "
        + "present and quietly doing nothing.";

    /// <summary>The answer to one submission, or null when nothing has been submitted yet.</summary>
    public static SubmitAnswer? Answer(SubmissionOutcome? outcome, string? address, SubmitLink? link = null) =>
        outcome switch
        {
            null => null,

            SubmissionOutcome.Accepted => new SubmitAnswer(
                "In the registry.",
                $"{Named(address)} will be dialled on the next crawl cycle, and on its own schedule "
                + "for ever after that. If it publishes a name of its own we will list it here as "
                + "soon as somebody proves they run it — come back to this form with the same "
                + "address and it will hand you the link."),

            // Two arms, and the link is what differs. A game we list gets its page; a submitted one
            // nobody has claimed gets its claim page, because that is the only exit from hidden and
            // a person who has just told us the address is exactly who should be offered it.
            SubmissionOutcome.AlreadyListed when link?.IsClaim is true => new SubmitAnswer(
                "We have that address, and nobody has claimed it.",
                $"{Named(address)} is one we already measure, and none of it is on this site "
                + "because nobody has proved they run it. If that is you, this is the way in.",
                link),

            SubmissionOutcome.AlreadyListed when link is not null => new SubmitAnswer(
                "We already have that one.",
                $"{Named(address)} is a game we already measure. Nothing was created and nothing "
                + "was changed.",
                link),

            SubmissionOutcome.AlreadyListed => new SubmitAnswer(
                "We already have that address.",
                $"{Named(address)} is already known to us. Nothing was created and nothing was "
                + "changed."),

            SubmissionOutcome.AlreadyQueued => new SubmitAnswer(
                "Already waiting.",
                $"{Named(address)} is in the crawl registry and has not answered for itself yet. "
                + "Sending it again does not bring it forward — a target keeps its own schedule, "
                + "which is what stops anybody hurrying us at somebody else's server."),

            SubmissionOutcome.Malformed => new SubmitAnswer(
                "That is not an address we can dial.",
                "A host needs a dot or a colon in it, and a port is a number between 1 and 65535. "
                + "Either fill in both boxes, or paste the whole thing — mud.example.org:4201 — "
                + "into the first."),

            // ONE SENTENCE FOR BOTH, AND THE VAGUENESS IS THE POINT. §7.2 keeps "did not resolve"
            // and "resolved somewhere we will not go" apart because they are two facts and our own
            // record has to hold them apart — but telling a *stranger* which of the two happened
            // turns this form into a scanner of whatever the crawler's resolver can see. Submit
            // internal.corp.example: one answer means it exists on our side of a split horizon and
            // the other means it does not, and a few hundred guesses is a map of somebody's network
            // drawn from outside it. The log knows which; the page does not say.
            SubmissionOutcome.RefusedNotRoutable or SubmissionOutcome.Unresolvable => new SubmitAnswer(
                "We cannot dial that.",
                $"Either {Named(address)} does not resolve, or it resolves somewhere that is not "
                + "the public internet — and we deliberately do not say which, because answering "
                + "that question for a stranger is a way to map a private network from outside it. "
                + "Nothing has been recorded about whatever is at that address; the decision was "
                + "ours and it is filed as ours."),

            SubmissionOutcome.TooMany => new SubmitAnswer(
                "That is enough for now.",
                "This form is rate-limited by where a submission came from, and that bound has been "
                + "reached. Come back in an hour. Nothing you sent has been lost — an address we "
                + "took is already in the registry."),

            _ => null,
        };

    private static string Named(string? address) => address is { Length: > 0 } ? address : "that address";
}

/// <summary>One answer, as both surfaces render it.</summary>
public sealed record SubmitAnswer(string Heading, string Sentence, SubmitLink? Link = null);

/// <summary>Where an answer points, when it points anywhere.</summary>
/// <param name="IsClaim">
/// Whether this is the way into a game the site is holding back — which reads differently, and is
/// the only exit hidden-until-claimed has.
/// </param>
public sealed record SubmitLink(string Href, string Label, bool IsClaim = false)
{
    public static SubmitLink Game(string slug) => new($"/g/{slug}", $"/g/{slug}");

    public static SubmitLink Claim(string slug) => new($"/g/{slug}/claim", "claim this game", IsClaim: true);
}

/// <summary>
/// The querystring the form's answer travels in.
/// </summary>
/// <remarks>
/// <para>
/// Post, redirect, get — so a reload does not resubmit and the answer is a page somebody can send to
/// themselves. The alternative is a session, and a public form that has to set a cookie before it
/// can tell you what it did is a public form with a tracking problem.
/// </para>
/// <para>
/// <b>Nothing in this querystring is trusted.</b> The outcome is a word out of a fixed vocabulary,
/// the address is echoed back at whoever typed it, and the slug is looked up again before it is
/// rendered — so a hand-made link can make this page say something about an address, and can make it
/// say nothing at all about a game.
/// </para>
/// </remarks>
public static class SubmitLinks
{
    public const string Path = "/submit";

    public const string ResultKey = "result";

    public const string HostKey = "host";

    public const string PortKey = "port";

    public const string GameKey = "g";

    /// <summary>Where a handler sends the browser once it has decided.</summary>
    public static string For(SubmissionOutcome outcome, SubmittedAddress? address, string? slug)
    {
        var query = new List<string> { $"{ResultKey}={Token(outcome)}" };

        if (address is not null)
        {
            query.Add($"{HostKey}={Uri.EscapeDataString(address.Host)}");
            query.Add($"{PortKey}={address.Port.ToString(CultureInfo.InvariantCulture)}");
        }

        if (slug is not null)
        {
            query.Add($"{GameKey}={Uri.EscapeDataString(slug)}");
        }

        return $"{Path}?{string.Join('&', query)}";
    }

    /// <summary>The address as the answer names it, from what the querystring carried.</summary>
    public static string? Address(string? host, string? port) =>
        SubmittedAddressReader.TryRead(host, port, out var address)
            ? $"{address.Host} {address.Port.ToString(CultureInfo.InvariantCulture)}"
            : null;

    /// <summary>
    /// The word an outcome travels under.
    /// </summary>
    /// <remarks>
    /// <b>Both scope outcomes share one token, and that is not a shortcut.</b> Collapsing the two
    /// sentences and then putting the distinction back in the URL would leave the same oracle in a
    /// place easier to read — a script would never look at the prose. §7.2's two facts live in
    /// <c>game_submission.outcome</c>, which is ours.
    /// </remarks>
    public static string Token(SubmissionOutcome outcome) => outcome switch
    {
        SubmissionOutcome.Accepted => "accepted",
        SubmissionOutcome.AlreadyListed => "already-listed",
        SubmissionOutcome.AlreadyQueued => "already-queued",
        SubmissionOutcome.Malformed => "malformed",
        SubmissionOutcome.RefusedNotRoutable or SubmissionOutcome.Unresolvable => "undialable",
        SubmissionOutcome.TooMany => "too-many",
        _ => "unknown",
    };

    /// <summary>The reverse, refusing anything that is not one of the words above.</summary>
    public static SubmissionOutcome? Outcome(string? token) => token switch
    {
        "accepted" => SubmissionOutcome.Accepted,
        "already-listed" => SubmissionOutcome.AlreadyListed,
        "already-queued" => SubmissionOutcome.AlreadyQueued,
        "malformed" => SubmissionOutcome.Malformed,

        // Reads back as one of the two arbitrarily, because the surface treats them identically and
        // a reader of this URL is owed no more than that.
        "undialable" => SubmissionOutcome.RefusedNotRoutable,
        "too-many" => SubmissionOutcome.TooMany,
        _ => null,
    };
}
