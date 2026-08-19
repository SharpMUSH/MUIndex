using System.Text.Json;
using MUI.Catalog;
using MUI.Crawl;

using MUI.Catalog.Persistence;

namespace MUI.Discovery;

/// <summary>
/// The weighted signals of spec §7.3, and the two thresholds they are compared against.
/// </summary>
/// <remarks>
/// Spec §15.5: these are unvalidated conservative defaults pending calibration against real data;
/// <see cref="DiscoveryOptions"/> is what a deployment tunes. Re-run <c>IdentityCorpusTests</c> after
/// any change.
/// </remarks>
public static class IdentityWeights
{
    /// <summary>A previously-seen (host, port). Strongest: direct continuity, and on its own enough to merge.</summary>
    public const double Endpoint = 1.00;

    /// <summary>
    /// A bare-IP probe's address, at the port a candidate's own known endpoint resolves to — the same
    /// (host, port) <see cref="Endpoint"/> asserts, read off a hostname instead of a literal string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exists because I3 seeds by bare IP (spec §7.6): a probe dialling that literal address can never
    /// match <see cref="Endpoint"/>'s string comparison against a game already on record under its DNS
    /// hostname (<c>45.79.224.33</c> is not the string <c>nightfall.org</c>), which in production
    /// produced shadow listings stuck in review with the game's traffic split across two pages.
    /// </para>
    /// <para>
    /// <b>Equal to <see cref="Endpoint"/> deliberately, and only that literal.</b> It resolves the
    /// candidate's own recorded hostname and asks whether it names the same (address, port) this probe
    /// reached by number — the same fact as a literal string match, observed through a resolver
    /// instead of equality, since two listeners cannot share one address and port.
    /// </para>
    /// <para>
    /// <b>Cannot manufacture a candidate on its own</b> — the safety property that matters most. This
    /// is only evaluated for a candidate <see cref="IdentityMatcher"/> already gathered through
    /// another signal, so a stranger cannot force a merge merely by dialling from an address that
    /// happens to resolve the same as somebody else's hostname.
    /// </para>
    /// </remarks>
    public const double ResolvedEndpoint = Endpoint;

    /// <summary>
    /// MSSP <c>NAME</c> together with <c>CREATED</c>. Both, because a name alone collides: "Fantasy
    /// MUD", "The Realm" and "Midnight Sun" are each several games, and <c>CREATED</c> is a year and
    /// therefore collides freely on its own too.
    /// </summary>
    public const double MsspNameAndCreated = 0.60;

    /// <summary>The connect screen's fingerprint. Survives a host move; changes on redesign.</summary>
    public const double BannerHash = 0.50;

    /// <summary>
    /// <c>WEBSITE</c> or <c>CONTACT</c>. Stable, and rarely coincidental — but shared across every game
    /// on a hosting provider, and across both games of one person who runs two, often enough that it
    /// reaches review and never a merge on its own.
    /// </summary>
    public const double WebsiteOrContact = 0.40;

    /// <summary><c>CODEBASE</c> and its version. Weak alone; useful only as corroboration.</summary>
    public const double CodebaseAndVersion = 0.15;

    /// <summary>The site-issued claim token (spec §7.3, §8). Decisive when present.</summary>
    /// <remarks>
    /// Ten times the auto-merge threshold, so one matching token merges two games with nothing else
    /// agreeing — §7.3's "a claimed game is never duplicated". <b>The token is not a secret</b>: every
    /// §8 channel publishes it, so on a bare value comparison anyone can read a claimed game's MSSP,
    /// republish the token from their own host, and be absorbed into that listing. Narrowing this
    /// (token only counts on the game's known endpoints, or once those stop answering) belongs with
    /// the half of §8 that issues and verifies tokens — not in scope yet. Until then no verified token
    /// ever reaches <c>claim_token</c>, so this signal never fires, which is correct rather than
    /// degraded.
    /// </remarks>
    public const double ClaimToken = 10.0;

    // Spec §15.5 — open question. These thresholds are reasoned but unvalidated: they need calibration
    // against real data, so they ship conservative and DiscoveryOptions is what a deployment tunes.

