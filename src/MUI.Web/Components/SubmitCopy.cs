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
    public static SubmitAnswer? Answer(SubmissionOutcome? outcome, string? address, string? slug) =>
        outcome switch
        {
            null => null,

            SubmissionOutcome.Accepted => new SubmitAnswer(
                "In the registry.",
                $"{Named(address)} will be dialled on the next crawl cycle, and on its own schedule "
                + "for ever after that. Nothing about it will appear on this site until somebody "
                + "claims it."),

            // Two arms, because the slug is present exactly when the game is public. A submitted
            // game nobody has claimed is not linkable, and inventing a link to its page would be
            // this filter leaking the thing it exists to hide.
            SubmissionOutcome.AlreadyListed when slug is not null => new SubmitAnswer(
                "We already have that one.",
                $"{Named(address)} is a game we already measure. Nothing was created and nothing "
                + "was changed.",
                slug),

            SubmissionOutcome.AlreadyListed => new SubmitAnswer(
                "We already have that address.",
                $"{Named(address)} is already known to us, and is not listed because nobody has "
                + "claimed it yet."),

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

            SubmissionOutcome.RefusedNotRoutable => new SubmitAnswer(
                "We will not dial that.",
                $"{Named(address)} resolves somewhere that is not the public internet, so the "
                + "answer is no — and it is no for the whole name rather than for the part we "
                + "objected to. This is our policy about our own socket. It says nothing about "
                + "whatever is at that address, and it is recorded nowhere as though it did."),

            SubmissionOutcome.Unresolvable => new SubmitAnswer(
                "That name does not resolve.",
                $"DNS has no answer for {Named(address)}, which is a fact about the world rather "
                + "than a decision of ours. Check the spelling and try it again."),

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
/// <param name="GameSlug">
/// Set only where a public game exists to link to, which is never the case for a refusal.
/// </param>
public sealed record SubmitAnswer(string Heading, string Sentence, string? GameSlug = null);

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

    public static string Token(SubmissionOutcome outcome) => outcome switch
    {
        SubmissionOutcome.Accepted => "accepted",
        SubmissionOutcome.AlreadyListed => "already-listed",
        SubmissionOutcome.AlreadyQueued => "already-queued",
        SubmissionOutcome.Malformed => "malformed",
        SubmissionOutcome.RefusedNotRoutable => "refused",
        SubmissionOutcome.Unresolvable => "unresolvable",
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
        "refused" => SubmissionOutcome.RefusedNotRoutable,
        "unresolvable" => SubmissionOutcome.Unresolvable,
        "too-many" => SubmissionOutcome.TooMany,
        _ => null,
    };
}
