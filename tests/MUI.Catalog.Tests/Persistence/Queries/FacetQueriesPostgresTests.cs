using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// The facets against a real database — which column each one reads, and what it does with silence.
/// </summary>
/// <remarks>
/// <see cref="FacetedSearchTests"/> covers the arithmetic with no I/O. What can only be checked here
/// is that the facets read the right column when a measured and a declared one sit side by side under
/// nearly the same name — e.g. that the protocol facet reads <c>capability.gmcp.measured</c> and not
/// <c>.declared</c>, and TLS comes off an endpoint rather than a claim.
/// </remarks>
public class FacetQueriesPostgresTests
{
    private static readonly DateTimeOffset Now = Seed.Now;

    private static NpgsqlGameQueries QueriesOn(TestDatabase db) =>
        new(db.DataSource, time: new FixedClock(Now));

    private static FacetGroup? Group(GameListing listing, string key) =>
        listing.Facets.FirstOrDefault(f => f.Key == key);

    [Test]
    public async Task TheProtocolFacetCountsWhatWasMeasuredAndNotWhatWasClaimed()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var measured = await Seed.GameAsync(db, "measured", "Measured", lastReachableAt: Now);
        var claimed = await Seed.GameAsync(db, "claimed", "Claimed", lastReachableAt: Now);
        var fields = new NpgsqlGameFieldStore(db.DataSource);

        await fields.UpsertAsync(new GameField(
            measured, CapabilityFields.Measured("GMCP"), FieldSource.Handshake, "true", Now, Now));
        await fields.UpsertAsync(new GameField(
            claimed, CapabilityFields.Declared("GMCP"), FieldSource.Mssp, "true", Now, Now));

        var listing = await QueriesOn(db).SearchAsync(new GameFilter());
        var gmcp = Group(listing, FacetKeys.Protocol)!.Values.Single(v => v.Token == "GMCP");