    /// <summary>At or above this, merge. Equal to <see cref="Endpoint"/>: a known endpoint <em>is</em> the game.</summary>
    public const double AutoMergeThreshold = 1.00;

    /// <summary>At or above this, open a review pair. Below it, a new game.</summary>
    /// <remarks>
    /// <b>Set equal to <see cref="WebsiteOrContact"/> on purpose</b> — the review band opens at
    /// exactly the weakest signal §7.3 calls "stable, and rarely coincidental"; a shared codebase
    /// (0.15) does not, or the review queue would be the whole catalogue. Costs a review pair per
    /// pair of games behind one hosting provider's contact address — the cheapest error this system
    /// makes, since both pages stay live and nothing is hidden.
    /// </remarks>
    public const double ReviewThreshold = WebsiteOrContact;
}

/// <summary>One weighted signal and whether it fired, kept whether it fired or not.</summary>
/// <remarks>
/// The losing signals are carried deliberately: a review is a judgement a person makes, and "which of
/// the six were considered and how did each land" is the whole content of that judgement.
/// </remarks>
public sealed record IdentitySignal(string Name, double Weight, bool Matched);

/// <summary>How well one probe matched one candidate game.</summary>
public sealed record IdentityScore(
    Guid? CandidateGameId,
    double Score,
    IReadOnlyList<IdentitySignal> Signals);

/// <summary>What to do about it (spec §7.3).</summary>
public abstract record IdentityVerdict
{
    private IdentityVerdict()
    {
    }

    /// <summary>Above threshold: this probe is that game. The endpoint change is recorded as a FieldChange.</summary>
    public sealed record Merge(Guid GameId, IdentityScore Score) : IdentityVerdict;

    /// <summary>
    /// Middling: open a suspected-duplicate pair. Both pages stay live and link to each other
    /// reciprocally, because a wrongly hidden game is worse than a visible duplicate.
    /// </summary>
    public sealed record Review(Guid GameId, IdentityScore Score) : IdentityVerdict;

    /// <summary>Below threshold: a new game. <paramref name="Best"/> is null when there was no candidate at all.</summary>
    public sealed record Fresh(IdentityScore? Best) : IdentityVerdict;
}

/// <summary>The stored field names the matcher compares on.</summary>
/// <remarks>
/// <see cref="ClaimToken"/> here is a <em>field name</em> and keeps that spelling; the type that reads
/// a beacon off a probe is <see cref="ClaimTokenBeacon"/>.
/// </remarks>
public static class IdentityFields
{
    public const string Name = "name";
    public const string Created = "created";
    /// <summary>The same name the catalogue keeps off the public page; one spelling, one decision.</summary>
    public const string BannerHash = InternalFields.BannerHash;
    public const string Website = "website";
    public const string Contact = "contact";
    public const string Codebase = "codebase";
    public const string ClaimToken = "claim_token";

    /// <summary>The pseudo-field a moved connection address is recorded under in the change feed.</summary>
    public const string Endpoint = "endpoint";
}

/// <summary>The MSSP variables the identity signals are read from.</summary>
public static class IdentityMsspVariables
{
    public const string Name = "NAME";
    public const string Created = "CREATED";
    public const string Website = "WEBSITE";
    public const string Contact = "CONTACT";
    public const string Codebase = "CODEBASE";
}

