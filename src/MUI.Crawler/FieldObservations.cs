using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawl;
using MUI.Discovery;

namespace MUI.Crawler;

/// <summary>
/// Everything one probe observed about a game's descriptive fields, in the vocabulary
/// <see cref="IFieldReconciler"/> stores (spec §5.1, §6.1).
/// </summary>
/// <remarks>
/// <para>
/// Three sources come out of one probe and they are kept apart, because <c>game_field</c> is keyed
/// <c>(game, field, source)</c> and the capability matrix's two columns are the whole point:
/// <see cref="FieldSource.Handshake"/> for what the server actually negotiated,
/// <see cref="FieldSource.Mssp"/> for what it claims about itself, and
/// <see cref="FieldSource.Banner"/> for what we parsed out of the text it painted before login.
/// "Declared GMCP, not offered in the handshake" is a fact the site exists to publish, and it is only
/// expressible because these never contend for one row.
/// </para>
/// </remarks>
public static class FieldObservations
{
    /// <summary>The field the negotiated character encoding is stored under.</summary>
    public const string CharsetField = "CHARSET";

    /// <summary>
    /// The field a codebase is stored under, whoever read it.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately the same name MSSP uses</b>, so that a codebase we parsed out of a
    /// <c>VERSION</c> reply and one a game declared land in the same field with different sources.
    /// That is the whole point of keying <c>(game, field, source)</c>: the ladder picks the declared
    /// one, and the page can still show that its own <c>VERSION</c> says something else — which is a
    /// fact worth publishing rather than a conflict to hide (§5.1).
    /// </remarks>
    public const string CodebaseField = "CODEBASE";

    /// <summary>The MSSP capability whose absence is itself a measurement. See <see cref="Measured"/>.</summary>
    public const string MsspCapability = "MSSP";

    /// <summary>
    /// MSSP values that mean "no". Everything else non-blank means yes, including a port number:
    /// <c>SSL 4202</c> is a game saying it has TLS and where.
    /// </summary>
    private static readonly HashSet<string> Falsehoods =
        new(StringComparer.OrdinalIgnoreCase) { "0", "-1", "no", "false", "off", "none" };

    /// <summary>
    /// Every field this probe observed that goes through the reconciler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Empty for a probe that did not answer — a failed dial saw nothing, and handing its (necessarily
    /// empty) observations to the reconciler would be worse than useless: reconciling an absent value
    /// is silent, but reconciling a <em>stale</em> one would bump <c>last_confirmed_at</c> on rows
    /// nothing confirmed, which is a probe of ours reported as a measurement of theirs.
    /// </para>
    /// <para>
    /// <b>The connect screen is deliberately not here.</b> It is written by
    /// <c>CatalogueBinder.AttachAsync</c>, beside the banner fingerprint it is the other half of,
    /// because the reconciler's contract is that a changed value is a change-feed <em>event</em> — and
    /// a banner is not. Measured on <c>aardmud.org:4000</c>, whose connect screen states its own live
    /// player count, so a first cycle and a second one apart wrote
    /// "connect_screen changed from ####… to ####…" and would have done so for ever, at one row per
    /// probe per game. That is exactly the cost §5.1 exists to avoid, and it would have buried every
    /// real event under a diff nobody can read. A redesign is still noticed: that is what the
    /// <c>banner_hash</c> signal is for.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<FieldObservation> From(ProbeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Outcome is not ProbeOutcome.Answered)
        {
            return [];
        }

        var observations = new List<FieldObservation>();

        observations.AddRange(Measured(result));
        observations.AddRange(Declared(result));

        // Reading last, and knowing what the other two produced: its one assumption is withdrawn by
        // anything the game said about its own codebase, and that is exactly the rows above.
        observations.AddRange(Parsed(result, observations));

