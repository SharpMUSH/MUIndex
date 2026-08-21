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
/// Three sources come out of one probe and are kept apart, since <c>game_field</c> is keyed
/// <c>(game, field, source)</c>: <see cref="FieldSource.Handshake"/> for what the server actually
/// negotiated, <see cref="FieldSource.Mssp"/> for what it claims, and <see cref="FieldSource.Banner"/>
/// for what we parsed from the pre-login text. "Declared GMCP, not offered in the handshake" is only
/// expressible because these never contend for one row.
/// </remarks>
public static class FieldObservations
{
    /// <summary>The field the negotiated character encoding is stored under.</summary>
    /// <remarks>
    /// This is also where an operator's override goes, at <c>staff</c>, which outranks everything —
    /// the same shape every other hand-set value has. Nothing new was added for it.
    /// </remarks>
    public const string CharsetField = "CHARSET";

    /// <summary>The field the encoding a session's bytes were actually read with is stored under.</summary>
    /// <remarks>
    /// Not <see cref="CharsetField"/> under another source: they are not two accounts of one fact.
    /// <c>CHARSET</c> is what the session settled on; this is what the bytes proved to be — a server
    /// can negotiate UTF-8 and still send bytes that prove to be something else, and both are true at
    /// once. Folding them into one field would make the ladder pick a winner between statements that
    /// don't contend.
    /// </remarks>
    public const string CharsetReadField = "charset.read";

