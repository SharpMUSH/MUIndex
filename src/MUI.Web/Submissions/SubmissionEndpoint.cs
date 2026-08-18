using Microsoft.AspNetCore.Mvc;

using MUI.Catalog.Persistence;
using MUI.Discovery;
using MUI.Web.Components;
using MUI.Web.Localization;

namespace MUI.Web.Submissions;

/// <summary>
/// The one POST behind <c>/submit</c> (spec §7.6, §9).
/// </summary>
/// <remarks>
/// <para>
/// <b>It takes two form fields and decides nothing.</b> <see cref="SubmissionService"/> owns the
/// order of the checks — the bound, then the address, then our own catalogue, then §7.2's gate on
/// the resolved address — and this maps what came back onto a URL. Keeping the decision out of the
/// handler is what lets it be tested without a socket, a browser or a database.
/// </para>
/// <para>
/// <b>Unauthenticated, and bounded rather than gated.</b> There is nothing to sign in for: the form
/// takes an address and an address is public information. What stops it being a firehose is the
/// per-source bound inside the service, and what stops it being a way to reach anything private is
/// the resolved-address gate — neither of which an account would have improved. Requiring a passkey
/// to tell us a MUD exists would mostly stop people telling us MUDs exist.
/// </para>
/// <para>
/// Antiforgery is enforced by the framework because the handler binds form fields, and the page
/// renders <c>&lt;AntiforgeryToken /&gt;</c> inside the form. That is not a security boundary here so
/// much as hygiene — the worst a cross-site post could achieve is telling us about a game.
/// </para>
/// </remarks>
public static class SubmissionEndpoint
{
    public static void MapMuiSubmissions(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost(SubmitLinks.Path, async (
            HttpContext context,
            SubmissionService submissions,
            SubmissionSource sources,
            IGameStore games,
            [FromForm] string? host,
            [FromForm] string? port) =>
        {
            var receipt = await submissions.SubmitAsync(
                host,
                port,
                await sources.OfAsync(SubmitterAddress.Of(context), context.RequestAborted),
                context.RequestAborted);

            // The slug of whatever already answers there, so the page can offer the right link — its
            // listing if we publish it, its claim page if we are holding it back. The row decides,
            // not this handler: the page looks it up again and reads the same two columns.
            var slug = receipt.GameId is { } id && await games.ByIdAsync(id, context.RequestAborted) is { } game
                ? game.Slug
                : null;

            return Results.Redirect(LocaleRouting.Link(
                context.LocaleOf().Tag,
                SubmitLinks.For(receipt.Outcome, receipt.Address, slug)));
        });
    }
}