/// <summary>
/// Where a site-issued claim token may be <em>seen on the wire</em>, and how to read it off a probe
/// (spec §8, §7.3's beacon).
/// </summary>
/// <remarks>
/// <para>
/// <b>Reads a beacon; does not model a token.</b> The record for an <em>issued</em> token — state,
/// validity window, verification channel — belongs with the claiming flow. This owns only reading:
/// given a <see cref="ProbeResult"/>, what token string, if any, did the server show us.
/// </para>
/// <para>
/// <b>Two of §8's three channels are visible to a probe, and <see cref="Find"/> reads only those.</b>
/// An MSSP variable and a connect-screen line arrive on the wire; a TXT record does not, and no
/// telnet session can see one. The DNS channel is read by <see cref="DnsClaimVerifier"/> instead,
/// which opens no socket — so a token that verified through DNS is a real claim and is nonetheless
/// <em>not</em> §7.3's identity beacon, because nothing on a probe carries it. The two facts are
/// separate and this type owns only the second.
/// </para>
/// <para>
/// <b>The spellings are a published contract with server operators</b> — they appear in the claim
/// instructions an owner is given, so changing one is a migration, not an edit.
/// <c>IdentityCorpusTests.TheWireSpellingsAreWhatWeTellOperatorsToType</c> pins them.
/// </para>
/// <para>
/// More than one MSSP spelling is accepted deliberately: MSSP variable names don't reliably survive a
/// config file intact, and an operator who did exactly what they were told must not be told their
/// claim failed.
/// </para>
/// </remarks>
public static class ClaimTokenBeacon
{
    /// <summary>The MSSP variable the site asks owners to set.</summary>
    public const string MsspVariable = "MUINDEX CLAIM";

    /// <summary>The labelled connect-screen form, e.g. <c>MUINDEX-CLAIM: muidx-a2b3-c4d5</c>.</summary>
    public const string ConnectScreenPrefix = "MUINDEX-CLAIM:";

    /// <summary>The DNS label §8's third channel lives under. Shared with §11's opt-out so a deployment owns one underscore label.</summary>
    public const string DnsLabel = "_muindex";

    /// <summary>
    /// Every MSSP variable a token is accepted from, canonical first — <see cref="Read"/> returns the
    /// first it finds, so a game that set both is read as having set the canonical one.
    /// </summary>
    public static readonly IReadOnlyList<string> AcceptedMsspVariables =
        [MsspVariable, "MUINDEX_CLAIM", "CONTACT_TOKEN"];

    /// <summary>The fully-qualified name a claim record for this host lives at.</summary>
    public static string DnsNameFor(string host) => $"{DnsLabel}.{CanonicalHost.Normalize(host)}";

    /// <summary>
    /// The claim token a TXT answer publishes <em>for this port</em>, or null (spec §8.3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The grammar is <see cref="OptOutVocabulary.ReadDns"/>'s: tokens separated by whitespace or
    /// semicolons, each able to qualify itself with <c>=</c> or <c>:</c> and a comma-separated port
    /// list, so <c>"v=muindex1; muidx-…=4201"</c> and a bare <c>"muidx-…=4201,4202"</c> both work.
    /// Several games on one domain publish several records, or several tokens in one.
    /// </para>
    /// <para>
    /// <b>It diverges from the opt-out grammar in the two places the safe direction reverses.</b> A
    /// qualifier is <em>required</em> here and unparseable ones are refused, where an opt-out reads
    /// both as covering the whole host. There the cost of guessing wrong is crawling somebody who
    /// asked us to stop, so an unreadable record must still stop us; here it is handing a listing to
    /// whoever controls the domain, so an unreadable record must verify nothing. The qualifier is
    /// also what answers §8.3's objection to the channel at all — a TXT record proves control of a
    /// hostname, and naming the port is how a publisher says which listener they are speaking for.
    /// </para>
    /// <para>
    /// A returned token has proved nothing yet: like every other channel, it is a candidate for
    /// <c>ClaimService.OfferBeaconAsync</c> to look up against an issued pending claim.
    /// </para>
    /// </remarks>
    public static string? ReadDns(IEnumerable<string> records, int port)
    {
        ArgumentNullException.ThrowIfNull(records);

        foreach (var record in records)
        {
            foreach (var part in record.Split([' ', '\t', ';'], StringSplitOptions.RemoveEmptyEntries))
            {
                var cut = part.IndexOfAny(['=', ':']);

                // No qualifier at all. Not an answer about this port, or about any other.
                if (cut < 0)
                {
                    continue;
                }

                // Lower-cased because the mint alphabet is, so this recovers the exact bytes we
                // issued from a control panel that normalised the value's case on the way in.
                var token = part[..cut].ToLowerInvariant();

                if (ClaimToken.LooksLikeOne(token) && NamesPort(part[(cut + 1)..], port))
                {
                    return token;
                }
            }
        }

        return null;

        static bool NamesPort(string qualifier, int port)
        {
            var named = qualifier.Split(',', StringSplitOptions.RemoveEmptyEntries);

            return named.Any(part => int.TryParse(part.Trim(), out var value) && value == port);
        }
    }

