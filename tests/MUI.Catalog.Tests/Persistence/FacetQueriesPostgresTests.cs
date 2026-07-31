using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// The facets against a real database — which column each one reads, and what it does with silence.
/// </summary>
/// <remarks>
/// <see cref="FacetedSearchTests"/> covers the arithmetic with no I/O. What can only be checked here
/// is the half that reads rows: that <c>capability.gmcp.measured</c> and
/// <c>capability.gmcp.declared</c> are two columns and the facet reads the first, that a CHARSET
/// row's <em>source</em> decides whether it counts, and that TLS comes off an endpoint rather than
/// off a claim. Every one of those is a place where the honest column and the convenient one sit
/// side by side under nearly the same name.
/// </remarks>
public class FacetQueriesPostgresTests
{
    private static readonly DateTimeOffset Now = Seed.Now;

    private static NpgsqlGameQueries QueriesOn(TestDatabase db) =>
        new(db.DataSource) { Clock = () => Now };

    private static FacetGroup? Group(GameListing listing, string key) =>
        listing.Facets.FirstOrDefault(f => f.Key == key);

    [Test]
    public async Task TheProtocolFacetCountsWhatWasMeasuredAndNotWhatWasClaimed()
    {
        // The central one. Both games "have GMCP" in the loose sense; only one of them was seen
        // offering it, and a facet that read the declared column would return the pair and call it
        // measurement — which is the lie the whole schema is shaped to prevent.
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
        // And it is not the other error either: the declaring game is neither counted as offering
        // GMCP nor recorded anywhere as refusing it. It is simply a game we have not measured, and
        // the facet has no vocabulary for saying otherwise.
        await using var db = await PostgresFixture.MigratedAsync();
        var claimed = await Seed.GameAsync(db, "claimed", "Claimed", lastReachableAt: Now);
        await new NpgsqlGameFieldStore(db.DataSource).UpsertAsync(new GameField(
            claimed, CapabilityFields.Declared("GMCP"), FieldSource.Mssp, "true", Now, Now));

        var listing = await QueriesOn(db).SearchAsync(new GameFilter());

        await Assert.That(Group(listing, FacetKeys.Protocol)).IsNull();
        await Assert.That(listing.Games).Count().IsEqualTo(1);
    }

    [Test]
    public async Task TheCharsetFacetReadsWhatWasNegotiatedAndNotWhatMsspClaimed()
    {
        // CHARSET is one of the few fields both a handshake and MSSP write, so the precedence winner
        // is the handshake's when there is one and the game's own assertion when there is not. A
        // facet labelled "we measured this" must not quietly answer from the second.
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
        // capability.ssl.declared says somebody typed SSL 4202 into their configuration. An endpoint
        // of kind tls says a socket was opened. Only the second is a measurement, and the facet
        // reads only the second — which is also why it renders nothing today: the crawler dials
        // plaintext, so nothing writes a TLS endpoint yet.
        await using var db = await PostgresFixture.MigratedAsync();
        var secure = await Seed.GameAsync(db, "secure", "Secure", lastReachableAt: Now);
        var boastful = await Seed.GameAsync(db, "boastful", "Boastful", lastReachableAt: Now);

        await new NpgsqlEndpointStore(db.DataSource).UpsertAsync(new GameEndpoint(
            secure, "secure.example", 4202, EndpointKind.Tls, Now, Now, EndpointState.Active));
        await new NpgsqlGameFieldStore(db.DataSource).UpsertAsync(new GameField(
            boastful, CapabilityFields.Declared("SSL"), FieldSource.Mssp, "true", Now, Now));

        var listing = await QueriesOn(db).SearchAsync(new GameFilter());

        await Assert.That(Group(listing, FacetKeys.Tls)!.Values.Single().Count).IsEqualTo(1);
        await Assert.That(
            (await QueriesOn(db).ListAsync(new GameFilter { Tls = true })).Select(g => g.Slug).ToList())
            .IsEquivalentTo(new[] { "secure" });
    }

    [Test]
    public async Task ACodebaseWeCouldNotIdentifyIsItsOwnBucketAndCanBeAskedFor()
    {
        // A measurement of our own reach, and one of the more useful filters in the panel. It also
        // survives the cap on open-ended facets, because it is exactly the value a popularity cut
        // would delete on a well-covered catalogue.
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
        // This is the divergence the shared search was extracted to close: the demo fixture read
        // band=archived as asking for the archive and this class read it as a filter over a listing
        // the archive had already left, so one filter had two answers.
        await using var db = await PostgresFixture.MigratedAsync();
        await Seed.GameAsync(db, "corvid", "Corvid", lastReachableAt: Now);
        await Seed.GameAsync(db, "gaslight-row", "Gaslight Row", LifecycleState.Archived);

        var archived = await QueriesOn(db).ListAsync(new GameFilter { Band = ActivityBand.Archived });

        await Assert.That(archived.Select(g => g.Slug).ToList()).IsEquivalentTo(new[] { "gaslight-row" });
    }

    [Test]
    public async Task TheLastSeenFacetCarriesTheDateItFilteredOnOntoTheRowsItReturned()
    {
        // A facet whose value cannot be read off its own results is one a reader has to take on
        // trust. Never reached stays null rather than being dated from our first sighting.
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
        // The panel's promise, kept end to end: run every advertised choice back through the real
        // query and check the listing is the size the facet said it would be.
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

    private static GameFilter Choose(string key, string token) => key switch
    {
        FacetKeys.Band => new GameFilter { Band = Band(token) },
        FacetKeys.LastSeen => new GameFilter { LastSeen = Seen(token) },
        FacetKeys.Protocol => new GameFilter { MeasuredProtocols = [token] },
        FacetKeys.Tls => new GameFilter { Tls = true },
        FacetKeys.Charset => new GameFilter { Charset = FacetChoice.Parse(token) },
        FacetKeys.Codebase => new GameFilter { Codebase = FacetChoice.Parse(token) },
        FacetKeys.Family => new GameFilter { Family = FacetChoice.Parse(token) },
        FacetKeys.Genre => new GameFilter { Genre = FacetChoice.Parse(token) },
        _ => new GameFilter { Language = FacetChoice.Parse(token) },
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
