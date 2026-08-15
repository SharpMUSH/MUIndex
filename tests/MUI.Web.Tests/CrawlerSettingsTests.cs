using Microsoft.Extensions.Configuration;

using MUI.Crawler;
using MUI.Web.Data;

namespace MUI.Web.Tests;

/// <summary>
/// The two knobs a deployment turns on the in-process crawler, and the one it deliberately cannot.
/// </summary>
/// <remarks>
/// The load-bearing test is <see cref="AConfiguredSeedIsNeverExemptFromTheResolvedAddressGate"/>.
/// Everything else here is convenience; that one is §7.2's rule that the exemption is a claim a
/// person makes about an address they chose, which an environment variable copied between
/// deployments cannot make on their behalf.
/// </remarks>
public class CrawlerSettingsTests
{
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

        await Assert.That(seeds.Select(s => s.ToString()))
            .IsEquivalentTo(new[] { "mush.pennmush.org:4201", "aardmud.org:4000" });
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
