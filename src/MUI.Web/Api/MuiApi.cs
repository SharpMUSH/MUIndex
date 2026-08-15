using MUI.Catalog.Persistence;

using Microsoft.Extensions.Options;

namespace MUI.Web.Api;

/// <summary>
/// The read API: registration and routing, in two calls a host makes.
/// </summary>
/// <remarks>
/// <para>
/// Minimal APIs alongside the Razor Components in one deployable (spec §4). Both surfaces read
/// through <see cref="MUI.Catalog.IGameQueries"/> and neither knows how the other renders, which is
/// what stops the API and the page disagreeing about a fact.
/// </para>
/// <para>
/// Every route is a GET. This surface writes nothing, offers no callback, and has no vote, star or
/// rating anywhere in it — the last is what killed Top Mud Sites, and a guard test says the words
/// are absent rather than trusting anyone to remember.
/// </para>
/// </remarks>
public static class MuiApi
{
    public static IServiceCollection AddMuiApi(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatasetLicenceOptions>(
            configuration.GetSection(DatasetLicenceOptions.Section));

        // The section IS the map — SlugAliases:{former} = {current} — rather than a nested
        // "Aliases" key, because an operator writing one down should not have to learn our
        // options class to say that /g/old-name became /g/new-name.
        services.Configure<SlugAliasOptions>(
            options => configuration.GetSection(SlugAliasOptions.Section).Bind(options.Aliases));

        services.AddSingleton<IAttributionSource, ConfiguredAttributionSource>();

        // The table where there is a database and the configuration where there is not, decided when
        // the graph is built rather than by registration order — this call happens before a host has
        // said whether it has a catalogue behind it, and on the fixture there is nothing to ask.
        services.AddSingleton<ISlugHistory>(s =>
        {
            var configured = new ConfiguredSlugHistory(
                s.GetRequiredService<IOptions<SlugAliasOptions>>());

            return s.GetService<ISlugHistoryStore>() is { } store
                ? new StoredSlugHistory(store, configured)
                : configured;
        });

        return services;
    }

    public static IEndpointRouteBuilder MapMuiApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(ApiRoutes.Base, IndexAsync);

        GameEndpoints.Map(endpoints);
        FeedEndpoints.Map(endpoints);
        DumpEndpoints.Map(endpoints);

        return endpoints;
    }

    /// <summary>
    /// The route table, so a consumer can start at <c>/api</c> and find the rest without reading us.
    /// </summary>
    private static Task IndexAsync(
        HttpContext http, IOptions<DatasetLicenceOptions> licence, TimeProvider clock) =>
        ApiResponse.WriteJsonAsync(http, new ApiIndexView(
            ApiVersion.Current,
            "MUIndex",
            "A read-only index of MU* games whose data is measured rather than asserted. Every "
            + "value carries the source it came from and when it was last confirmed.",
            ApiClock.Now(clock),
            licence.Value.View(),
            [
                new RouteView("GET", ApiRoutes.Games,
                    "The listing. Same querystring as the site's facet panel: q, archived, "
                    + "protocol (repeatable), band, limit, offset. Archived games are excluded "
                    + "unless archived=true."),
                new RouteView("GET", ApiRoutes.Games + "/{id-or-slug}",
                    "One game, by its immutable id or its current slug. A slug the game used to "
                    + "have redirects here permanently."),
                new RouteView("GET", ApiRoutes.Feeds,
                    "The three liveness registers: newly discovered, went dark, came back."),
                new RouteView("GET", ApiRoutes.DiscoveredRss, "Newly discovered, as RSS 2.0."),
                new RouteView("GET", ApiRoutes.WentDarkRss, "Went dark, as RSS 2.0."),
                new RouteView("GET", ApiRoutes.CameBackRss, "Came back, as RSS 2.0."),
                new RouteView("GET", ApiRoutes.Dump,
                    "The whole catalogue, archived games included, streamed as one JSON document."),
                new RouteView("GET", ApiRoutes.DumpLines,
                    "The whole catalogue, one game per line (NDJSON)."),
            ],
            [
                "id is an immutable GUID and never changes, including across a merge. slug is "
                + "minted from the game's name and does change; every slug a game has ever had "
                + "keeps redirecting.",
                "Nothing is ever deleted. An archived game keeps its URL, its history and its "
                + "place in the dump; state says what it is.",
                "Every value carries provenance and age. measured means somebody observed it; "
                + "declared means a game asserted it. Where they disagree, both are published.",
                "Every bare value has its label beside it or in fields: playersNow is labelled by "
                + "playersNowProvenance, codebase by codebaseProvenance, on the listing as well as "
                + "on a game. A null label means we hold no such value, never that we lost track "
                + "of where one came from.",
                "A player count of null means we did not measure one. It is never a zero. "
                + "playersNowState is measured, declared or unknown and always agrees with "
                + "playersNowProvenance. The line is who read the number: a pre-login WHO and a "
                + "connect screen are both text we parsed off the socket this probe and are "
                + "measured; PLAYERS in MSSP is the game reporting a field it maintains, and is "
                + "declared. source names which of the three it was.",
                // The word this sentence is careful not to use is the whole point of it, and the
                // rule binds published copy as much as it binds field names.
                "reachable describes a socket we opened from one vantage point at intervals. It "
                + "is not a claim about whether the game was running: a game with a routing "
                + "problem to our host is unreachable and perfectly alive.",
                "Per-codebase and per-protocol shares are publishable; an absolute population "
                + "figure is not, and this API publishes none.",
                "Every response carries a strong ETag over its exact bytes. Send If-None-Match "
                + "and expect a 304.",
                "Webhooks are deliberately not built. RSS is the whole of the notification "
                + "surface.",
            ]));
}
