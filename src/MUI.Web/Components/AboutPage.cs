using MUI.Crawl;
using MUI.Discovery;
using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>
/// The about page, as one view model both surfaces render.
/// </summary>
/// <remarks>
/// Spec §11: publish who is knocking and how to make it stop, and state the limits of what a
/// measurement here proves. The prose is data because both surfaces have to carry it identically —
/// see <see cref="PlainText"/> — so the page is a list of sections and the parity test reads words
/// rather than tags.
/// <para>
/// <b>Nothing here is written from the design document alone.</b> Stating the design's intentions as
/// the deployment's actual behaviour is the shape of the <c>ContactedMaintainer</c> defect this
/// repository already has a record of — a claim about the world compiled in by whoever typed it.
/// </para>
/// <para>
/// The opt-out's spellings are read off <see cref="OptOutVocabulary"/> rather than typed into this
/// file, since those are the values <see cref="MUI.Discovery.OptOutGate"/> actually reads — a page
/// advertising a switch wired to nothing would be worse than admitting there was none. The prose
/// itself is read out of <see cref="Messages"/> rather than typed here, since this page states the
/// rules everything else is built from and is the worst one to leave untranslated.
/// </para>
/// </remarks>
public sealed record AboutPage(string Lede, IReadOnlyList<AboutSection> Sections)
{
    /// <summary>
    /// Builds the page from the one thing about it a deployment can change.
    /// </summary>
    /// <param name="probe">
    /// The crawler's own options, so the identity published here is the object the probe is
    /// constructed from rather than a copy of it that can drift.
    /// </param>
    /// <param name="tag">The locale this page is being answered in.</param>
    public static AboutPage Build(ProbeOptions probe, string tag = Locales.SourceTag) => new(
        Say(tag, "about.lede"),
        [
            Measures(tag),
            Limits(tag),
            Never(tag),
            Crawler(tag, probe),
        ]);

    private static AboutSection Measures(string tag) => new(
        "measures",
        Say(tag, "about.measures.heading"),
        [
            Point(tag, "about.measures.declared"),
            Point(tag, "about.measures.count"),
            Point(tag, "about.measures.unknown"),
            Point(tag, "about.measures.reachable"),
            Point(tag, "about.measures.hour"),
        ]);

    private static AboutSection Limits(string tag) => new(
        "limits",
        Say(tag, "about.limits.heading"),
        [
            Point(tag, "about.limits.grace"),
            Point(tag, "about.limits.created"),
            Point(tag, "about.limits.claim"),
            Point(tag, "about.limits.oneHost"),
            Point(tag, "about.limits.deletion"),
        ]);

    private static AboutSection Never(string tag) => new(
        "never",
        Say(tag, "about.never.heading"),
        [
            Point(tag, "about.never.votes"),
            Point(tag, "about.never.forums"),
            Point(tag, "about.never.names"),
            Point(tag, "about.never.population"),
        ]);

    private static AboutSection Crawler(string tag, ProbeOptions probe) => new(
        "crawler",
        Say(tag, "about.crawler.heading"),
        [
            Point(tag, "about.crawler.probe",
                ("commands", string.Join(", ", TelnetProbe.PermittedCommands))),
            Point(tag, "about.crawler.delay"),
            Point(tag, "about.crawler.referral"),
            Point(tag, "about.crawler.screens"),
            Point(tag, "about.crawler.stop",
                ("variable", OptOutVocabulary.MsspVariable),
                ("label", OptOutVocabulary.DnsLabel),
                ("value", OptOutVocabulary.DnsValue)),
            Point(tag, "about.crawler.scope", ("value", OptOutVocabulary.DnsValue)),
            Point(tag, "about.crawler.dns"),
            Point(tag, "about.crawler.stopping"),
            Point(tag, "about.crawler.unlist"),
        ])
    {
        Identity = AboutIdentity.For(probe),
    };

    /// <summary>
    /// One point, from the pair of ids its two halves live under.
    /// </summary>
    /// <remarks>
    /// The <c>.lead</c>/<c>.body</c> suffixes are always translated together — one sentence a
    /// renderer is allowed to set in two weights.
    /// </remarks>
    private static AboutPoint Point(string tag, string id, params (string Key, object? Value)[] args) =>
        new(Messages.For(tag, $"{id}.lead"),
            Messages.For(tag, $"{id}.body", args.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal)));

    private static string Say(string tag, string id) => Messages.For(tag, id);
}

/// <summary>One headed run of prose, plus whatever structured block belongs under it.</summary>
/// <remarks>
/// The extras are nullable rather than a separate ordered list, so both renderers walk one sequence
/// with no if-ladder deciding where a block goes.
/// </remarks>
public sealed record AboutSection(string Id, string Heading, IReadOnlyList<AboutPoint> Points)
{
    /// <summary>Who the crawler says it is, when this section is the one about the crawler.</summary>
    public AboutIdentity? Identity { get; init; }
}

/// <summary>
/// A lead-in and the paragraph it introduces.
/// </summary>
/// <remarks>
/// Split rather than one string, since the graphical page sets the lead in bold and the plain page
/// cannot — the two surfaces read identically aloud.
/// </remarks>
public sealed record AboutPoint(string Lead, string Body)
{
    public string Sentence => $"{Lead} {Body}";
}

/// <summary>
/// What a server administrator sees when we knock, and what to do about it.
/// </summary>
/// <remarks>
/// Read off <see cref="ProbeOptions"/> rather than written out here, so the name published is a
/// property of the object the probe is built from.
/// </remarks>
public sealed record AboutIdentity(string Name, string InfoUrl, bool Announced, bool ContactConfigured)
{
    public static AboutIdentity For(ProbeOptions probe) => new(
        probe.TerminalTypes.Count > 0 ? probe.TerminalTypes[0] : "MUINDEX-CRAWLER",
        probe.InfoUrl,
        Announced: true,
        // The built-in value is a placeholder on an unchosen domain — publishing it unmarked would
        // be publishing an address that answers nobody.
        ContactConfigured: probe.InfoUrl != new ProbeOptions().InfoUrl);

    /// <summary>The honest version of "who is this in my logs", in one sentence.</summary>
    /// <remarks>
    /// Two whole messages rather than one with a branch in it — they share nothing but the name,
    /// which is an argument in both since a deployment that configures its own has to see it here.
    /// </remarks>
    public string Wording(string tag = Locales.SourceTag) => Messages.For(
        tag,
        Announced ? "about.identity.announced" : "about.identity.unannounced",
        new Dictionary<string, object?> { ["name"] = Name });
}
