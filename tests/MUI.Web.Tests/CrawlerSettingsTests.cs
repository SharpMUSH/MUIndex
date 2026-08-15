using Microsoft.Extensions.Configuration;

using MUI.Crawler;
using MUI.Web.Data;

namespace MUI.Web.Tests;

/// <summary>
/// The two knobs a deployment turns on the in-process crawler, and the one it deliberately cannot.
/// </summary>
/// <remarks>
/// <para>
/// The load-bearing test is <see cref="AConfiguredSeedIsNeverExemptFromTheResolvedAddressGate"/>.
/// Everything else here is convenience; that one is §7.2's rule that the exemption is a claim a
/// person makes about an address they chose, which an environment variable copied between
/// deployments cannot make on their behalf.
/// </para>
/// <para>
/// <b>Every test here runs with both variables cleared, and puts them back.</b> The thing under test
/// reads the environment <em>before</em> configuration, so a developer who still has
/// <c>MUI_CRAWL_SEEDS</c> exported from a compose session would otherwise be running a different
/// suite from CI — the settings would come from their shell and the assertions would be about
/// nothing. Clearing process-wide state is why the class is <c>[NotInParallel]</c>.
/// </para>
/// </remarks>
[NotInParallel]
public class CrawlerSettingsTests
{
    private string? _seeds;
    private string? _enabled;

    [Before(HookType.Test)]
    public void ClearTheAmbientEnvironment()
    {
        _seeds = Environment.GetEnvironmentVariable(CrawlerSettings.SeedsEnvironmentVariable);
        _enabled = Environment.GetEnvironmentVariable(CrawlerSettings.EnabledEnvironmentVariable);

        Environment.SetEnvironmentVariable(CrawlerSettings.SeedsEnvironmentVariable, null);
        Environment.SetEnvironmentVariable(CrawlerSettings.EnabledEnvironmentVariable, null);
    }

    [After(HookType.Test)]
    public void PutItBack()
    {
        Environment.SetEnvironmentVariable(CrawlerSettings.SeedsEnvironmentVariable, _seeds);
        Environment.SetEnvironmentVariable(CrawlerSettings.EnabledEnvironmentVariable, _enabled);
    }

