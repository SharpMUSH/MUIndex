namespace MUI.Catalog.Tests;

/// <summary>
/// The rules the facet panel is built on, asserted with no database and no markup in the way.
/// </summary>
/// <remarks>
/// Two of these are the design and not a detail. <b>A count is what a click returns</b> — the whole
/// justification for computing the counts in the same pass as the listing — so each one is checked
/// by running the filter it advertises and comparing sizes, which is the only assertion that would
/// have caught a denominator quietly borrowed from somewhere broader. And <b>an unknown is not a
/// no</b>: a game we have no genre for must be findable as such and must never be returned by a
/// choice of some other genre, because folding the two is the single failure this site exists to
/// stop making about everything else.
/// </remarks>
public class FacetedSearchTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static GameFacetRow Row(
        string slug,
        ActivityBand band = ActivityBand.PlayersNow,
        string? genre = null,
        string? codebase = null,
        string? charset = null,
        bool tls = false,
        DateTimeOffset? lastReachableAt = null,
        string[]? protocols = null)
    {
        var summary = new GameSummary(
            Guid.NewGuid(), slug, slug, Tagline: null, LifecycleState.Active, IsClaimed: false,
            PlayersNow: 1, codebase, protocols ?? [], lastReachableAt ?? Now);

        return new GameFacetRow(
            summary,
            band,
            FacetedSearch.LastSeenOf(lastReachableAt ?? Now, Now),
            tls,
            charset,
            Language: null,
            codebase,
            Family: null,
            genre);
    }

    private static FacetGroup Group(GameListing listing, string key) =>
        listing.Facets.Single(f => f.Key == key);

    private static FacetValue Value(GameListing listing, string key, string token) =>
        Group(listing, key).Values.Single(v => v.Token == token);

    [Test]
    public async Task EveryCountIsExactlyWhatChoosingThatValueReturns()
    {
        // The claim the panel makes, checked by making the panel's own promise and then keeping it.
        // Nothing else here would notice a count measured against a wider set than the click lands on.
        GameFacetRow[] rows =
        [
            Row("a", genre: "Fantasy", codebase: "Evennia", protocols: ["GMCP"]),
            Row("b", genre: "Fantasy", codebase: "PennMUSH 1.8.8p0"),
            Row("c", genre: "Historical", codebase: "Evennia", protocols: ["GMCP"], tls: true),
            Row("d", codebase: "Evennia"),
        ];

        var listing = FacetedSearch.Search(rows, new GameFilter());

        foreach (var group in listing.Facets)
        {
            foreach (var value in group.Values)
            {
                var chosen = FacetedSearch.Search(rows, Choose(group.Key, value.Token));

                await Assert.That(chosen.Games.Count)
                    .IsEqualTo(value.Count)
                    .Because($"choosing {group.Key}={value.Token} must return the number it advertises");
            }
        }
    }

    [Test]
    public async Task ACountStaysTrueWhenAnotherFacetIsAlreadyChosen()
    {
        // The harder half: a facet's values are counted against the rest of the filter, so the
        // number beside "Evennia" while genre=Fantasy is chosen is the intersection and not the
        // total. Counting against everything would over-promise on exactly the second click.
        GameFacetRow[] rows =
        [
            Row("a", genre: "Fantasy", codebase: "Evennia"),
            Row("b", genre: "Fantasy", codebase: "PennMUSH 1.8.8p0"),
            Row("c", genre: "Historical", codebase: "Evennia"),
            Row("d", genre: "Historical", codebase: "Evennia"),
        ];

        var filter = new GameFilter { Genre = FacetChoice.Of("Fantasy") };
        var listing = FacetedSearch.Search(rows, filter);

        await Assert.That(Value(listing, FacetKeys.Codebase, "Evennia").Count).IsEqualTo(1);
        await Assert.That(
            FacetedSearch.Search(rows, filter with { Codebase = FacetChoice.Of("Evennia") }).Games.Count)
            .IsEqualTo(1);
    }

    [Test]
    public async Task AFacetsOwnSelectionIsLiftedSoTheOtherValuesAreStillReachable()
    {
        // A choice facet replaces rather than intersects, so its siblings have to be counted with it
        // out of the way — counted against the results they would all read zero and the panel would
        // be a one-way door.
        GameFacetRow[] rows =
        [
            Row("a", genre: "Fantasy"),
            Row("b", genre: "Historical"),
            Row("c", genre: "Historical"),
        ];

        var listing = FacetedSearch.Search(rows, new GameFilter { Genre = FacetChoice.Of("Fantasy") });

        await Assert.That(Value(listing, FacetKeys.Genre, "Fantasy").IsSelected).IsTrue();
        await Assert.That(Value(listing, FacetKeys.Genre, "Historical").Count).IsEqualTo(2);
        await Assert.That(Group(listing, FacetKeys.Genre).Total).IsEqualTo(3);
    }

    [Test]
    public async Task AGameWithNoValueIsUnknownAndIsNeverReturnedByAnotherValue()
    {
        // "We have no genre for this game" and "this game's genre is not Fantasy" are different
        // facts. The first is askable, and asking the second must not hand back the first.
        GameFacetRow[] rows =
        [
            Row("known", genre: "Fantasy"),
            Row("silent"),
        ];

        var listing = FacetedSearch.Search(rows, new GameFilter());
        var unknown = Value(listing, FacetKeys.Genre, FacetChoice.UnknownToken);

        await Assert.That(unknown.IsUnknown).IsTrue();
        await Assert.That(unknown.Count).IsEqualTo(1);

        var asked = FacetedSearch.Search(rows, new GameFilter { Genre = FacetChoice.Unknown });
        var elsewhere = FacetedSearch.Search(rows, new GameFilter { Genre = FacetChoice.Of("Fantasy") });

        await Assert.That(asked.Games.Select(g => g.Slug).ToList()).IsEquivalentTo(new[] { "silent" });
        await Assert.That(elsewhere.Games.Select(g => g.Slug).ToList()).IsEquivalentTo(new[] { "known" });
    }

    [Test]
    public async Task AProtocolNobodyWasSeenOfferingIsNotOfferedAsAChoice()
    {
        // A facet that can be clicked into an empty listing is a facet lying about the catalogue.
        // Not drawing the click is the cheapest way to make that impossible.
        var listing = FacetedSearch.Search([Row("a", protocols: ["GMCP"])], new GameFilter());
        var protocols = Group(listing, FacetKeys.Protocol).Values.Select(v => v.Token).ToList();

        await Assert.That(protocols).IsEquivalentTo(new[] { "GMCP" });
    }

    [Test]
    public async Task ProtocolsIntersectAndTheTickBoxNeverMeansTheGameLacksIt()
    {
        // Ticking two asks for games seen offering both. There is deliberately no way to ask for the
        // complement: a capability is written only when observed, so "not listed" covers a game we
        // never measured as well as one that declined, and only one of those is a fact about them.
        GameFacetRow[] rows =
        [
            Row("both", protocols: ["GMCP", "MSSP"]),
            Row("one", protocols: ["MSSP"]),
        ];

        var listing = FacetedSearch.Search(rows, new GameFilter { MeasuredProtocols = ["GMCP", "MSSP"] });

        await Assert.That(listing.Games.Select(g => g.Slug).ToList()).IsEquivalentTo(new[] { "both" });

        // With GMCP ticked, MSSP's count is the listing itself — what unticking MSSP would leave.
        var mssp = Value(FacetedSearch.Search(rows, new GameFilter { MeasuredProtocols = ["GMCP"] }),
            FacetKeys.Protocol, "MSSP");
        await Assert.That(mssp.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AskingForTheArchivedBandLiftsTheArchiveExclusionByItself()
    {
        // The one filter the database and the demo fixture used to answer differently, which is why
        // both now come through this function. Archived games leave the default listing and nothing
        // else (spec §7.5), and choosing the band that names them is not the default listing.
        GameFacetRow[] rows =
        [
            Row("live"),
            Row("gone", band: ActivityBand.Archived),
        ];

        var listing = FacetedSearch.Search(rows, new GameFilter { Band = ActivityBand.Archived });

        await Assert.That(listing.Games.Select(g => g.Slug).ToList()).IsEquivalentTo(new[] { "gone" });
        await Assert.That(FacetedSearch.Search(rows, new GameFilter()).Games.Count).IsEqualTo(1);
    }

    [Test]
    public async Task NeverReachedIsItsOwnBandAndNotTheOldestOne()
    {
        // A game we have listed and never once got an answer from has no last-seen date. Dating it
        // from our own first sighting would publish our ignorance as its outage.
        GameFacetRow[] rows =
        [
            Row("fresh", lastReachableAt: Now.AddHours(-2)),
            Row("stale", lastReachableAt: Now.AddDays(-200)),
            new(
                new GameSummary(
                    Guid.NewGuid(), "silent", "Silent", null, LifecycleState.Active, false, null, null, [],
                    null),
                ActivityBand.Dark,
                FacetedSearch.LastSeenOf(null, Now),
                false, null, null, null, null, null),
        ];

        await Assert.That(FacetedSearch.LastSeenOf(null, Now)).IsEqualTo(LastSeenBand.Never);

        var older = FacetedSearch.Search(rows, new GameFilter { LastSeen = LastSeenBand.Older });
        var never = FacetedSearch.Search(rows, new GameFilter { LastSeen = LastSeenBand.Never });

        await Assert.That(older.Games.Select(g => g.Slug).ToList()).IsEquivalentTo(new[] { "stale" });
        await Assert.That(never.Games.Select(g => g.Slug).ToList()).IsEquivalentTo(new[] { "silent" });
    }

    [Test]
    public async Task TheLastSeenBandsNestSoTheCommonQuestionIsOneChoice()
    {
        GameFacetRow[] rows =
        [
            Row("hour", lastReachableAt: Now.AddHours(-1)),
            Row("days", lastReachableAt: Now.AddDays(-3)),
        ];

        var listing = FacetedSearch.Search(rows, new GameFilter());

        await Assert.That(Value(listing, FacetKeys.LastSeen, "day").Count).IsEqualTo(1);
        await Assert.That(Value(listing, FacetKeys.LastSeen, "week").Count).IsEqualTo(2);
    }

    [Test]
    public async Task TlsIsAMeasuredEndpointAndTheFacetIsAbsentWhenNothingMeasuredOne()
    {
        var without = FacetedSearch.Search([Row("plain")], new GameFilter());
        var with = FacetedSearch.Search([Row("plain"), Row("secure", tls: true)], new GameFilter());

        await Assert.That(without.Facets.Any(f => f.Key == FacetKeys.Tls)).IsFalse();
        await Assert.That(Value(with, FacetKeys.Tls, "yes").Count).IsEqualTo(1);
        await Assert.That(
            FacetedSearch.Search([Row("plain"), Row("secure", tls: true)], new GameFilter { Tls = true })
                .Games.Select(g => g.Slug).ToList())
            .IsEquivalentTo(new[] { "secure" });
    }

    [Test]
    public async Task EveryTokenAFacetOffersIsOneTheFilterVocabularyCanReadBack()
    {
        // The panel emits these and the querystring binding reads them. If the two tables were
        // separate, a facet could offer a value its own parser would refuse — which is a dead end a
        // reader would find by clicking, and nothing else would.
        foreach (var token in FacetTokens.Bands)
        {
            await Assert.That(FacetTokens.TryBand(token, out var band)).IsTrue();
            await Assert.That(FacetTokens.Of(band)).IsEqualTo(token);
        }

        foreach (var token in FacetTokens.LastSeenBands)
        {
            await Assert.That(FacetTokens.TryLastSeen(token, out var seen)).IsTrue();
            await Assert.That(FacetTokens.Of(seen)).IsEqualTo(token);
        }
    }

    [Test]
    public async Task ANumberIsNotAFacetValueHoweverTheEnumIsOrdered()
    {
        // Enum.TryParse accepts the underlying number, which would make band=0 a synonym for
        // whichever member is declared first — a facet that silently re-points itself the day
        // somebody reorders the enum.
        await Assert.That(FacetTokens.TryBand("0", out _)).IsFalse();
        await Assert.That(FacetTokens.TryLastSeen("4", out _)).IsFalse();

        // Separators are forgiven, because all three spellings are what people type.
        await Assert.That(FacetTokens.TryBand("active-this-week", out var band)).IsTrue();
        await Assert.That(band).IsEqualTo(ActivityBand.ActiveThisWeek);
    }

    /// <summary>
    /// The codebase facet counts families, so one codebase is one value however many patchlevels of
    /// it are running.
    /// </summary>
    /// <remarks>
    /// This shipped counting the raw <c>CODEBASE</c> string and was only visible against a real
    /// crawl, where the panel offered <c>PennMUSH 1.8.8p0</c>, <c>PennMUSH 1.8.7p0</c> and
    /// <c>PennMUSH 1.8.6p1</c> as three unrelated choices and spent a quarter of its twelve slots
    /// doing it.
    /// </remarks>
    [Test]
    public async Task OneCodebaseIsOneValueWhateverItsPatchlevel()
    {
        GameFacetRow[] rows =
        [
            Row("a", codebase: "PennMUSH 1.8.8p0"),
            Row("b", codebase: "PennMUSH 1.8.7p0"),
            Row("c", codebase: "PennMUSH 1.8.6p1"),
            Row("d", codebase: "Evennia"),
        ];

        var listing = FacetedSearch.Search(rows, new GameFilter());

        await Assert.That(Value(listing, FacetKeys.Codebase, "PennMUSH").Count).IsEqualTo(3);
        await Assert.That(Group(listing, FacetKeys.Codebase).Values.Select(v => v.Token))
            .IsEquivalentTo(new[] { "PennMUSH", "Evennia" });

        // And the exact strings are still all there, one facet down.
        await Assert.That(Group(listing, FacetKeys.CodebaseVersion).Values.Select(v => v.Token))
            .IsEquivalentTo(new[] { "PennMUSH 1.8.8p0", "PennMUSH 1.8.7p0", "PennMUSH 1.8.6p1", "Evennia" });
    }

    /// <summary>The lineage facet gathers codebases that share no name and no MSSP.</summary>
    /// <remarks>
    /// The question this whole facet exists for: the MUSH codebases are five separate answers to
    /// <c>CODEBASE</c>, all but one of them publish no MSSP at all, and MSSP's <c>FAMILY</c> has no
    /// <c>MUSH</c> in it even for the one that does. Nothing a game says can group them.
    /// </remarks>
    [Test]
    public async Task TheLineageFacetGathersWhatNoDeclarationCould()
    {
        GameFacetRow[] rows =
        [
            Row("a", codebase: "PennMUSH 1.8.8p0"),
            Row("b", codebase: "TinyMUX 2.12"),
            Row("c", codebase: "RhostMUSH"),
            Row("d", codebase: "AresMUSH"),
            Row("e", codebase: "ROM 2.4"),
            Row("f", codebase: null),
        ];

        var listing = FacetedSearch.Search(rows, new GameFilter());

        await Assert.That(Value(listing, FacetKeys.Lineage, CodebaseLineage.Mush).Count).IsEqualTo(4);
        await Assert.That(Value(listing, FacetKeys.Lineage, CodebaseLineage.Diku).Count).IsEqualTo(1);

        // Its evidence is neither of the other two words, and the panel has to be able to say so.
        await Assert.That(Group(listing, FacetKeys.Lineage).Evidence).IsEqualTo(FacetEvidence.Derived);
    }

    /// <summary>A value's label is the commonest spelling, not whichever row was read first.</summary>
    /// <remarks>
    /// Values group case-insensitively, so the label is a choice rather than a given. Taking the
    /// first one seen makes it a function of the sort order — the same catalogue could name a facet
    /// two different things on two renders — and it lets one game's stray capitalisation put a
    /// codebase on a public page under a spelling nobody uses.
    /// </remarks>
    [Test]
    public async Task OneGamesCapitalisationDoesNotNameAValue()
    {
        GameFacetRow[] rows =
        [
            Row("a", codebase: "pennmush 1.8.8p0"),
            Row("b", codebase: "PennMUSH 1.8.7p0"),
            Row("c", codebase: "PennMUSH 1.8.6p1"),
        ];

        var listing = FacetedSearch.Search(rows, new GameFilter());
        var codebase = Group(listing, FacetKeys.Codebase).Values.Single();

        await Assert.That(codebase.Token).IsEqualTo("PennMUSH");
        await Assert.That(codebase.Count).IsEqualTo(3);
    }

    private static GameFilter Choose(string key, string token) => key switch
    {
        FacetKeys.Band => new GameFilter { Band = Band(token) },
        FacetKeys.LastSeen => new GameFilter { LastSeen = Seen(token) },
        FacetKeys.Protocol => new GameFilter { MeasuredProtocols = [token] },
        FacetKeys.Tls => new GameFilter { Tls = true },
        FacetKeys.Charset => new GameFilter { Charset = FacetChoice.Parse(token) },
        FacetKeys.Codebase => new GameFilter { Codebase = FacetChoice.Parse(token) },
        FacetKeys.CodebaseVersion => new GameFilter { CodebaseVersion = FacetChoice.Parse(token) },
        FacetKeys.Lineage => new GameFilter { Lineage = FacetChoice.Parse(token) },
        FacetKeys.Family => new GameFilter { Family = FacetChoice.Parse(token) },
        FacetKeys.Genre => new GameFilter { Genre = FacetChoice.Parse(token) },
        FacetKeys.Language => new GameFilter { Language = FacetChoice.Parse(token) },

        // Never a catch-all: a facet added to the panel and not to this switch would arrive here as
        // a language filter and quietly assert the wrong thing about the wrong key.
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "no filter for this facet"),
    };

    private static ActivityBand Band(string token)
    {
        FacetTokens.TryBand(token, out var band);
        return band;
    }

    private static LastSeenBand Seen(string token)
    {
        FacetTokens.TryLastSeen(token, out var seen);
        return seen;
    }
}