    /// <summary>The token this probe carries, from any channel a probe can see, or null.</summary>
    /// <remarks>
    /// The identity matcher wants the value and not the provenance — a token is decisive wherever it
    /// was published. <see cref="Find"/> is the arm that also says which channel, which is what the
    /// claim record stores so an owner can be told what we actually saw.
    /// </remarks>
    public static string? Read(ProbeResult result) => Find(result)?.Token;

    /// <summary>The token and the channel it was read from, or null.</summary>
    /// <remarks>
    /// MSSP is looked at before the connect screen because it is the channel we ask for first and the
    /// one that survives a screen redesign. A server publishing the token in both is reported as MSSP,
    /// which is true and is the more durable of the two.
    /// </remarks>
    public static ClaimBeacon? Find(ProbeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        foreach (var variable in AcceptedMsspVariables)
        {
            if (MsspReading.Value(result.Mssp, variable) is { } declared && !string.IsNullOrWhiteSpace(declared))
            {
                return new ClaimBeacon(declared.Trim(), ClaimChannel.Mssp);
            }
        }

        if (result.Banner is not { Length: > 0 } banner)
        {
            return null;
        }

        // Flattened first: a beacon inside an SGR run is still a beacon, and this is the same
        // normalisation the banner fingerprint is taken over.
        var plain = BannerFingerprint.Flatten(banner);
        var start = plain.IndexOf(ConnectScreenPrefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        var rest = plain[(start + ConnectScreenPrefix.Length)..].TrimStart();
        var labelled = new string(rest.TakeWhile(ch => !char.IsWhiteSpace(ch)).ToArray());
        return labelled.Length > 0 ? new ClaimBeacon(labelled, ClaimChannel.ConnectScreen) : null;
    }
}

/// <summary>A claim token read off a server, and where it was published (spec §8.3).</summary>
public sealed record ClaimBeacon(string Token, ClaimChannel Channel);

/// <summary>
/// Reading an MSSP report without ever treating a codebase default as an answer.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every identity signal goes through <see cref="Meaningful"/>, and that is not polish.</b> Every
/// unedited PennMUSH publishes <c>NAME "PennMUSH"</c>; scored naively they'd all match each other on
/// the strongest textual signal in §7.3's table, and auto-merge would fuse unrelated games into one
/// listing.
/// </para>
/// <para>
/// A placeholder contributes <b>nothing</b> rather than a little — two absences must never score as
/// an agreement. The same applies to <c>CONTACT</c>/<c>WEBSITE</c> (shared across a hosting provider)
/// and <c>CREATED</c> (a year that collides freely), all covered by
/// <see cref="MsspDefaults.IsPlaceholder"/>.
/// </para>
/// </remarks>
public static class MsspReading
{
    /// <summary>The raw value of an MSSP variable, matched case-insensitively as MSSP variables are.</summary>
    /// <remarks>
    /// A variable holds a <em>list</em>, because MSSP lets a server repeat one; identity wants a
    /// scalar. Takes the <b>last</b>, per the spec's own reduction rule ("the last reported value
    /// should be used as the default value"), matching <see cref="ProbeResult.MsspField"/>.
    /// </remarks>
    public static string? Value(IReadOnlyDictionary<string, IReadOnlyList<string>> mssp, string variable)
    {
        ArgumentNullException.ThrowIfNull(mssp);

        foreach (var (name, values) in mssp)
        {
            if (string.Equals(name.Trim(), variable, StringComparison.OrdinalIgnoreCase)
                && values.Count > 0)
            {
                return values[^1];
            }
        }

        return null;
    }

    /// <summary>
    /// The value if somebody answered, or null if it is blank, template text or the codebase's own
    /// default. Never returns an empty string — the two states a caller must tell apart are "answered"
    /// and "did not", and a placeholder is the second.
    /// </summary>
    public static string? Meaningful(IReadOnlyDictionary<string, IReadOnlyList<string>> mssp, string variable)
    {
        var raw = Value(mssp, variable);
        return MsspDefaults.IsPlaceholder(raw) ? null : raw!.Trim();
    }

