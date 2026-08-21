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
