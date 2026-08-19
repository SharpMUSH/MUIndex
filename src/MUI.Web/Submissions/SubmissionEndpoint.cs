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
/// <b>It takes two form fields and decides nothing.</b> <see cref="SubmissionService"/> owns the
/// order of the checks — the bound, then the address, then our own catalogue, then §7.2's gate on the
/// resolved address — and this only maps what came back onto a URL.
/// <b>Unauthenticated, bounded rather than gated.</b> An address is public information; the
/// per-source bound and the resolved-address gate are what stop abuse, not an account requirement.
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

            // The slug of whatever already answers there, so the page can offer the right link.
            var slug = receipt.GameId is { } id && await games.ByIdAsync(id, context.RequestAborted) is { } game
                ? game.Slug
                : null;

            return Results.Redirect(LocaleRouting.Link(
                context.LocaleOf().Tag,
                SubmitLinks.For(receipt.Outcome, receipt.Address, slug)));
        });
    }
}