    /// <summary>
    /// A game's declared name, or null when it is a placeholder <em>or</em> merely restates the
    /// codebase — <c>NAME "PennMUSH 1.8.8p0"</c> is the same non-answer as <c>NAME "PennMUSH"</c>.
    /// </summary>
    public static string? MeaningfulName(IReadOnlyDictionary<string, IReadOnlyList<string>> mssp) =>
        MsspDefaults.MeaningfulName(
            Value(mssp, IdentityMsspVariables.Name),
            Value(mssp, IdentityMsspVariables.Codebase));
}

/// <summary>Signals as stored on a merge or review row, so a decision can be explained later.</summary>
public static class IdentitySignals
{
    public static string ToJson(IReadOnlyList<IdentitySignal> signals) => JsonSerializer.Serialize(signals);

    /// <summary>
    /// The reverse of <see cref="ToJson"/> — reading a review row's evidence back, so a merge that
    /// resolves one can carry the same signals forward onto <c>merge_log</c> rather than inventing new
    /// ones. An unreadable or absent payload is an empty list, not a failure: nothing here is evidence
    /// of a defect a caller should crash over.
    /// </summary>
    public static IReadOnlyList<IdentitySignal> FromJson(string? signalsJson)
    {
        if (string.IsNullOrWhiteSpace(signalsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<IdentitySignal>>(signalsJson) ?? [];
        }
        catch (JsonException)
        {
            // The unreadable half of the summary above: corrupted evidence must not block a merge.
            return [];
        }
    }
}

/// <summary>
/// The reverse field lookup identity needs: "which games carry this value for this field".
/// </summary>
/// <remarks>
/// A forward store reads by game id, which can't answer "who else calls themselves Corvid" without
/// scanning every game — this is the missing arrow. Implementations must compare case-insensitively
/// on field name and value, trimmed, distinct game ids, or the matcher passes tests and misses
/// candidates in production.
/// </remarks>
public interface IGameFieldIndex
{
    Task<IReadOnlyList<Guid>> GamesWithFieldAsync(string field, string value, CancellationToken ct);
}

/// <summary>An address a game is known to answer at.</summary>
/// <remarks>
/// The catalogue's own endpoint view carries no game id, because a page already knows whose page it is.
/// Identity works the other way round — from an address to a game — so it needs the arrow the view
/// omits.
/// </remarks>
public sealed record KnownEndpoint(
    Guid GameId,
    string Host,
    int Port,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);

/// <summary>
/// Which game answers at an address. One of three narrow seams this project needs from catalogue
/// storage.
/// </summary>
/// <remarks>
/// Deliberately narrow: these interfaces state exactly what discovery reads and writes, so the storage
/// layer can implement them without discovery ever depending on a repository's full surface. An
/// in-memory implementation is what every test here runs against, with no network and no database.
/// </remarks>
public interface IEndpointDirectory
{
    Task<KnownEndpoint?> ByAddressAsync(string host, int port, CancellationToken ct);

    /// <summary>
    /// Every address on record for one game. <see cref="IdentityWeights.ResolvedEndpoint"/> is the
    /// reason this exists: asking "what does this candidate's own hostname resolve to" needs the
    /// hostname first, and <see cref="ByAddressAsync"/> only ever answers in the other direction.
    /// </summary>
    Task<IReadOnlyList<KnownEndpoint>> ForGameAsync(Guid gameId, CancellationToken ct);

    Task UpsertAsync(KnownEndpoint endpoint, CancellationToken ct);
}

/// <summary>Whether a game id still names a game.</summary>
/// <remarks>
/// Asked before a candidate is scored: an endpoint or field row outliving its game is a repair job, not
/// a match, and returning it would attach a probe to a game that is not there.
/// </remarks>
public interface IGameDirectory
{
    Task<bool> ExistsAsync(Guid gameId, CancellationToken ct);
}
