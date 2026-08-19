using System.Net;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using MUI.Catalog;
using MUI.Web.Api;

namespace MUI.Web.Tests.Api;

/// <summary>
/// §10's time series: presence over time, and reachability over time.
/// </summary>
/// <remarks>§5.4's three states are easy for a JSON array to flatten into one, so the assertions here are mostly about what is <em>absent</em> and what is <em>null</em>.</remarks>
public class SeriesApiTests
{
    private static readonly Guid Game = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    /// <summary>
    /// A bucket nobody measured is absent, and an uncountable one is present with a null count.
    /// </summary>
    /// <remarks>Why the rollup keeps two tallies: a zero would say the hour was empty, dropping it would say we never looked — §5.4's middle state erased either way.</remarks>
    [Test]
    public async Task AnUncountableBucketIsNullAndAnUnmeasuredOneIsAbsent()
    {
        var at = ApiHost.Now.AddDays(-2);
        await using var host = await ApiHost.StartAsync(services => services.AddSingleton<IPresenceSeries>(
            new StubSeries([
                Bucket(at, counted: 3, uncountable: 0, min: 4, max: 12, mean: 8m),
                Bucket(at.AddDays(-1), counted: 0, uncountable: 2, min: null, max: null, mean: null),
            ])));

        var body = await host.Client.GetStringAsync($"{ApiRoutes.Games}/m-u-s-h/presence");
        var buckets = JsonDocument.Parse(body).RootElement.GetProperty("buckets");

        await Assert.That(buckets.GetArrayLength()).IsEqualTo(2);

        var counted = buckets[0];
        await Assert.That(counted.GetProperty("countedSamples").GetInt32()).IsEqualTo(3);
        await Assert.That(counted.GetProperty("mean").GetDecimal()).IsEqualTo(8m);

        var uncountable = buckets[1];
        await Assert.That(uncountable.GetProperty("countedSamples").GetInt32()).IsEqualTo(0);
        await Assert.That(uncountable.GetProperty("uncountableSamples").GetInt32()).IsEqualTo(2);
        await Assert.That(uncountable.GetProperty("mean").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(uncountable.GetProperty("min").ValueKind).IsEqualTo(JsonValueKind.Null);

        await Assert.That(body).Contains("absent rather than zero");
    }

    /// <summary>The grain is asked for by name, and an unknown one is refused rather than guessed.</summary>
    [Test]
    public async Task TheGrainIsDayUnlessHourIsAskedForAndNonsenseIsRefused()
    {
        var seen = new List<PresenceGrain>();
        await using var host = await ApiHost.StartAsync(services => services.AddSingleton<IPresenceSeries>(
            new StubSeries([], seen.Add)));

        await host.Client.GetAsync($"{ApiRoutes.Games}/m-u-s-h/presence");
        await host.Client.GetAsync($"{ApiRoutes.Games}/m-u-s-h/presence?grain=hour");
        var bad = await host.Client.GetAsync($"{ApiRoutes.Games}/m-u-s-h/presence?grain=fortnight");

        await Assert.That(seen).IsEquivalentTo(new[] { PresenceGrain.Day, PresenceGrain.Hour });
        await Assert.That(bad.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(await bad.Content.ReadAsStringAsync()).Contains("not a grain");
    }

    /// <summary>
    /// A window too wide to serve is refused, and never quietly narrowed.
    /// </summary>
    /// <remarks>A silent clamp answers a different question and says so nowhere — a consumer paging through history would take short pages for the end of the record.</remarks>
    [Test]
    public async Task AWindowWiderThanWeServeIsRefusedRatherThanTruncated()
    {
        await using var host = await ApiHost.StartAsync(services => services.AddSingleton<IPresenceSeries>(
            new StubSeries([])));

        var from = ApiHost.Now.AddYears(-2).ToString("O");
        var response = await host.Client.GetAsync(
            $"{ApiRoutes.Games}/m-u-s-h/presence?grain=hour&from={Uri.EscapeDataString(from)}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains("narrower window");
    }

    [Test]
    public async Task AWindowThatRunsBackwardsIsRefused()
    {
        await using var host = await ApiHost.StartAsync(services => services.AddSingleton<IPresenceSeries>(
            new StubSeries([])));

        var from = Uri.EscapeDataString(ApiHost.Now.ToString("O"));
        var to = Uri.EscapeDataString(ApiHost.Now.AddDays(-5).ToString("O"));
        var response = await host.Client.GetAsync(
            $"{ApiRoutes.Games}/m-u-s-h/presence?from={from}&to={to}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains("from is after to");
    }

    /// <summary>
    /// A former slug redirects here too, keeping the sub-route and the question that was asked.
    /// </summary>
    /// <remarks>A redirect that dropped the querystring would silently send a scheduled job asking for hourly data to a day-grained answer.</remarks>
    [Test]
    public async Task AFormerSlugRedirectsAndKeepsTheRouteAndTheQuery()
    {
        await using var host = await ApiHost.StartAsync(new Dictionary<string, string?>
        {
            ["SlugAliases:tidewater-nights"] = "m-u-s-h",
        });

        var response = await host.Client.GetAsync(
            $"{ApiRoutes.Games}/tidewater-nights/presence?grain=hour");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
        await Assert.That(response.Headers.Location!.ToString())
            .IsEqualTo($"{ApiRoutes.Games}/m-u-s-h/presence?grain=hour");
    }

    [Test]
    public async Task AGameNobodyHasIsAProblemDocumentRatherThanAnEmptySeries()
    {
        await using var host = await ApiHost.StartAsync();

        var response = await host.Client.GetAsync($"{ApiRoutes.Games}/nobody/presence");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The span running when the window opened is in the answer, though it began before it.
    /// </summary>
    /// <remarks>Overlap, not containment — a containment filter would answer "no spans" for exactly the long-dark game whose availability is most worth knowing.</remarks>
    [Test]
    public async Task ASpanThatStartedBeforeTheWindowIsStillInIt()
    {
        await using var host = await ApiHost.StartAsync();

        var body = await host.Client.GetStringAsync(
            $"{ApiRoutes.Games}/gaslight-row/availability?grain=day");
        var root = JsonDocument.Parse(body).RootElement;
        var spans = root.GetProperty("spans");

        await Assert.That(spans.GetArrayLength()).IsGreaterThan(0);
        await Assert.That(spans[0].GetProperty("fromAt").GetDateTimeOffset())
            .IsLessThan(root.GetProperty("from").GetDateTimeOffset());
        await Assert.That(body).Contains("one vantage point");
    }

    /// <summary>Both series are on the route table, so a consumer finds them without reading us.</summary>
    [Test]
    public async Task TheIndexNamesBothSeries()
    {
        await using var host = await ApiHost.StartAsync();

        var body = await host.Client.GetStringAsync(ApiRoutes.Base);

        await Assert.That(body).Contains("/presence");
        await Assert.That(body).Contains("/availability");
    }

    private static PresenceRollup Bucket(
        DateTimeOffset at, int counted, int uncountable, int? min, int? max, decimal? mean) => new()
        {
            GameId = Game,
            Grain = PresenceGrain.Day,
            Bucket = at,
            CountedSamples = counted,
            UnmeasurableSamples = uncountable,
            MinCount = min,
            MaxCount = max,
            MeanCount = mean,
        };

    /// <summary>
    /// A series that answers with what it was given, and records what it was asked.
    /// </summary>
    /// <remarks>The rollup's own SQL is tested against real PostgreSQL in the catalogue suite; this covers the route above it.</remarks>
    private sealed class StubSeries(
        IReadOnlyList<PresenceRollup> buckets,
        Action<PresenceGrain>? asked = null) : IPresenceSeries
    {
        public Task<IReadOnlyList<PresenceRollup>> ForGameAsync(
            Guid gameId,
            PresenceGrain grain,
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken = default)
        {
            asked?.Invoke(grain);
            return Task.FromResult(buckets);
        }
    }
}
