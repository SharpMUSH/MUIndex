using MUI.Catalog;
using MUI.Crawl;

namespace MUI.Discovery;

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