    /// <summary>
    /// The field a codebase is stored under, whoever read it.
    /// </summary>
    /// <remarks>
    /// Deliberately the same name MSSP uses, so a codebase parsed from a <c>VERSION</c> reply and one
    /// a game declared land in the same field with different sources: the ladder picks the declared
    /// one, but the page can still show its own <c>VERSION</c> saying something else (§5.1).
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
    /// Empty for a probe that did not answer: a failed dial saw nothing, and reconciling a stale value
    /// as if confirmed would bump <c>last_confirmed_at</c> on rows nothing confirmed. The connect
    /// screen is deliberately not here — it's written separately by
    /// <c>CatalogueBinder.AttachAsync</c>, because the reconciler's contract is that a changed value is
    /// a change-feed event, and a banner is not: one that states its own live player count would
    /// otherwise write a change-feed row every probe, burying every real event under noise. A redesign
    /// is still noticed via the <c>banner_hash</c> signal instead.
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
    /// §6.2: "version banners identify the codebase when <c>CODEBASE</c> is unset or wrong". Measured,
    /// because a <c>VERSION</c> reply is text this crawler asked for and parsed, not a field the game
    /// reported. Sits on <see cref="FieldSource.Banner"/>, the bottom of the precedence ladder: a game
    /// that declares its own codebase wins, and one that declares nothing gets a page with something
    /// on it. Unlike the connect screen this is safe to reconcile, since a change here is a game that
    /// upgraded — an event, not noise. Last, and only into an otherwise empty space, comes the one
    /// assumption: a game whose own text calls itself a MUCK is running one
    /// (<see cref="MuckNaming"/>), reached only when nothing was declared or labelled — least trusted,
    /// still ours to have read (§6.2).
    /// </remarks>
    private static IEnumerable<FieldObservation> Parsed(
        ProbeResult result,
        IReadOnlyList<FieldObservation> alreadyObserved)
    {
        // Null unless the text labelled a value or plainly named a known family. Rule 4: a parser
        // that guessed here would be inventing a fact about somebody else's server.
        //
        // The credit line is second because it is coarser, not weaker: a labelled `Codebase:` or
        // `Version:` line names an engine and release, and `Based on CircleMUD 3.0bpl10` is read as
        // `CircleMUD` on purpose (see CodebaseCredits). Both contend for the same (game, CODEBASE,
        // banner) row, and the more specific reading wins.
        if ((LoginCommandReading.MeaningfulCodebase(result.Info, result.Version)
                ?? CodebaseCredits.Named(result.Banner)) is { Length: > 0 } codebase)
        {
            yield return new FieldObservation(CodebaseField, FieldSource.Banner, codebase);
            yield break;
        }

        // The one assumption, and only where there is nothing else at all — a game that declared what
        // it runs, or its family, is never guessed over (§5.1, rule 5).
        //
        // Asked of the rows this probe is about to store, not of the report: MsspReading.Meaningful
        // answers a different question, reading CODEBASE "PennMUSH" as unset because a *name* of
        // "PennMUSH" means an unedited mush.cnf — but a codebase of PennMUSH is the answer itself.
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
    /// <remarks>
    /// Public since <see cref="I3Description"/> writes it too — one field name with one spelling,
    /// because a second copy is a place for the two to drift apart and stop reconciling.
    /// </remarks>
    public const string FamilyField = "FAMILY";

    /// <summary>
    /// Layer 1 — what the server actually negotiated (spec §6.1).
    /// </summary>
    /// <remarks>
    /// A capability is written <c>true</c> when observed and otherwise not written at all — with
    /// exactly one exception, MSSP. Writing <c>false</c> for every capability the handshake didn't
    /// produce would be tempting but wrong: TNC fires <c>OnEnabledAsync</c> unreliably for most
    /// options, so publishing "does not offer MSDP" on that strength is our own instrumentation
    /// recorded as a fact about their game (rule 5). MSSP is different because the spec says a server
    /// should send <c>IAC WILL MSSP</c> during initial negotiation, and we give every server a full
    /// connected session to do so — an absence after that is a real measurement. Servers that only
    /// respond to <c>IAC DO MSSP</c> rather than advertising it themselves will appear as
    /// <see cref="MsspOutcome.NotOffered"/> until TNC gains a client-side request.
    /// <see cref="MsspOutcome.RejectedTooLarge"/> is not that case and is recorded as present: the
    /// server offered, we just chose not to hold the reply (§6.4). A protocol the library named that
    /// isn't in <see cref="CapabilityFields.Names"/> is still recorded — the registry isn't a gate on
    /// ingestion.
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

        // MXP read off the wire rather than out of a negotiation: a server may simply start emitting
        // MXP without negotiating option 91, and the handshake sees none of that. Recorded under
        // `banner`, not `handshake` — this is text we parsed, and calling it a negotiation would put
        // our reading method into a game's record as something the server did.
        if (result.MxpObserved)
        {
            yield return new FieldObservation(
                CapabilityFields.Measured("MXP"), FieldSource.Banner, "true");
        }

        // The one honest negative — see the remarks above for the caveat about IAC DO MSSP-only servers.
        if (result.MsspOutcome is MsspOutcome.NotOffered)
        {
            yield return new FieldObservation(
                CapabilityFields.Measured(MsspCapability), FieldSource.Handshake, "false");
        }

        // What the bytes turned out to be, answered by a strict decoder rather than anybody's say-so.
        //
        // Nothing is written when the encoding is undetermined: an unexplained non-UTF-8 screen is
        // read with Latin-1 just to keep its bytes whole, and storing "iso-8859-1" for that would be
        // our own fallback recorded as a measurement (rules 4 and 5).
        //
        // The two determined cases carry different sources because they are different claims: the
        // bytes proved UTF-8 to a decoder, an override is a person's assertion. A staff row outranks
        // the handshake one, so a withdrawn override must be deleted by hand like any other.
        if (result.ReadAs is { Length: > 0 } readAs)
        {
            switch (result.CharsetSource)
            {
                case WireCharset.Proven:
                    yield return new FieldObservation(CharsetReadField, FieldSource.Handshake, readAs);
                    break;

                case WireCharset.Overridden:
                    yield return new FieldObservation(CharsetReadField, FieldSource.Staff, readAs);
                    break;

                case WireCharset.Undetermined:
                default:
                    break;
            }
        }

        if (!result.Negotiation.CharsetNegotiated || result.Negotiation.Charset is not { Length: > 0 } charset)
        {
            yield break;
        }

        // CHARSET settling is a measurement; merely *having* an encoding is not, which is what
        // CharsetNegotiated distinguishes. The game's own MSSP CHARSET lands on the same field under
        // a different source, and handshake outranks mssp.
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
    /// Nothing is filtered — every variable the server reported becomes a row, including names no
    /// specification lists. <c>PLAYERS</c> and <c>UPTIME</c> are the exception in storage rather than
    /// here: <see cref="FieldReconciler.VolatileFields"/> drops them, so that rule has one home. A
    /// variable with several values is reduced by MSSP's own rule (last value wins) and never by
    /// joining them, since a value may legitimately contain a comma and <c>string.Join</c> would
    /// fabricate one that can't be split back apart. A capability variable produces a
    /// <c>.declared</c> row, plus a descriptive row only when its value carries information beyond
    /// yes/no: <c>SSL 4202</c> is a claim and a port, <c>GMCP 1</c> is only a claim.
    /// </remarks>
    private static IEnumerable<FieldObservation> Declared(ProbeResult result)
    {
        if (result.MsspOutcome is not MsspOutcome.Received)
        {
            // Neither NotOffered nor RejectedTooLarge is a game withdrawing its answers.
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
