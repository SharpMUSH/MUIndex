using Dapper;

using MUI.Catalog;
using MUI.Web.Mcp;
using MUI.Web.Tests.Support;

using Npgsql;

namespace MUI.Web.Tests.Mcp;

/// <summary>
/// Honouring "please take us out of the listing" from somebody with no account (issue #187).
/// </summary>
/// <remarks>
/// <para>
/// Measured on production, and the reason this exists: Convergence MUSH's admin asked in a chat
/// message on 2026-08-16. The opt-out was recorded and honoured at the dial — and a month later the
/// game was still in the listing, still indexable, still publishing <c>telnet://</c> as structured
/// data under a page titled "how to connect". Every part of that was reachable except the one that
/// mattered, because the state could only be set by a verified owner and there wasn't one.
/// </para>
/// <para>
/// §7.5 is untouched throughout: the page, the URL, the history and the change feed survive, as they
/// do for every other state. What stops is the promotion.
/// </para>
/// </remarks>
public class StaffUnlistTests
{
    private const string Token = "0123456789abcdef0123456789abcdef";

    private static Dictionary<string, string?> Settings() => new()
    {
        [MuiMcp.TokenConfigurationKey] = Token,
    };

    private static async Task SeedAsync(NpgsqlDataSource source, string slug)
    {
        await using var connection = await source.OpenConnectionAsync();

        await connection.ExecuteAsync(
            "INSERT INTO game (id, slug, name, state, is_claimed, first_seen_at)"
            + " VALUES (@id, @slug, @name, 'active', false, now())",
            new { id = Guid.CreateVersion7(), slug, name = "Convergence MUSH" });
    }

    [Test]
    public async Task GameUnlistTakesAGameOutOfTheListingForAReason()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        await SeedAsync(source, "convergence-mush");

        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString);
        await using var client = await McpTestClient.ConnectAsync(site, Token);

        var result = await client.CallAsync<GameUnlistResult>(
            "game_unlist",
            new Dictionary<string, object?>
            {
                ["slug"] = "convergence-mush",
                ["because"] = "their admin asked in a chat message on 2026-08-16",
            });

        await Assert.That(result.Slug).IsEqualTo("convergence-mush");
        await Assert.That(result.State).IsEqualTo("unlisted");

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<string>(
                "SELECT unlisted_reason FROM game WHERE slug = 'convergence-mush'"))
            .IsEqualTo("their admin asked in a chat message on 2026-08-16");
    }

    /// <summary>
    /// A reason is required, the way <c>--because</c> is on merge and distinct.
    /// </summary>
    /// <remarks>
    /// An unlisting is an editorial act on somebody else's behalf. Without the words beside it,
    /// nobody reviewing the catalogue later can tell it from a mistake — which is the thing the
    /// account requirement was protecting, and the thing that has to survive relaxing it.
    /// </remarks>
    [Test]
    public async Task GameUnlistRefusesAnEmptyBecause()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        await SeedAsync(source, "convergence-mush");

        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString);
        await using var client = await McpTestClient.ConnectAsync(site, Token);

        await Assert.That(async () => await client.CallAsync<GameUnlistResult>(
                "game_unlist",
                new Dictionary<string, object?>
                {
                    ["slug"] = "convergence-mush",
                    ["because"] = "   ",
                }))
            .Throws<InvalidOperationException>();
    }

    /// <summary>
    /// And it is reversible, so an unlisting is not a one-way door.
    /// </summary>
    /// <remarks>
    /// A game whose operators change their mind has no other route back: an opted-out address is
    /// refused before the dial, so the probe that would otherwise relist it never happens.
    /// </remarks>
    [Test]
    public async Task GameRelistPutsItBack()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        await SeedAsync(source, "convergence-mush");

        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString);
        await using var client = await McpTestClient.ConnectAsync(site, Token);

        await client.CallAsync<GameUnlistResult>(
            "game_unlist",
            new Dictionary<string, object?>
            {
                ["slug"] = "convergence-mush",
                ["because"] = "their admin asked in a chat message on 2026-08-16",
            });

        var back = await client.CallAsync<GameUnlistResult>(
            "game_relist", new Dictionary<string, object?> { ["slug"] = "convergence-mush" });

        await Assert.That(back.State).IsEqualTo("active");

        await using var connection = await source.OpenConnectionAsync();

        // The reason goes with the state: it explained a condition that no longer holds, and leaving
        // it behind would have the row saying the game is listed and why it is not.
        await Assert.That(await connection.ExecuteScalarAsync<string?>(
                "SELECT unlisted_reason FROM game WHERE slug = 'convergence-mush'"))
            .IsNull();
    }

    /// <summary>
    /// The page of an unlisted game stops being offered to search engines (issue #187).
    /// </summary>
    /// <remarks>
    /// End to end over a real database, because this is the sentence the whole issue is about: the
    /// page answers, and a crawler is told not to index it and handed no connect address. Rendering
    /// alone cannot show it — the meta and the graph live in the head, which the component harness
    /// does not produce.
    /// </remarks>
    [Test]
    public async Task AnUnlistedGamesPageIsNoLongerOfferedToSearchEngines()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        await SeedAsync(source, "convergence-mush");

        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString);

        var listed = await site.Client.GetStringAsync("/g/convergence-mush");

        await Assert.That(listed).DoesNotContain("noindex");

        await using var client = await McpTestClient.ConnectAsync(site, Token);

        await client.CallAsync<GameUnlistResult>(
            "game_unlist",
            new Dictionary<string, object?>
            {
                ["slug"] = "convergence-mush",
                ["because"] = "their admin asked in a chat message on 2026-08-16",
            });

        var unlisted = await site.Client.GetStringAsync("/g/convergence-mush");

        // The page still answers — §7.5, and the whole point of not deleting anything.
        await Assert.That(unlisted).Contains("Convergence MUSH");

        await Assert.That(Head.Meta(unlisted, "robots")).IsEqualTo("noindex, nofollow");
        await Assert.That(unlisted).DoesNotContain("gameServer");

        // And no empty graph left behind where the real one used to be: a machine reading an empty
        // ld+json block gets a parse error rather than an absence.
        await Assert.That(unlisted).DoesNotContain("application/ld+json\"></script>");
    }
}