    private static IConfiguration Config(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => KeyValuePair.Create(s.Key, (string?)s.Value)))
            .Build();

    [Test]
    public async Task NoSeedsConfiguredIsNoSeeds()
    {
        await Assert.That(CrawlerSettings.Seeds(Config())).IsEmpty();
    }

    [Test]
    public async Task ASeedListIsReadInTheOrderItWasWritten()
    {
        var seeds = CrawlerSettings.Seeds(Config(
            (CrawlerSettings.SeedsConfigurationKey, "mush.pennmush.org:4201, aardmud.org:4000")));

        // Joined rather than compared as collections: TUnit's IsEquivalentTo ignores order, so it
        // would have passed on a list this test exists to say is in order.
        await Assert.That(string.Join(" ", seeds.Select(s => s.ToString())))
            .IsEqualTo("mush.pennmush.org:4201 aardmud.org:4000");
    }

    [Test]
    public async Task TheEnvironmentIsReadBeforeConfiguration()
    {
        // Documented in docs/deploy.md and true of both variables, so it is asserted rather than
        // described: a deployment that sets MUI_CRAWL_SEEDS and also ships an appsettings entry has
        // to know which one wins.
        Environment.SetEnvironmentVariable(
            CrawlerSettings.SeedsEnvironmentVariable, "from.environment:4201");
        Environment.SetEnvironmentVariable(CrawlerSettings.EnabledEnvironmentVariable, "false");

        var configuration = Config(
            (CrawlerSettings.SeedsConfigurationKey, "from.configuration:4000"),
            (CrawlerSettings.EnabledConfigurationKey, "true"));

        await Assert.That(CrawlerSettings.Seeds(configuration).Single().Host)
            .IsEqualTo("from.environment");
        await Assert.That(new CrawlerOptionsBuilder().Apply(configuration).Enabled).IsFalse();
    }

    [Test]
    public async Task AnEmptyEnvironmentVariableFallsThroughToConfiguration()
    {
        // Compose writes MUI_CRAWL_SEEDS: ${MUI_CRAWL_SEEDS:-} into the container, so the empty
        // string is the normal state of an unset seed list rather than an exotic one.
        Environment.SetEnvironmentVariable(CrawlerSettings.SeedsEnvironmentVariable, string.Empty);

        var seeds = CrawlerSettings.Seeds(Config(
            (CrawlerSettings.SeedsConfigurationKey, "from.configuration:4000")));

        await Assert.That(seeds.Single().Host).IsEqualTo("from.configuration");
    }

    [Test]
    public async Task WhitespaceSeparatesAsWellAsCommas()
    {
        // A seed list arrives from a compose file, a shell export or a Kubernetes manifest, and each
        // of those has its own idea of how a list is written.
        var seeds = CrawlerSettings.Seeds(Config(
            (CrawlerSettings.SeedsConfigurationKey, "a.example:4201\n b.example:4000\tc.example:23")));

        await Assert.That(seeds).Count().IsEqualTo(3);
    }

    [Test]
    public async Task ABracketedIpv6AddressKeepsItsPort()
    {
        var seed = CrawlerSettings.Seeds(Config(
            (CrawlerSettings.SeedsConfigurationKey, "[2001:db8::1]:4201"))).Single();

        await Assert.That(seed.Host).IsEqualTo("2001:db8::1");
        await Assert.That(seed.Port).IsEqualTo(4201);
    }

    [Test]
    public async Task AConfiguredSeedIsNeverExemptFromTheResolvedAddressGate()
    {
        var seeds = CrawlerSettings.Seeds(Config(
            (CrawlerSettings.SeedsConfigurationKey, "127.0.0.1:4201 localhost:4000")));

        await Assert.That(seeds.All(s => !s.IsOperatorSeed)).IsTrue();
    }

    [Test]
    public async Task AnAddressWithoutAPortIsRefusedRatherThanSkipped()
    {
        // Skipping it would be a crawl that quietly never dialled what it was pointed at.
        await Assert.That(() => CrawlerSettings.Seeds(Config(
            (CrawlerSettings.SeedsConfigurationKey, "mush.pennmush.org")))).Throws<ArgumentException>();
    }

    [Test]
    public async Task TheCrawlerIsOnUnlessTheDeploymentSaysOtherwise()
    {
        await Assert.That(new CrawlerOptionsBuilder().Apply(Config()).Enabled).IsTrue();

        await Assert.That(new CrawlerOptionsBuilder()
            .Apply(Config((CrawlerSettings.EnabledConfigurationKey, "false")))
            .Enabled).IsFalse();
    }

    [Test]
    public async Task AValueThatIsNeitherTrueNorFalseIsAnError()
    {
        // "no" read as "not the word false, so leave it on" is a deployment that believes it turned
        // the crawler off and is still dialling.
        await Assert.That(() => new CrawlerOptionsBuilder()
            .Apply(Config((CrawlerSettings.EnabledConfigurationKey, "no")))).Throws<ArgumentException>();
    }

    [Test]
    public async Task WhatWasConfiguredIsWhatTheCrawlerIsBuiltWith()
    {
        var options = new CrawlerOptionsBuilder()
            .Apply(Config(
                (CrawlerSettings.EnabledConfigurationKey, "true"),
                (CrawlerSettings.SeedsConfigurationKey, "mush.pennmush.org:4201")))
            .Build();

        options.Validate();

        await Assert.That(options.Enabled).IsTrue();
        await Assert.That(options.Seeds.Single()).IsEqualTo(new CrawlSeed("mush.pennmush.org", 4201));
    }
}
