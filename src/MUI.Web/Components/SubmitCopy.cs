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
        "Tell us where a game is. A host and a port is the whole form; everything else on this site "
        + "is measured by our own crawler.";

    /// <summary>What happens to an address after it is submitted, in the order it happens.</summary>
    /// <remarks>
    /// The third point is the one people are surprised by, so it is stated on the form rather than
    /// discovered afterwards: a submission does not put a listing up.
    /// </remarks>
    public static IReadOnlyList<string> Points { get; } =
    [
        "We resolve the address before dialling it, and refuse anything that resolves off the public "
        + "internet. That is a decision about our own socket, never a fact about a game.",

        "If whoever runs that host has asked us not to crawl it, we will not take the address, "
        + "whoever submits it. A stranger cannot put your game back on this site.",

        "If it answers, we read what the server says for itself and keep reading it on its own "
        + "schedule, for ever. An address only has to be given once.",

        "Nothing appears on the site until somebody proves they run it. Claiming takes a passkey and "
        + "one line published on the game itself.",

        "An address we already have collapses onto the existing entry. Sending it twice makes no "
        + "second listing and brings no probe forward.",
    ];

    /// <summary>The label on each box, so the rendered form and the plain one cannot drift.</summary>
    public const string HostLabel = "Host";

    public const string PortLabel = "Port";

    public const string HostHint = "mud.example.org, or paste mud.example.org:4201 and leave the port empty";

    public const string SubmitLabel = "Submit";

    /// <summary>What the site says when it has no database and therefore no registry to write into.</summary>
    public const string NoCatalogue =
        "Submitting needs a database, and this site is running on the demo fixture. There is no "
        + "crawl registry to write into, so the form is absent rather than quietly doing nothing.";

    /// <summary>The answer to one submission, or null when nothing has been submitted yet.</summary>
    public static SubmitAnswer? Answer(SubmissionOutcome? outcome, string? address, SubmitLink? link = null) =>
        outcome switch
        {
            null => null,

            SubmissionOutcome.Accepted => new SubmitAnswer(
                "In the registry.",
                $"{Named(address)} will be dialled on the next crawl cycle, then on its own schedule "
                + "for ever. It appears here once somebody proves they run it — come back to this "
                + "form with the same address and it will hand you the link."),

            // Two arms, and the link is what differs. A game we list gets its page; a submitted one
            // nobody has claimed gets its claim page, because that is the only exit from hidden and
            // a person who has just told us the address is exactly who should be offered it.
            SubmissionOutcome.AlreadyListed when link?.IsClaim is true => new SubmitAnswer(
                "We have it, unclaimed.",
                $"{Named(address)} is one we already measure. It stays off the site until somebody "
                + "proves they run it. If that is you, this is the way in.",
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
                $"{Named(address)} is in the crawl registry and has not answered yet. Sending it "
                + "again does not bring it forward: a target keeps its own schedule, so nobody can "
                + "hurry us at somebody else's server."),

            SubmissionOutcome.Malformed => new SubmitAnswer(
                "Not an address we can dial.",
                "A host needs a dot or a colon in it, and a port is a number between 1 and 65535. "
                + "Fill in both boxes, or paste mud.example.org:4201 into the first."),

            // ONE SENTENCE FOR ALL THREE, AND THE VAGUENESS IS THE POINT. §7.2 keeps "did not
            // resolve" and "resolved somewhere we will not go" apart because they are two facts and
            // our own record has to hold them apart, and §11's opt-out is a third — but telling a
            // *stranger* which of them happened turns this form into a scanner of whatever the
            // crawler can see. Submit internal.corp.example: one answer means it exists on our side
            // of a split horizon and another means it does not, and a few hundred guesses is a map
            // of somebody's network drawn from outside it. The same enumeration works on the opt-out
            // register with a list of hostnames. The log knows which; the page does not say.
            //
            // THE REASONS ARE LISTED AND THE ANSWER IS NOT, WHICH IS NOT A CONTRADICTION. Knowing
            // that three things can produce this sentence tells a reader nothing about which one
            // did, and leaving them unlisted would make an honest refusal look like a malfunction.
            SubmissionOutcome.RefusedNotRoutable
                or SubmissionOutcome.Unresolvable
                or SubmissionOutcome.RefusedOptOut => new SubmitAnswer(
                "We cannot dial that.",
                $"Three things produce this answer for {Named(address)}: the name may not resolve, "
                + "it may resolve off the public internet, or whoever runs that host may have asked "
                + "us to stay away. We deliberately do not say which, because answering that for a "
                + "stranger maps a network from outside it. Nothing was recorded about the address; "
                + "the decision was ours and it is filed as ours."),

            SubmissionOutcome.TooMany => new SubmitAnswer(
                "Enough for now.",
                "This form is rate-limited by sender, and you have hit the bound. Come back in an "
                + "hour. Nothing was lost — anything we took is already in the registry."),

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
        SubmissionOutcome.RefusedNotRoutable
            or SubmissionOutcome.Unresolvable
            or SubmissionOutcome.RefusedOptOut => "undialable",
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
