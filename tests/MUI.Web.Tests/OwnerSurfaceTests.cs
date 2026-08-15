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
/// A claim about what an owner can reach has to be read off the rendered form, because the form is
/// the only part of this a person sees. The gate itself is asserted against a real database in
/// <c>OwnerEnrichmentPostgresTests</c> — these say that the surface offers exactly what the gate
/// permits, which is the half that goes wrong quietly.
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
    public async Task ThePanelOffersEveryEnrichableFieldAndNoMeasurementAtAll()
    {
        var markup = await PanelAsync();

        foreach (var definition in FieldRegistry.OwnerEnrichable)
        {
            await Assert.That(markup).Contains($"name=\"{OwnerWrites.FieldPrefix}{definition.Name}\"");
        }

        foreach (var measured in new[] { "CODEBASE", "PLAYERS", "GENRE", CapabilityFields.Measured("GMCP") })
        {
            await Assert.That(markup).DoesNotContain($"{OwnerWrites.FieldPrefix}{measured}");
        }
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
    /// §8.5 requires the refusal to be out loud. A parser that filtered here would turn it back into
    /// the silent no-op the rule exists to prevent — and the filtering would be a second spelling of
    /// the writable set, in the one place nobody would think to look for it.
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

    private static Task<string> PanelAsync(IReadOnlyDictionary<string, GameField>? declared = null) =>
        Render.ComponentAsync<OwnerPanel>(
            new Dictionary<string, object?>
            {
                ["GameId"] = Game,
                ["Name"] = "Ashen Court",
                ["Declared"] = declared ?? new Dictionary<string, GameField>(StringComparer.Ordinal),
                ["Now"] = Now,
            },
            services => services.AddSingleton<AntiforgeryStateProvider, NoAntiforgery>());

    /// <summary>
    /// A stand-in for the token provider the host supplies, so a form can be rendered headlessly.
    /// </summary>
    /// <remarks>
    /// It answers null, which is what the real one answers outside a request — the token is a
    /// property of the response, and these tests are about what the form asks for rather than about
    /// what protects it.
    /// </remarks>
    private sealed class NoAntiforgery : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken? GetAntiforgeryToken() => null;
    }
}
