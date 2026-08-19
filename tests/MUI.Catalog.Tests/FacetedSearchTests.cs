namespace MUI.Catalog.Tests;

/// <summary>
/// The rules the facet panel is built on, asserted with no database and no markup in the way.
/// </summary>
/// <remarks>
/// Two design rules drive most of these: <b>a count is what a click returns</b>, so each count is
/// checked by actually running the filter it advertises and comparing sizes; and <b>an unknown is not
/// a no</b> — a game with no value for a facet must be findable as such and never returned by a choice
/// of some other value.
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
        string[]? protocols = null,
        bool uncounted = false,
        bool unreachable = false,
        GrowthDirection? growth = null)
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
            genre,

            // Adult content has its own suite (AdultListingTests); every row here declares none.
            IsAdult: false,

            // Default false: a row is reached and counted unless a test says otherwise.
            uncounted,
            unreachable,
            growth);
    }

    private static FacetGroup Group(GameListing listing, string key) =>
        listing.Facets.Single(f => f.Key == key);

    private static FacetValue Value(GameListing listing, string key, string token) =>
        Group(listing, key).Values.Single(v => v.Token == token);

    [Test]
    public async Task EveryCountIsExactlyWhatChoosingThatValueReturns()
    {
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
        // A facet's values are counted against the rest of the filter, so the number beside
        // "Evennia" while genre=Fantasy is chosen is the intersection, not the total.
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
        // A choice facet replaces rather than intersects, so its siblings must be counted with its
        // own selection lifted, or they'd all read zero and the panel would be a one-way door.
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
        var listing = FacetedSearch.Search([Row("a", protocols: ["GMCP"])], new GameFilter());
        var protocols = Group(listing, FacetKeys.Protocol).Values.Select(v => v.Token).ToList();

        await Assert.That(protocols).IsEquivalentTo(new[] { "GMCP" });
    }

    [Test]
    public async Task ProtocolsIntersectAndTheTickBoxNeverMeansTheGameLacksIt()
    {
        // No way to ask for the complement: a capability is written only when observed, so "not
        // listed" covers an unmeasured game as well as one that declined, and only one is a fact.
        GameFacetRow[] rows =
        [
            Row("both", protocols: ["GMCP", "MSSP"]),
            Row("one", protocols: ["MSSP"]),
        ];

        var listing = FacetedSearch.Search(rows, new GameFilter { MeasuredProtocols = ["GMCP", "MSSP"] });

        await Assert.That(listing.Games.Select(g => g.Slug).ToList()).IsEquivalentTo(new[] { "both" });

        var mssp = Value(FacetedSearch.Search(rows, new GameFilter { MeasuredProtocols = ["GMCP"] }),
            FacetKeys.Protocol, "MSSP");
        await Assert.That(mssp.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AskingForTheArchivedBandLiftsTheArchiveExclusionByItself()
    {
        // Archived games leave the default listing and nothing else (spec §7.5); choosing the band
        // that names them is not the default listing.
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
        // A game never once answered has no last-seen date; dating it from our first sighting would
        // publish our ignorance as its outage.
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
                false, null, null, null, null, null, false,

                // Never once reached, so unreachable — and NOT uncounted, because we hold no
                // presence row for it at all. Naming a cause for that is what rule 2 forbids.
                Uncounted: false,
                Unreachable: true),
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
        // If emit and parse were separate tables, a facet could offer a value its own parser refuses.
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
        // whichever member is declared first and silently re-point on an enum reorder.
        await Assert.That(FacetTokens.TryBand("0", out _)).IsFalse();
        await Assert.That(FacetTokens.TryLastSeen("4", out _)).IsFalse();

        await Assert.That(FacetTokens.TryBand("active-this-week", out var band)).IsTrue();
        await Assert.That(band).IsEqualTo(ActivityBand.ActiveThisWeek);
    }

    /// <summary>
    /// The codebase facet counts families, so one codebase is one value however many patchlevels of
    /// it are running.
    /// </summary>
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

        await Assert.That(Group(listing, FacetKeys.CodebaseVersion).Values.Select(v => v.Token))
            .IsEquivalentTo(new[] { "PennMUSH 1.8.8p0", "PennMUSH 1.8.7p0", "PennMUSH 1.8.6p1", "Evennia" });
    }

    /// <summary>The lineage facet gathers codebases that share no name and no MSSP.</summary>
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

        await Assert.That(Group(listing, FacetKeys.Lineage).Evidence).IsEqualTo(FacetEvidence.Derived);
    }

    /// <summary>A value's label is the commonest spelling, not whichever row was read first.</summary>
    /// <remarks>
    /// Values group case-insensitively, so the label is a choice. Taking the first one seen would
    /// make it a function of sort order, and let one game's stray capitalisation name the value.
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

    // ── what we could measure ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The set these four rows describe, once, so every assertion below is about the same catalogue.
    /// </summary>
    /// <remarks>
    /// <c>zero</c> is a game we got into and counted nobody in; <c>unreadable</c> is a game we got
    /// into and could not count. Both sit in <see cref="ActivityBand.Quiet"/> — the band cannot tell
    /// them apart — and only the second is uncounted. <c>gone</c> is the third state: never reached.
    /// </remarks>
    private static GameFacetRow[] Measured() =>
    [
        Row("zero", band: ActivityBand.Quiet, genre: "Fantasy"),
        Row("unreadable", band: ActivityBand.Quiet, genre: "Fantasy", uncounted: true),
        Row("busy", genre: "Historical"),
        Row("gone", band: ActivityBand.Dark, genre: "Fantasy",
            lastReachableAt: Now.AddDays(-90), unreachable: true),
    ];

    [Test]
    public async Task AGameMeasuredAtZeroIsNotUncounted()
    {
        // A measured nought is a count (rule 2), even though it shares an activity band with a game
        // whose every WHO was unreadable.
        var rows = Measured();

        var uncounted = FacetedSearch.Search(rows, new GameFilter { Uncounted = FacetChoice.Of(FacetTokens.Yes) });

        await Assert.That(uncounted.Games.Select(g => g.Slug)).IsEquivalentTo(new[] { "unreadable" });

        var quiet = FacetedSearch.Search(rows, new GameFilter { Band = ActivityBand.Quiet });

        await Assert.That(quiet.Games.Select(g => g.Slug)).IsEquivalentTo(new[] { "zero", "unreadable" });
    }

    [Test]
    public async Task AGameWeHaveNotMeasuredIsNeitherUncountedNorCounted()
    {
        // §5.4's third state, which names no cause. `gone` has no presence rows at all, so "uncounted"
        // (we tried and could not read) would be a fabrication in the other direction.
        var rows = Measured();

        var uncounted = FacetedSearch.Search(rows, new GameFilter { Uncounted = FacetChoice.Of(FacetTokens.Yes) });
        var unreachable = FacetedSearch.Search(rows, new GameFilter { Unreachable = FacetChoice.Of(FacetTokens.Yes) });

        await Assert.That(uncounted.Games.Any(g => g.Slug == "gone")).IsFalse();
        await Assert.That(unreachable.Games.Select(g => g.Slug)).IsEquivalentTo(new[] { "gone" });
    }

    [Test]
    public async Task TheTwoSwitchesComposeWithEachOtherAndWithAnUnrelatedFacet()
    {
        // Two separate FacetChoices, not two values of `band`, so a reader can drop both kinds of
        // unmeasured game without spending the one selection `band` has.
        var rows = Measured();

        var listing = FacetedSearch.Search(rows, new GameFilter
        {
            Genre = FacetChoice.Of("Fantasy"),
            Uncounted = FacetChoice.Not(FacetTokens.Yes),
            Unreachable = FacetChoice.Not(FacetTokens.Yes),
        });

        await Assert.That(listing.Games.Select(g => g.Slug)).IsEquivalentTo(new[] { "zero" });
    }

    [Test]
    public async Task ExcludingBothStillLeavesAListingThatExplainsItself()
    {
        // Hiding is a decision about the listing, so the controls that made it must stay reachable —
        // a selection whose only affordance has vanished is what this test guards against.
        var rows = Measured();

        var listing = FacetedSearch.Search(rows, new GameFilter
        {
            Uncounted = FacetChoice.Not(FacetTokens.Yes),
            Unreachable = FacetChoice.Not(FacetTokens.Yes),
        });

        await Assert.That(listing.Games.Select(g => g.Slug)).IsEquivalentTo(new[] { "zero", "busy" });

        foreach (var key in new[] { FacetKeys.Uncounted, FacetKeys.Unreachable })
        {
            var value = Value(listing, key, FacetTokens.Yes);

            await Assert.That(value.State).IsEqualTo(FacetState.Excluded);
            await Assert.That(value.Count).IsEqualTo(1);
        }
    }

    [Test]
    public async Task TheCountBesideEachSwitchIsWhatChoosingItReturns()
    {
        var rows = Measured();
        var listing = FacetedSearch.Search(rows, new GameFilter());

        foreach (var key in new[] { FacetKeys.Uncounted, FacetKeys.Unreachable })
        {
            var value = Value(listing, key, FacetTokens.Yes);

            await Assert.That(value.Count).IsEqualTo(1);
            await Assert.That(FacetedSearch.Search(rows, Choose(key, value.Token)).Games.Count)
                .IsEqualTo(value.Count);
        }
    }

    [Test]
    public async Task ASwitchNothingMatchesIsNotDrawnAtAll()
    {
        // Unlike a bounded scale, this is one fact held or not — a catalogue fully counted and
        // reached offers no control for it.
        var listing = FacetedSearch.Search([Row("busy"), Row("also")], new GameFilter());

        await Assert.That(listing.Facets.Any(f => f.Key == FacetKeys.Uncounted)).IsFalse();
        await Assert.That(listing.Facets.Any(f => f.Key == FacetKeys.Unreachable)).IsFalse();
    }

    private static GameFilter Choose(string key, string token) => key switch
    {
        FacetKeys.Band => new GameFilter { Band = Band(token) },
        FacetKeys.LastSeen => new GameFilter { LastSeen = Seen(token) },
        FacetKeys.Uncounted => new GameFilter { Uncounted = FacetChoice.Parse(token) },
        FacetKeys.Unreachable => new GameFilter { Unreachable = FacetChoice.Parse(token) },
        FacetKeys.Protocol => new GameFilter { MeasuredProtocols = [token] },
        FacetKeys.Tls => new GameFilter { Tls = true },
        FacetKeys.Charset => new GameFilter { Charset = FacetChoice.Parse(token) },
        FacetKeys.Codebase => new GameFilter { Codebase = FacetChoice.Parse(token) },
        FacetKeys.CodebaseVersion => new GameFilter { CodebaseVersion = FacetChoice.Parse(token) },
        FacetKeys.Lineage => new GameFilter { Lineage = FacetChoice.Parse(token) },
        FacetKeys.Family => new GameFilter { Family = FacetChoice.Parse(token) },
        FacetKeys.Trending => new GameFilter { Trending = FacetChoice.Parse(token) },
        FacetKeys.Genre => new GameFilter { Genre = FacetChoice.Parse(token) },
        FacetKeys.Language => new GameFilter { Language = FacetChoice.Parse(token) },

        // Never a catch-all: a facet added to the panel and not to this switch would arrive here as
        // a language filter and quietly assert the wrong thing about the wrong key.
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "no filter for this facet"),
    };

    [Test]
    public async Task TrendingFiltersToExactlyTheGamesInThatDirection()
    {
        GameFacetRow[] rows =
        [
            Row("rising", growth: GrowthDirection.Up),
            Row("falling", growth: GrowthDirection.Down),
            Row("flat", growth: GrowthDirection.Steady),
            Row("too-new", growth: null),
        ];

        var listing = FacetedSearch.Search(rows, new GameFilter());
        var up = FacetedSearch.Search(rows, new GameFilter { Trending = FacetChoice.Of("up") });

        await Assert.That(Value(listing, FacetKeys.Trending, "up").Count).IsEqualTo(1);
        await Assert.That(up.Games.Select(g => g.Slug)).IsEquivalentTo(new[] { "rising" });

        var group = Group(listing, FacetKeys.Trending);
        await Assert.That(group.Evidence).IsEqualTo(FacetEvidence.Derived);
    }

    [Test]
    public async Task AGameWithNoPriorWeekIsFindableAsTrendingUnknown()
    {
        GameFacetRow[] rows = [Row("rising", growth: GrowthDirection.Up), Row("too-new", growth: null)];

        var unknown = FacetedSearch.Search(
            rows, new GameFilter { Trending = FacetChoice.Parse(FacetChoice.UnknownToken) });

        await Assert.That(unknown.Games.Select(g => g.Slug)).IsEquivalentTo(new[] { "too-new" });
    }

    [Test]
    public async Task TheVersionFacetShowsMoreThanTheOldTwelveValueCap()
    {
        // Sixteen distinct patchlevels, one game each — more than the panel used to show. Raising
        // the cap is what surfaces the difference here, not deduping happening to land under it.
        var rows = Enumerable.Range(1, 16)
            .Select(i => Row($"game-{i}", codebase: $"PennMUSH 1.8.{i}"))
            .ToArray();

        var listing = FacetedSearch.Search(rows, new GameFilter());
        var group = Group(listing, FacetKeys.CodebaseVersion);

        await Assert.That(group.Values.Count).IsGreaterThan(12);
    }

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