        return observations;
    }

    /// <summary>
    /// Layer 2 — what we read out of the free text the server painted before login (spec §6.2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// §6.2 asks for this in as many words: "version banners identify the codebase when
    /// <c>CODEBASE</c> is unset or wrong", and it is measured, because a <c>VERSION</c> reply is text
    /// this crawler asked for and parsed on this probe rather than a field the game reported. The
    /// value sits on <see cref="FieldSource.Banner"/>, which is the bottom of the precedence ladder
    /// and exactly where a conservatively-parsed free-form string belongs: a game that declares its
    /// own codebase wins, and one that declares nothing gets a page with something on it.
    /// </para>
    /// <para>
    /// <b>The reading itself is not new; its only consumer was.</b>
    /// <see cref="LoginCommandReading.MeaningfulCodebase"/> has been deciding this since <c>INFO</c>
    /// and <c>VERSION</c> were added to the probe, and the only caller was
    /// <c>IdentityMatcher</c>, which used it to tell two games apart and then dropped it. Of the 409
    /// games in the first real crawl, 281 answered the socket and published no MSSP at all — for
    /// every one of those, this is the difference between a page that names what they run and a page
    /// that does not.
    /// </para>
    /// <para>
    /// Unlike the connect screen (see above) this is safe to reconcile: the value is a codebase name,
    /// not a screen with a player count in it, so a change here is a game that upgraded — an event
    /// worth a change-feed row rather than noise that buries them.
    /// </para>
    /// <para>
    /// <b>Last of all, and only into an otherwise empty space, comes the one assumption:</b> a game
    /// whose own text calls itself a MUCK is running one (<see cref="MuckNaming"/>). It is reached
    /// only when nothing was declared and nothing was labelled, because the MUCK line is the family
    /// least able to answer either question — no MSSP, no <c>INFO</c>, no labelled <c>VERSION</c> —
    /// and 21 of the 23 games in the catalogue that say <c>MUCK</c> in their own words carried no
    /// codebase whatsoever. It sits on the same rung as the reading above it: least trusted, still
    /// ours to have read (§6.2).
    /// </para>
    /// </remarks>
    private static IEnumerable<FieldObservation> Parsed(
        ProbeResult result,
        IReadOnlyList<FieldObservation> alreadyObserved)
    {
        // Null unless the text labelled a value or plainly named a known family. Rule 4: a parser
        // that guessed here would be inventing a fact about somebody else's server.
        if (LoginCommandReading.MeaningfulCodebase(result.Info, result.Version) is { Length: > 0 } codebase)
        {
            yield return new FieldObservation(CodebaseField, FieldSource.Banner, codebase);
            yield break;
        }

        // The one assumption, and only where there is nothing else at all. A game that declared what
        // it runs — or a family, which is the same answer coarsened — is never guessed over: our
        // value would lose to theirs on the ladder anyway, and all it could do on the way is turn a
        // guess of ours into a published disagreement with their own report (§5.1, rule 5).
        //
        // Asked of the rows this probe is about to store rather than of the report, because that is
        // the question: MsspReading.Meaningful answers a different one, reading CODEBASE "PennMUSH"
        // as unset because a *name* of "PennMUSH" is somebody who never edited mush.cnf — and a
        // codebase of PennMUSH is the answer itself.
        if (alreadyObserved.Any(o =>
                o.Field.Equals(CodebaseField, StringComparison.OrdinalIgnoreCase)
                || o.Field.Equals(FamilyField, StringComparison.OrdinalIgnoreCase)))
        {
            yield break;
        }

        if (MuckNaming.Assumed(
                result.Banner,
                result.Info,
                result.Version,
                MsspReading.MeaningfulName(result.Mssp),
                result.Host)
            is { } muck)
        {
            yield return new FieldObservation(CodebaseField, FieldSource.Banner, muck);
        }
    }

    /// <summary>MSSP's coarse taxonomy, which answers the same question <c>CODEBASE</c> does.</summary>
    private const string FamilyField = "FAMILY";

    /// <summary>
    /// Layer 1 — what the server actually negotiated (spec §6.1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A capability is written <c>true</c> when it was observed and is otherwise not written at
    /// all. There is exactly one exception, and it is MSSP.</b> The temptation is to write
    /// <c>false</c> for every capability the handshake did not produce, because that is what makes
    /// "declared GMCP, never offered" render as a disagreement. It would be a lie: the MSSP plugin
    /// is registered and TNC responds to <c>IAC WILL MSSP</c> when the server sends it, but for
    /// other options TNC fires <c>OnEnabledAsync</c> unreliably — measured not firing on live servers
    /// where the payload callbacks did. Publishing "Alter Aeon does not offer MSDP" on the strength
    /// of that is our own instrumentation recorded as a fact about their game, which rule 5 forbids
    /// in the same breath as an unparseable <c>WHO</c> read as zero.
    /// </para>
    /// <para>
    /// MSSP is different because the MSSP spec says a server "should send <c>IAC WILL MSSP</c> during
    /// the initial negotiation of the telnet session". We give every server a full connected session
    /// to do so. A server that connected, exchanged telnet options, and never sent <c>IAC WILL MSSP</c>
    /// has had its opportunity: that absence is a measurement. Note that servers which only respond to
    /// <c>IAC DO MSSP</c> (never sending <c>IAC WILL MSSP</c> on their own) will appear as
    /// <see cref="MsspOutcome.NotOffered"/> until TelnetNegotiationCore gains a client-side
    /// <c>RequestMSSPAsync()</c>. <see cref="MsspOutcome.RejectedTooLarge"/> is emphatically
    /// <b>not</b> that case and is recorded as present: the server offered, we accepted the handshake,
    /// and we chose not to hold the reply (§6.4).
    /// </para>
    /// <para>
    /// A protocol the library named that is not in <see cref="CapabilityFields.Names"/> is still
    /// recorded, under the same naming convention. The registry is not a gate on ingestion, and a
    /// crawler whose premise is faithful measurement does not get to drop an observation because no
    /// column of the matrix renders it.
    /// </para>
    /// </remarks>
    private static IEnumerable<FieldObservation> Measured(ProbeResult result)
    {
        foreach (var protocol in result.OfferedOptions.Order(StringComparer.Ordinal))
        {
            yield return new FieldObservation(
                CapabilityFields.Measured(CapabilityFields.Canonical(protocol) ?? protocol),
                FieldSource.Handshake,
                "true");
        }

        // The one honest negative. The server had a full connected session to send IAC WILL MSSP
        // and did not — that absence is a measurement. See the Measured remarks for the caveat
        // about servers that only answer IAC DO MSSP rather than advertising it themselves.
        if (result.MsspOutcome is MsspOutcome.NotOffered)
        {
            yield return new FieldObservation(
                CapabilityFields.Measured(MsspCapability), FieldSource.Handshake, "false");
        }

        if (!result.Negotiation.CharsetNegotiated || result.Negotiation.Charset is not { Length: > 0 } charset)
        {
            yield break;
        }

        // CHARSET settling is a measurement; the interpreter merely *having* an encoding is not, which
        // is what CharsetNegotiated distinguishes. The game's own MSSP CHARSET lands on the same field
        // under a different source, and handshake outranks mssp — which is the ladder working.
        yield return new FieldObservation(CharsetField, FieldSource.Handshake, charset);

        if (string.Equals(charset, "utf-8", StringComparison.OrdinalIgnoreCase))
        {
            yield return new FieldObservation(
                CapabilityFields.Measured("UTF-8"), FieldSource.Handshake, "true");
        }
    }

    /// <summary>
    /// Layer 4 — everything the game claims about itself, exactly as it said it (spec §6.4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Nothing is filtered.</b> Every variable the server reported becomes a row, including names
    /// no specification lists — a protocol whose entire purpose is servers describing themselves does
    /// not get an allow-list, and a field declined at the socket cannot be reconsidered downstream.
    /// <c>PLAYERS</c> and <c>UPTIME</c> are the exception in <em>storage</em> rather than here:
    /// <see cref="FieldReconciler.VolatileFields"/> drops them, so that rule has one home and this
    /// projection does not acquire a second copy of it.
    /// </para>
    /// <para>
    /// <b>A variable with several values is reduced by MSSP's own rule</b> — "the last reported value
    /// should be used as the default value" — and never by joining them. A value may legitimately
    /// contain a comma, so <c>string.Join</c> would manufacture something that looks like a value and
    /// cannot be split back apart, which is a fabrication and the rule against those does not stop at
    /// player counts. The variables where every value matters are read from
    /// <see cref="ProbeResult.Mssp"/> directly by the code that cares: <c>REFERRAL</c> by
    /// <c>MsppReferrals</c>, and the descriptive row is not where a game's ports live —
    /// <c>game_endpoint</c> is (§5.5).
    /// </para>
    /// <para>
    /// A capability variable produces a <c>.declared</c> row, and additionally a descriptive row when
    /// its value carries information beyond the yes: <c>SSL 4202</c> is a claim and a port,
    /// <c>GMCP 1</c> is only a claim, and listing the second among a game's descriptive fields would
    /// be the capability matrix restated as noise.
    /// </para>
    /// </remarks>
    private static IEnumerable<FieldObservation> Declared(ProbeResult result)
    {
        if (result.MsspOutcome is not MsspOutcome.Received)
        {
            // NotOffered says nothing, and RejectedTooLarge says only that we declined to hold what
            // arrived. Neither is a game withdrawing its answers, so neither writes one.
            yield break;
        }

        foreach (var (variable, values) in result.Mssp)
        {
            if (values.Count == 0)
            {
                continue;
            }

            var name = variable.Trim();
            var value = values[^1].Trim();

            if (CapabilityFields.Canonical(name) is { } capability)
            {
                yield return new FieldObservation(
                    CapabilityFields.Declared(capability), FieldSource.Mssp, IsTrue(value) ? "true" : "false");

                if (IsBare(value))
                {
                    continue;
                }
            }

            if (name.Length > 0 && value.Length > 0)
            {
                yield return new FieldObservation(name, FieldSource.Mssp, value);
            }
        }
    }

    /// <summary>MSSP's yes/no reading. Anything not an explicit no is a yes, including a port number.</summary>
    private static bool IsTrue(string value) => value.Length > 0 && !Falsehoods.Contains(value);

    /// <summary>Whether a value says nothing beyond yes or no, and so needs no descriptive row.</summary>
    private static bool IsBare(string value) =>
        value.Length == 0
        || Falsehoods.Contains(value)
        || string.Equals(value, "1", StringComparison.Ordinal)
        || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
