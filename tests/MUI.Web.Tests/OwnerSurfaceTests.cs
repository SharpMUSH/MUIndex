using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Web.Accounts;
using MUI.Web.Components;

namespace MUI.Web.Tests;

/// <summary>
/// The owner dashboard's write path, as markup and as the parse behind it (spec §8.5, §11).
/// </summary>
/// <remarks>
/// A claim about what an owner can reach has to be read off the rendered form. The gate itself is
/// asserted against a real database in <c>OwnerEnrichmentPostgresTests</c>; these say the surface
/// offers exactly what the gate permits.
/// </remarks>
public class OwnerSurfaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Game = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000007");

    /// <summary>
    /// The form offers the enrichable fields and nothing measured — no count, no capability, no
    /// codebase.
    /// </summary>
    [Test]
    public async Task ThePanelOffersEveryWritableFieldAndNoMeasurementAtAll()
    {
        var markup = await PanelAsync();

        foreach (var definition in FieldRegistry.OwnerEnrichable.Concat(FieldRegistry.OwnerOverridable))
        {
            await Assert.That(markup).Contains($"name=\"{OwnerWrites.FieldPrefix}{definition.Name}\"");
        }

        // GENRE is on the form: §8.5 says an owner may never edit a MEASUREMENT, and a hand-typed
        // MSSP GENRE is not one (§5.1). What stays off the form is what a probe observed.
        foreach (var refused in new[]
                 {
                     "PLAYERS", "UPTIME", "CODEBASE", "HOSTNAME", "PORT", "IP", "FAMILY",
                     CapabilityFields.Measured("GMCP"), CapabilityFields.Declared("GMCP"),
                 })
        {
            await Assert.That(markup).DoesNotContain($"{OwnerWrites.FieldPrefix}{refused}");
        }
    }

    /// <summary>
    /// An override box shows what the game reports beside it, and never inside it.
    /// </summary>
    /// <remarks>
    /// Showing the report is what makes the form an override rather than a blank second opinion.
    /// Putting it in the box would invite an owner to retype their own MSSP into a row that says the
    /// same thing — a second value ageing independently of the first.
    /// </remarks>
    [Test]
    public async Task AnOverrideBoxShowsTheReportBesideItRatherThanInIt()
    {
        var markup = await PanelAsync(reported: [("GENRE", "Adventure")]);

        await Assert.That(Render.Words(markup)).Contains("your game reports");
        await Assert.That(markup).Contains("Adventure");
        await Assert.That(markup).DoesNotContain("value=\"Adventure\"");
    }

    /// <summary>
    /// The name box says that saving it moves the game's address, and that the old one keeps working.
    /// </summary>
    /// <remarks>
    /// §5.7's promise is to whoever holds the old URL — a change in address should be told about
    /// before the button is pressed, not discovered after.
    /// </remarks>
    [Test]
    public async Task ThePanelSaysThatRenamingMovesTheGamesAddress()
    {
        var words = Render.Words(await PanelAsync());

        await Assert.That(words).Contains("the address of its page");
        await Assert.That(words).Contains("redirects to its current one");
    }

    /// <summary>
    /// The panel says which of the two kinds of fact an owner is producing.
    /// </summary>
    /// <remarks>
    /// A box on a site whose whole claim is that its data is measured has to say that what goes into
    /// it is declared — and that it lands beside the measurements rather than over them, which is the
    /// property <c>(game, field, source)</c> exists to provide.
    /// </remarks>
    [Test]
    public async Task ThePanelSaysAnOwnersAnswerIsDeclaredAndSitsBesideWhatWeMeasured()
    {
        var words = Render.Words(await PanelAsync());

        await Assert.That(words).Contains("owner-declared");
        await Assert.That(words).Contains("beside what we measured — never instead of it");
        await Assert.That(words).Contains("Nothing measured can be edited from here");
    }

    /// <summary>§11 — no questions asked. There is nowhere on the form to put a reason.</summary>
    [Test]
    public async Task SuppressingAConnectScreenAsksForNoJustification()
    {
        var markup = await PanelAsync();
        var words = Render.Words(markup);

        await Assert.That(markup).Contains($"action=\"/account/games/{Game}/connect-screen\"");
        await Assert.That(markup).Contains("name=\"suppress\" value=\"true\"");
        await Assert.That(words).Contains("We will not ask why.");

        // No reason box, no confirmation step, no "tell us more".
        await Assert.That(markup).DoesNotContain("name=\"reason\"");
        await Assert.That(markup).DoesNotContain("textarea");
    }

    /// <summary>Once suppressed, the button offers the other direction rather than repeating itself.</summary>
    [Test]
    public async Task AnAlreadySuppressedScreenOffersToComeBack()
    {
        var markup = await PanelAsync(new Dictionary<string, GameField>(StringComparer.Ordinal)
        {
            [InternalFields.ConnectScreenSuppressed] =
                new(Game, InternalFields.ConnectScreenSuppressed, FieldSource.Owner, "true", Now, Now),
        });

        await Assert.That(markup).Contains("name=\"suppress\" value=\"false\"");
        await Assert.That(Render.Words(markup)).Contains("Show it again");
        await Assert.That(Render.Words(markup)).Contains("We are not republishing it.");
    }

    /// <summary>What an owner already declared comes back in the box, with its age beside it.</summary>
    [Test]
    public async Task WhatAnOwnerAlreadyDeclaredIsInTheBoxAndCarriesItsAge()
    {
        var markup = await PanelAsync(new Dictionary<string, GameField>(StringComparer.Ordinal)
        {
            ["FANDOM"] = new(Game, "FANDOM", FieldSource.Owner, "Exalted", Now.AddDays(-40), Now.AddDays(-40)),
        });

        await Assert.That(markup).Contains("value=\"Exalted\"");
        await Assert.That(Render.Words(markup)).Contains("declared 5w ago");

        // Clearing is offered as what it is: a withdrawal that keeps the record, not a delete.
        await Assert.That(Render.Words(markup))
            .Contains("the record of what it said is kept either way");
    }

    /// <summary>
    /// A withdrawn field is an empty box with nothing claimed beside it.
    /// </summary>
    /// <remarks>
    /// Clearing keeps the row (nothing is ever deleted), and the panel read the row's presence as a
    /// declaration — so a field an owner had emptied printed "declared 5w ago" next to an empty box.
    /// An age on a value nobody can see is an age on nothing.
    /// </remarks>
    [Test]
    public async Task AWithdrawnFieldDoesNotClaimToHaveBeenDeclared()
    {
        var markup = await PanelAsync(new Dictionary<string, GameField>(StringComparer.Ordinal)
        {
            ["FANDOM"] = new(Game, "FANDOM", FieldSource.Owner, string.Empty, Now.AddDays(-40), Now.AddDays(-40)),
        });

        await Assert.That(markup).Contains($"name=\"{OwnerWrites.FieldPrefix}FANDOM\"");
        await Assert.That(Render.Words(markup)).DoesNotContain("declared 5w ago");
    }

    /// <summary>
    /// A form key names the field, so the gate can be on the name.
    /// </summary>
    /// <remarks>
    /// Everything else a form posts — the anti-forgery token, a button — is not an edit and is not
    /// guessed at. The last value of a repeated key wins, because that is what a browser means by a
    /// hidden default followed by a control.
    /// </remarks>
    [Test]
    public async Task OnlyPrefixedKeysAreEditsAndTheLastValueOfOneWins()
    {
        var edits = OwnerWrites.EditsIn(new Dictionary<string, StringValues>(StringComparer.Ordinal)
        {
            ["__RequestVerificationToken"] = "irrelevant",
            ["suppress"] = "true",
            [OwnerWrites.FieldPrefix + "FANDOM"] = new(["", "Exalted"]),
        });

        await Assert.That(edits.Count).IsEqualTo(1);
        await Assert.That(edits[0].Field).IsEqualTo("FANDOM");
        await Assert.That(edits[0].Value).IsEqualTo("Exalted");
    }

    /// <summary>
    /// A key naming a measurement is carried through to be refused, never dropped on the way.
    /// </summary>
    /// <remarks>
    /// §8.5 requires the refusal to be out loud — filtering here would turn it back into the silent
    /// no-op the rule exists to prevent, and in the one place nobody would think to look for it.
    /// </remarks>
    [Test]
    public async Task AKeyNamingAMeasurementReachesTheGateRatherThanBeingDroppedQuietly()
    {
        var edits = OwnerWrites.EditsIn(new Dictionary<string, StringValues>(StringComparer.Ordinal)
        {
            [OwnerWrites.FieldPrefix + "CODEBASE"] = "PennMUSH 9.9.9",
        });

        await Assert.That(edits.Single().Field).IsEqualTo("CODEBASE");
    }

    /// <summary>
    /// The unlisting control is not offered to a game we are still dialling.
    /// </summary>
    /// <remarks>
    /// A button rendered here that the endpoint would refuse reads as an offer. Null and "may not"
    /// render identically — the panel does not guess when nothing records whether we're crawling.
    /// </remarks>
    [Test]
    [Arguments(null)]
    [Arguments(false)]
    public async Task TheUnlistingControlIsOfferedOnlyOnceWeHaveStopped(bool? mayUnlist)
    {
        var html = await PanelAsync(
            listing: mayUnlist is { } may ? new OwnerListingState(IsUnlisted: false, MayUnlist: may) : null);

        await Assert.That(html).DoesNotContain("/listing");
    }

    [Test]
    public async Task AGameWeHaveStoppedEverywhereIsOfferedTheWayOutOfTheListing()
    {
        var html = await PanelAsync(listing: new OwnerListingState(IsUnlisted: false, MayUnlist: true));

        await Assert.That(html).Contains($"/account/games/{Game}/listing");
        await Assert.That(html).Contains("Take us out of the listing too");
    }

    /// <summary>
    /// An unlisted game is offered the way back, and told the probe will do it too.
    /// </summary>
    /// <remarks>
    /// The second sentence is the one that matters. An owner who withdraws their opt-out in their own
    /// zone file and never returns here should not be left believing the listing needs a second ask.
    /// </remarks>
    [Test]
    public async Task AnUnlistedGameIsOfferedTheWayBackAndToldAProbeDoesItToo()
    {
        var html = await PanelAsync(listing: new OwnerListingState(IsUnlisted: true, MayUnlist: true));

        await Assert.That(html).Contains("Put us back in the listing");
        await Assert.That(html).Contains("One probe that answers does this too");
    }

    private static Task<string> PanelAsync(
        IReadOnlyDictionary<string, GameField>? declared = null,
        IReadOnlyList<(string Field, string Value)>? reported = null,
        OwnerListingState? listing = null) =>
        Render.ComponentAsync<OwnerPanel>(
            new Dictionary<string, object?>
            {
                ["GameId"] = Game,
                ["Name"] = "Ashen Court",
                ["Declared"] = declared ?? new Dictionary<string, GameField>(StringComparer.Ordinal),
                ["Reported"] = (reported ?? []).ToDictionary(
                    r => r.Field,
                    r => new GameField(Game, r.Field, FieldSource.Mssp, r.Value, Now.AddDays(-30), Now),
                    StringComparer.OrdinalIgnoreCase),
                ["Listing"] = listing,
                ["Now"] = Now,
            },
            services => services.AddSingleton<AntiforgeryStateProvider, NoAntiforgery>());

    /// <summary>
    /// A stand-in for the token provider the host supplies, so a form can be rendered headlessly.
    /// </summary>
    /// <remarks>
    /// Answers null, matching the real provider outside a request — these tests are about what the
    /// form asks for, not what protects it.
    /// </remarks>
    private sealed class NoAntiforgery : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken? GetAntiforgeryToken() => null;
    }
}
