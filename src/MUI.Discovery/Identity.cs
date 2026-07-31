using System.Text.Json;
using MUI.Catalog;
using MUI.Crawl;

using MUI.Catalog.Persistence;

namespace MUI.Discovery;

/// <summary>
/// The weighted signals of spec §7.3, and the two thresholds they are compared against.
/// </summary>
/// <remarks>
/// Spec §15.5 records that the auto-merge threshold needs calibration against real data, so these are
/// the conservative shipping defaults and <see cref="DiscoveryOptions"/> is what a deployment tunes.
/// The corpus in <c>IdentityCorpusTests</c> is the thing to re-run after any change; if a real merge is
/// ever reverted twice for the same shape, that shape belongs in the corpus before the number moves.
/// </remarks>
public static class IdentityWeights
{
    /// <summary>A previously-seen (host, port). Strongest: direct continuity, and on its own enough to merge.</summary>
    public const double Endpoint = 1.00;

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
    /// agreeing — which is §7.3's "a claimed game is never duplicated". <b>Read this before treating
    /// the number as the whole guarantee:</b> every §8 channel <em>publishes</em> the token, because
    /// proving control without an email round-trip is the entire point, so on a bare value comparison
    /// this weight is a merge primitive any passer-by can trigger — read a claimed game's MSSP,
    /// republish the token from your own host, be absorbed into their listing. Secrecy cannot fix a
    /// credential whose job is to be public. Narrowing it (the token counts on the game's own known
    /// endpoints, and from a strange host only once the game's own addresses have stopped answering)
    /// belongs with the half of §8 that issues and verifies tokens, and is not in this plan's scope.
    /// Until that lands, no verified token ever reaches <c>claim_token</c> and the signal never fires —
    /// which is correct rather than degraded.
    /// </remarks>
    public const double ClaimToken = 10.0;

    // Spec §15.5 — open question. These thresholds are reasoned but unvalidated: they need calibration
    // against real data, so they ship conservative and DiscoveryOptions is what a deployment tunes.

    /// <summary>At or above this, merge. Equal to <see cref="Endpoint"/>: a known endpoint <em>is</em> the game.</summary>
    public const double AutoMergeThreshold = 1.00;

    /// <summary>At or above this, open a review pair. Below it, a new game.</summary>
    /// <remarks>
    /// <b>Set equal to <see cref="WebsiteOrContact"/> on purpose.</b> The review band opens at exactly
    /// the weakest signal §7.3 calls "stable, and rarely coincidental", so one shared <c>WEBSITE</c> or
    /// <c>CONTACT</c> earns a human's eye and nothing weaker does — a codebase in common (0.15) does
    /// not, or the review queue would be the whole catalogue. It costs a review pair for every pair of
    /// games behind one hosting provider's contact address, which is the cheapest error this system
    /// makes: both pages stay live, they link reciprocally, and nothing is hidden.
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
/// <b>This type reads a beacon; it does not model a token.</b> The two halves of §8 belong apart: the
/// record for an <em>issued</em> token, with its state, validity window and verification channel, and
/// the issuing and verifying of it, belong with the claiming flow. This owns only the reading: given a
/// <see cref="ProbeResult"/>, what token string, if any, did the server show us.
/// </para>
/// <para>
/// Two of §8's three channels are visible to a probe — an MSSP variable and a line on the connect
/// screen. The third, a DNS TXT record, is not: nothing in a telnet session can see it. Until
/// something writes a verified token into the <c>claim_token</c> field, this signal never fires and the
/// matcher scores as though the weight were absent, which is correct rather than degraded.
/// </para>
/// <para>
/// <b>The spellings are a published contract with server operators.</b> They appear in the claim
/// instructions an owner is given and in this reader, so a silent edit on either side breaks claiming
/// with no failing test anywhere. <c>IdentityCorpusTests.TheWireSpellingsAreWhatWeTellOperatorsToType</c>
/// pins them. When the claiming half lands, these constants move to a type both sides can see and this
/// class aliases them; changing a value is a migration, not an edit, because it is changing what every
/// already-claimed game has typed into its config.
/// </para>
/// <para>
/// <b>More than one MSSP spelling is accepted, and that is not sloppiness.</b> MSSP variable names
/// cannot be relied on to survive a config file intact, and an operator who did exactly what they were
/// told must not be informed their claim failed — that support mail is the thing §8's whole design
/// exists to prevent.
/// </para>
/// </remarks>
public static class ClaimTokenBeacon
{
    /// <summary>The MSSP variable the site asks owners to set.</summary>
    public const string MsspVariable = "MUINDEX CLAIM";

    /// <summary>The labelled connect-screen form, e.g. <c>MUINDEX-CLAIM: muidx-a2b3-c4d5</c>.</summary>
    public const string ConnectScreenPrefix = "MUINDEX-CLAIM:";

    /// <summary>The DNS label of §8's third channel, which no telnet probe can see. Named here so both halves agree.</summary>
    public const string DnsLabel = "_muindex";

    /// <summary>
    /// Every MSSP variable a token is accepted from, canonical first — <see cref="Read"/> returns the
    /// first it finds, so a game that set both is read as having set the canonical one.
    /// </summary>
    public static readonly IReadOnlyList<string> AcceptedMsspVariables =
        [MsspVariable, "MUINDEX_CLAIM", "CONTACT_TOKEN"];

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
/// <b>Every identity signal goes through <see cref="Meaningful"/>, and that is not polish.</b> Observed
/// on a live server: an unedited PennMUSH publishes <c>NAME "PennMUSH"</c>, and so does every other
/// unedited PennMUSH on the internet. Scored naively they all match each other on the strongest textual
/// signal in §7.3's table, and auto-merge fuses unrelated games into one listing — silently, and
/// afterwards indistinguishably from a merge that should have happened.
/// </para>
/// <para>
/// So a placeholder contributes <b>nothing</b> rather than a little. <b>Two absences must never score
/// as an agreement.</b> The same caution applies to <c>CONTACT</c> and <c>WEBSITE</c>, shared across a
/// hosting provider more often than unique, and to <c>CREATED</c>, a year that collides freely — all of
/// which <see cref="MsspDefaults.IsPlaceholder"/> already covers for the blank and template-text cases.
/// </para>
/// </remarks>
public static class MsspReading
{
    /// <summary>The raw value of an MSSP variable, matched case-insensitively as MSSP variables are.</summary>
    /// <remarks>
    /// A variable holds a <em>list</em>, because MSSP lets a server repeat one — and identity wants a
    /// scalar. Where there are several this takes the <b>last</b>, which is the specification's own
    /// rule for reducing one ("the last reported value should be used as the default value") and what
    /// <see cref="ProbeResult.MsspField"/> does. Anything that cares about the other values must read
    /// the list, which is what <see cref="MsspReferrals"/> now does with <c>REFERRAL</c>.
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
}

/// <summary>
/// The reverse field lookup identity needs: "which games carry this value for this field".
/// </summary>
/// <remarks>
/// A forward store reads by game id, which cannot answer "who else calls themselves Corvid" without
/// scanning every game. This is the missing arrow. Its implementation must compare the same way this
/// contract states — case-insensitive on both field name and value, trimmed on both sides, distinct
/// game ids — or the matcher passes its tests and misses candidates in production.
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