        await Assert.That(gmcp.Count).IsEqualTo(1);
        await Assert.That(
            (await QueriesOn(db).ListAsync(new GameFilter { MeasuredProtocols = ["GMCP"] }))
                .Select(g => g.Slug).ToList())
            .IsEquivalentTo(new[] { "measured" });
    }

    [Test]
    public async Task AGameThatOnlyDeclaredAProtocolIsNotFoldedIntoAnyProtocolAnswer()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var claimed = await Seed.GameAsync(db, "claimed", "Claimed", lastReachableAt: Now);
        await new NpgsqlGameFieldStore(db.DataSource).UpsertAsync(new GameField(
            claimed, CapabilityFields.Declared("GMCP"), FieldSource.Mssp, "true", Now, Now));

        var listing = await QueriesOn(db).SearchAsync(new GameFilter());

        await Assert.That(Group(listing, FacetKeys.Protocol)).IsNull();
        await Assert.That(listing.Games).Count().IsEqualTo(1);
    }

    [Test]
    public async Task TheAdultDefaultReadsBothDeclarationsOffRealRows()
    {
        // GENRE and ADULT MATERIAL are two separate variables, catching different games — the flag is
        // the only way a game whose GENRE is Fantasy can still declare adult content.
        await using var db = await PostgresFixture.MigratedAsync();
        var byGenre = await Seed.GameAsync(db, "by-genre", "By Genre", lastReachableAt: Now);
        var byFlag = await Seed.GameAsync(db, "by-flag", "By Flag", lastReachableAt: Now);
        var says = await Seed.GameAsync(db, "says-no", "Says No", lastReachableAt: Now);
        await Seed.GameAsync(db, "silent", "Silent", lastReachableAt: Now);
        var fields = new NpgsqlGameFieldStore(db.DataSource);

        await fields.UpsertAsync(new GameField(
            byGenre, "GENRE", FieldSource.Mssp, AdultContent.Genre, Now, Now));
        await fields.UpsertAsync(new GameField(
            byFlag, "GENRE", FieldSource.Mssp, "Fantasy", Now, Now));
        await fields.UpsertAsync(new GameField(
            byFlag, AdultContent.Field, FieldSource.Mssp, "1", Now, Now));

        // A "no" and a never-answered game are both not "declared adult content"; both stay listed.
        await fields.UpsertAsync(new GameField(
            says, AdultContent.Field, FieldSource.Mssp, "0", Now, Now));

        var listed = await QueriesOn(db).ListAsync(new GameFilter { IncludeAdult = false });
        var all = await QueriesOn(db).ListAsync(new GameFilter());

        await Assert.That(listed.Select(g => g.Slug).ToList())
            .IsEquivalentTo(new[] { "says-no", "silent" });
        await Assert.That(all).Count().IsEqualTo(4);
    }

    [Test]
    public async Task AnOwnerCanCorrectTheFlagTheirServerNeverSent()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "corrected", "Corrected", lastReachableAt: Now);
        var fields = new NpgsqlGameFieldStore(db.DataSource);

        await fields.UpsertAsync(new GameField(game, "GENRE", FieldSource.Mssp, "Fantasy", Now, Now));

        await Assert.That((await QueriesOn(db).ListAsync(new GameFilter { IncludeAdult = false })).Count)
            .IsEqualTo(1);

        await fields.UpsertAsync(new GameField(
            game, AdultContent.Field, FieldSource.Owner, "1", Now, Now));

        await Assert.That((await QueriesOn(db).ListAsync(new GameFilter { IncludeAdult = false })).Count)
            .IsEqualTo(0);
    }

    [Test]
    public async Task TheCharsetFacetReadsWhatWasNegotiatedAndNotWhatMsspClaimed()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var negotiated = await Seed.GameAsync(db, "negotiated", "Negotiated", lastReachableAt: Now);
        var asserted = await Seed.GameAsync(db, "asserted", "Asserted", lastReachableAt: Now);
        var fields = new NpgsqlGameFieldStore(db.DataSource);

        await fields.UpsertAsync(new GameField(
            negotiated, "CHARSET", FieldSource.Handshake, "UTF-8", Now, Now));
        await fields.UpsertAsync(new GameField(
            asserted, "CHARSET", FieldSource.Mssp, "UTF-8", Now, Now));

        var listing = await QueriesOn(db).SearchAsync(new GameFilter());
        var charset = Group(listing, FacetKeys.Charset)!;

        await Assert.That(charset.Values.Single(v => v.Token == "UTF-8").Count).IsEqualTo(1);
        await Assert.That(charset.Values.Single(v => v.IsUnknown).Count).IsEqualTo(1);
        await Assert.That(charset.Evidence).IsEqualTo(FacetEvidence.Measured);

        var chosen = await QueriesOn(db).ListAsync(
            new GameFilter { Charset = FacetChoice.Of("UTF-8") });
        await Assert.That(chosen.Select(g => g.Slug).ToList()).IsEquivalentTo(new[] { "negotiated" });
    }

    [Test]
    public async Task NothingNegotiatedIsItsOwnAnswerAndNotAnAbsenceOfUtf8()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        await Seed.GameAsync(db, "silent", "Silent", lastReachableAt: Now);

        var listing = await QueriesOn(db).SearchAsync(new GameFilter { Charset = FacetChoice.Unknown });

        await Assert.That(listing.Games.Select(g => g.Slug).ToList()).IsEquivalentTo(new[] { "silent" });
        await Assert.That(Group(listing, FacetKeys.Charset)!.Values.Single().IsUnknown).IsTrue();
    }

    [Test]
    public async Task TlsIsAnEndpointWeOpenedAndNeverAnSslLineInMssp()
    {
        // MSSP's SSL variable folds onto capability.tls.declared, but only an endpoint of kind Tls —
        // a socket we actually opened — is a measurement, and the facet reads only that.
        await using var db = await PostgresFixture.MigratedAsync();
        var secure = await Seed.GameAsync(db, "secure", "Secure", lastReachableAt: Now);
        var boastful = await Seed.GameAsync(db, "boastful", "Boastful", lastReachableAt: Now);

        await new NpgsqlEndpointStore(db.DataSource).UpsertAsync(new GameEndpoint(
            secure, "secure.example", 4202, EndpointKind.Tls, Now, Now, EndpointState.Active));
        await new NpgsqlGameFieldStore(db.DataSource).UpsertAsync(new GameField(
            boastful, CapabilityFields.Declared("TLS"), FieldSource.Mssp, "true", Now, Now));

        var listing = await QueriesOn(db).SearchAsync(new GameFilter());

        await Assert.That(Group(listing, FacetKeys.Tls)!.Values.Single().Count).IsEqualTo(1);
        await Assert.That(
            (await QueriesOn(db).ListAsync(new GameFilter { Tls = true })).Select(g => g.Slug).ToList())
            .IsEquivalentTo(new[] { "secure" });
    }

    [Test]
    public async Task ACodebaseWeCouldNotIdentifyIsItsOwnBucketAndCanBeAskedFor()
    {
        // Survives the cap on open-ended facets: it's exactly the value a popularity cut would
        // otherwise delete on a well-covered catalogue.
        await using var db = await PostgresFixture.MigratedAsync();
        var known = await Seed.GameAsync(db, "known", "Known", lastReachableAt: Now);
        await Seed.GameAsync(db, "mystery", "Mystery", lastReachableAt: Now);

        await new NpgsqlGameFieldStore(db.DataSource).UpsertAsync(new GameField(
            known, "CODEBASE", FieldSource.Mssp, "Evennia", Now, Now));

        var listing = await QueriesOn(db).SearchAsync(new GameFilter());
        var codebase = Group(listing, FacetKeys.Codebase)!;

        await Assert.That(codebase.Evidence).IsEqualTo(FacetEvidence.Declared);
        await Assert.That(codebase.Values.Single(v => v.IsUnknown).Count).IsEqualTo(1);
        await Assert.That(
            (await QueriesOn(db).ListAsync(new GameFilter { Codebase = FacetChoice.Unknown }))
                .Select(g => g.Slug).ToList())
            .IsEquivalentTo(new[] { "mystery" });
    }

    [Test]
    public async Task TheArchivedBandLiftsTheArchiveExclusionInTheDatabaseToo()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        await Seed.GameAsync(db, "corvid", "Corvid", lastReachableAt: Now);
        await Seed.GameAsync(db, "gaslight-row", "Gaslight Row", LifecycleState.Archived);

        var archived = await QueriesOn(db).ListAsync(new GameFilter { Band = ActivityBand.Archived });

        await Assert.That(archived.Select(g => g.Slug).ToList()).IsEquivalentTo(new[] { "gaslight-row" });
    }

    [Test]
    public async Task TheLastSeenFacetCarriesTheDateItFilteredOnOntoTheRowsItReturned()
    {
        // Never reached stays null rather than being dated from our first sighting.
        await using var db = await PostgresFixture.MigratedAsync();
        await Seed.GameAsync(db, "fresh", "Fresh", lastReachableAt: Now.AddHours(-2));
        await Seed.GameAsync(db, "silent", "Silent");

        var listing = await QueriesOn(db).SearchAsync(new GameFilter());
        var byslug = listing.Games.ToDictionary(g => g.Slug);
        var seen = Group(listing, FacetKeys.LastSeen)!;

        await Assert.That(byslug["fresh"].LastReachableAt).IsEqualTo(Now.AddHours(-2));
        await Assert.That(byslug["silent"].LastReachableAt).IsNull();
        await Assert.That(seen.Values.Single(v => v.Token == "never").Count).IsEqualTo(1);
        await Assert.That(seen.Values.Single(v => v.Token == "day").Count).IsEqualTo(1);
    }

    [Test]
    public async Task EveryCountTheDatabasePublishesIsWhatChoosingThatValueReturns()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var penn = await Seed.GameAsync(db, "penn", "Penn", lastReachableAt: Now);
        var evennia = await Seed.GameAsync(db, "evennia", "Evennia game", lastReachableAt: Now.AddDays(-10));
        await Seed.GameAsync(db, "quiet-one", "Quiet one", lastReachableAt: Now.AddDays(-200));
        var fields = new NpgsqlGameFieldStore(db.DataSource);

        await fields.UpsertAsync(new GameField(penn, "CODEBASE", FieldSource.Mssp, "PennMUSH", Now, Now));
        await fields.UpsertAsync(new GameField(penn, "GENRE", FieldSource.Mssp, "Fantasy", Now, Now));
        await fields.UpsertAsync(new GameField(
            evennia, "CODEBASE", FieldSource.Mssp, "Evennia", Now, Now));
        await fields.UpsertAsync(new GameField(
            penn, CapabilityFields.Measured("MSSP"), FieldSource.Handshake, "true", Now, Now));

        var queries = QueriesOn(db);
        var listing = await queries.SearchAsync(new GameFilter());

        foreach (var group in listing.Facets)
        {
            foreach (var value in group.Values)
            {
                var games = await queries.ListAsync(Choose(group.Key, value.Token));

                await Assert.That(games.Count)
                    .IsEqualTo(value.Count)
                    .Because($"{group.Key}={value.Token} advertised {value.Count}");
            }
        }
    }

    [Test]
    public async Task AMeasuredZeroIsNotUncountedAndTheBandCannotTellThemApart()
    {
        // Both games land in `quiet` — the band is one threshold and cannot say why. `empty` was
        // probed and answered nought (a measurement); `unreadable` was probed and produced no number
        // (a limit of ours). Conflating them is rules 2, 4 and 5 in one control.
        await using var db = await PostgresFixture.MigratedAsync();
        var empty = await Seed.GameAsync(db, "empty", "Measured empty", lastReachableAt: Now);
        var unreadable = await Seed.GameAsync(db, "unreadable", "Unreadable", lastReachableAt: Now);
        var writer = new PresenceWriter(new NpgsqlPresenceStore(db.DataSource));

        foreach (var hours in new[] { 1, 20, 60 })
        {
            await writer.WriteAsync(
                empty, PresenceReading.Counted(0, FieldSource.Who), Now.AddHours(-hours));
            await writer.WriteAsync(
                unreadable,
                PresenceReading.Unmeasurable(UnmeasurableReason.WhoUnparseable),
                Now.AddHours(-hours));
        }

        var queries = QueriesOn(db);

        var quiet = await queries.ListAsync(new GameFilter { Band = ActivityBand.Quiet });
        await Assert.That(quiet.Select(g => g.Slug)).IsEquivalentTo(new[] { "empty", "unreadable" });

        var uncounted = await queries.ListAsync(
            new GameFilter { Uncounted = FacetChoice.Of(FacetTokens.Yes) });
        await Assert.That(uncounted.Select(g => g.Slug)).IsEquivalentTo(new[] { "unreadable" });

        var listed = await queries.ListAsync(new GameFilter());
        await Assert.That(listed.Single(g => g.Id == empty).PlayersNow).IsEqualTo(0);
        await Assert.That(listed.Single(g => g.Id == unreadable).PlayersNow).IsNull();
    }

    [Test]
    public async Task AGameWithNoPresenceRowsAtAllIsNotUncounted()
    {
        // §5.4's third state: a probe that failed writes no presence row, so a game with nothing
        // stored was not measured, not "tried and failed to read". It is unreachable instead.
        await using var db = await PostgresFixture.MigratedAsync();
        await Seed.GameAsync(db, "never", "Never answered", lastReachableAt: null);
        var probed = await Seed.GameAsync(db, "probed", "Probed", lastReachableAt: Now);
        var writer = new PresenceWriter(new NpgsqlPresenceStore(db.DataSource));

        await writer.WriteAsync(
            probed, PresenceReading.Unmeasurable(UnmeasurableReason.WhoLoginPrompt), Now.AddHours(-2));

        var queries = QueriesOn(db);

        await Assert.That(
            (await queries.ListAsync(new GameFilter { Uncounted = FacetChoice.Of(FacetTokens.Yes) }))
                .Select(g => g.Slug))
            .IsEquivalentTo(new[] { "probed" });

        await Assert.That(
            (await queries.ListAsync(new GameFilter { Unreachable = FacetChoice.Of(FacetTokens.Yes) }))
                .Select(g => g.Slug))
            .IsEquivalentTo(new[] { "never" });
    }

    [Test]
    public async Task TheTwoSwitchesNarrowTogetherAndTheCountsSayWhatTheyReturn()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var counted = await Seed.GameAsync(db, "counted", "Counted", lastReachableAt: Now);
        var unreadable = await Seed.GameAsync(db, "unreadable", "Unreadable", lastReachableAt: Now);
        await Seed.GameAsync(db, "gone", "Gone", lastReachableAt: Now.AddDays(-90));
        var writer = new PresenceWriter(new NpgsqlPresenceStore(db.DataSource));

        await writer.WriteAsync(counted, PresenceReading.Counted(4, FieldSource.Who), Now.AddHours(-1));
        await writer.WriteAsync(
            unreadable, PresenceReading.Unmeasurable(UnmeasurableReason.I3NoReply), Now.AddHours(-1));

        var queries = QueriesOn(db);

        var kept = await queries.SearchAsync(new GameFilter
        {
            Uncounted = FacetChoice.Not(FacetTokens.Yes),
            Unreachable = FacetChoice.Not(FacetTokens.Yes),
        });

        await Assert.That(kept.Games.Select(g => g.Slug)).IsEquivalentTo(new[] { "counted" });

        foreach (var key in new[] { FacetKeys.Uncounted, FacetKeys.Unreachable })
        {
            var value = Group(kept, key)!.Values.Single(v => v.Token == FacetTokens.Yes);

            await Assert.That(value.State).IsEqualTo(FacetState.Excluded);
            await Assert.That((await queries.ListAsync(Choose(key, value.Token))).Count)
                .IsEqualTo(value.Count);
        }
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
        FacetKeys.Genre => new GameFilter { Genre = FacetChoice.Parse(token) },
        FacetKeys.Language => new GameFilter { Language = FacetChoice.Parse(token) },

        // Never a catch-all: a facet missing from this switch would silently fall through as a
        // language filter and fail with a misleading "count was wrong" instead.
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

    private sealed class FixedClock(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }
}
