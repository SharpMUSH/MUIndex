using System.Globalization;

using MUI.Discovery;
using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>
/// The words on the submission form, and the one sentence it answers with.
/// </summary>
/// <remarks>
/// <para>
/// Here rather than in the page because the plain surface renders the same words (spec §9), and a
/// form whose refusal reads differently in a text browser is a form with two policies. Nothing here
/// touches a database or a socket: this maps a <see cref="SubmissionOutcome"/> to a message id, and
/// <see cref="SubmissionService"/> is what decides.
/// </para>
/// <para>
/// <b>Every member takes a locale, and the address is an ICU argument rather than a prefix.</b> The
/// answers used to open with the address glued to the front of an English sentence, which is a fact
/// about English word order and not about addresses — a language that puts the subject elsewhere had
/// nowhere to say so. The default is the source language, so a caller with no request behind it
/// still gets a sentence.
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
    public static string Title(string tag = Locales.SourceTag) => Messages.Say(tag, "submit.title");

    public static string Lede(string tag = Locales.SourceTag) => Messages.Say(tag, "submit.lede");

    /// <summary>What happens to an address after it is submitted, in the order it happens.</summary>
    /// <remarks>
    /// The third point is the one people are surprised by, so it is stated on the form rather than
    /// discovered afterwards: a submission does not put a listing up.
    /// </remarks>
    public static IReadOnlyList<string> Points(string tag = Locales.SourceTag) =>
    [
        Messages.Say(tag, "submit.what.resolve"),
        Messages.Say(tag, "submit.what.optOut"),
        Messages.Say(tag, "submit.what.schedule"),
        Messages.Say(tag, "submit.what.claim"),
        Messages.Say(tag, "submit.what.duplicate"),
    ];

    /// <summary>The label on each box, so the rendered form and the plain one cannot drift.</summary>
    public static string HostLabel(string tag = Locales.SourceTag) => Messages.Say(tag, "submit.host.label");

    public static string PortLabel(string tag = Locales.SourceTag) => Messages.Say(tag, "submit.port.label");

    public static string HostHint(string tag = Locales.SourceTag) => Messages.Say(tag, "submit.host.hint");

    public static string SubmitLabel(string tag = Locales.SourceTag) => Messages.Say(tag, "submit.button");

    /// <summary>What the site says when it has no database and therefore no registry to write into.</summary>
    public static string NoCatalogue(string tag = Locales.SourceTag) => Messages.Say(tag, "submit.noCatalogue");

    /// <summary>The answer to one submission, or null when nothing has been submitted yet.</summary>
    public static SubmitAnswer? Answer(
        SubmissionOutcome? outcome,
        string? address,
        SubmitLink? link = null,
        string tag = Locales.SourceTag) =>
        outcome switch
        {
            null => null,

            SubmissionOutcome.Accepted => new SubmitAnswer(
                Messages.Say(tag, "submit.accepted.heading"),
                Messages.Say(tag, "submit.accepted.sentence", ("address", Named(tag, address)))),

            // Two arms, and the link is what differs. A game we list gets its page; a submitted one
            // nobody has claimed gets its claim page, because that is the only exit from hidden and
            // a person who has just told us the address is exactly who should be offered it.
            SubmissionOutcome.AlreadyListed when link?.IsClaim is true => new SubmitAnswer(
                Messages.Say(tag, "submit.unclaimed.heading"),
                Messages.Say(tag, "submit.unclaimed.sentence", ("address", Named(tag, address))),
                link),

            SubmissionOutcome.AlreadyListed when link is not null => new SubmitAnswer(
                Messages.Say(tag, "submit.known.heading"),
                Messages.Say(tag, "submit.known.sentence", ("address", Named(tag, address))),
                link),

            SubmissionOutcome.AlreadyListed => new SubmitAnswer(
                Messages.Say(tag, "submit.knownAddress.heading"),
                Messages.Say(tag, "submit.knownAddress.sentence", ("address", Named(tag, address)))),

            SubmissionOutcome.AlreadyQueued => new SubmitAnswer(
                Messages.Say(tag, "submit.queued.heading"),
                Messages.Say(tag, "submit.queued.sentence", ("address", Named(tag, address)))),

            SubmissionOutcome.Malformed => new SubmitAnswer(
                Messages.Say(tag, "submit.malformed.heading"),
                Messages.Say(tag, "submit.malformed.sentence")),

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
                Messages.Say(tag, "submit.undialable.heading"),
                Messages.Say(tag, "submit.undialable.sentence", ("address", Named(tag, address)))),

            SubmissionOutcome.TooMany => new SubmitAnswer(
                Messages.Say(tag, "submit.tooMany.heading"),
                Messages.Say(tag, "submit.tooMany.sentence")),

            _ => null,
        };

    /// <summary>
    /// The address as the sentence names it — or the noun phrase that stands where one would.
    /// </summary>
    /// <remarks>
    /// Its own id rather than a fragment of every sentence that can take it: it is a noun phrase
    /// filling a hostname's slot, and it declines in the languages that decline.
    /// </remarks>
    private static string Named(string tag, string? address) =>
        address is { Length: > 0 } ? address : Messages.Say(tag, "submit.answer.thatAddress");
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
    /// <summary>
    /// A game's page, labelled with its own address.
    /// </summary>
    /// <remarks>
    /// The label is the URL, which is machine voice and takes no locale: a reader following it is
    /// being shown where they are about to go, and translating a path would be inventing one.
    /// </remarks>
    public static SubmitLink Game(string slug) => new($"/g/{slug}", $"/g/{slug}");

    public static SubmitLink Claim(string slug, string tag = Locales.SourceTag) =>
        new($"/g/{slug}/claim", Messages.For(tag, "submit.link.claim"), IsClaim: true);
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
