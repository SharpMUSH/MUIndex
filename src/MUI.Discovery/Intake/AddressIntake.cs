using MUI.Crawl;

namespace MUI.Discovery;

/// <summary>
/// What the intake checks said about one proposed address.
/// </summary>
/// <param name="Refusal">
/// Why the address goes no further, or null when nothing stood in its way and the caller may plant it.
/// </param>
/// <param name="GameId">The game that already answers there, for <see cref="SubmissionOutcome.AlreadyListed"/>.</param>
/// <param name="Detail">
/// The scope gate's own words, for an operator's log. Never shown to whoever proposed the address: it
/// names what the host resolved to.
/// </param>
public sealed record IntakeRuling(SubmissionOutcome? Refusal, Guid? GameId = null, string? Detail = null)
{
    public static readonly IntakeRuling Clear = new(Refusal: null);
}

/// <summary>
/// The checks every address proposed to us from outside passes before it becomes a crawl target —
/// the public form's and the lead routine's alike (spec §7.2, §8, §11).
/// </summary>
/// <remarks>
/// <para>
/// <b>One sequence, two doors.</b> <see cref="SubmissionService"/> and <see cref="LeadService"/> differ
/// in who is asking and how often they may — a stranger's browser against staff's own routine — and in
/// nothing that decides whether an address may be dialled. A copy of these four checks in each would
/// be two places for the order to drift, and the order is the design.
/// </para>
/// <para>
/// Already listed, then already queued, then §7.2's gate on the resolved address, then §11's opt-out,
/// which is asked last because an address we will not dial anyway isn't worth a register read or a
/// TXT lookup. Our own catalogue is consulted before DNS, so neither door can be used as a free
/// resolver for an address we already know.
/// </para>
/// <para>
/// <b>A refusal writes nothing.</b> Recording what became of the proposal is the caller's business,
/// in its own log; nothing here touches a game, a target, or an availability sample.
/// </para>
/// </remarks>
public sealed class AddressIntake(
    ICrawlTargetRepository targets,
    IEndpointDirectory endpoints,
    IHostScopeGuard scope,
    OptOutGate optOut)
{
    public async Task<IntakeRuling> RuleAsync(SubmittedAddress address, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (await endpoints.ByAddressAsync(address.Host, address.Port, ct) is { } known)
        {
            return new IntakeRuling(SubmissionOutcome.AlreadyListed, known.GameId);
        }

        if (await targets.ByAddressAsync(address.Host, address.Port, ct) is not null)
        {
            return new IntakeRuling(SubmissionOutcome.AlreadyQueued);
        }

        var decision = await scope.InspectAsync(address.Host, ct);

        if (decision.Ruling is not HostScopeRuling.Allowed)
        {
            // Two outcomes, because §7.2 keeps them as two facts on the record — but what a
            // submitter is told collapses them into one sentence (see SubmitCopy), since telling a
            // stranger which happened is an oracle over our resolver's view of names.
            var outcome = decision.Ruling is HostScopeRuling.RefusedNonGlobal
                ? SubmissionOutcome.RefusedNotRoutable
                : SubmissionOutcome.Unresolvable;

            return new IntakeRuling(outcome, Detail: decision.Detail);
        }

        // §11, checked here rather than left to the crawl loop's gate: without this, a caller would be
        // told "accepted" for an address we'd already promised never to touch. A refusal we know at
        // the door belongs at the door.
        if (await optOut.RuleOnAsync(address.Host, address.Port, ct) is not null)
        {
            return new IntakeRuling(SubmissionOutcome.RefusedOptOut);
        }

        return IntakeRuling.Clear;
    }
}
