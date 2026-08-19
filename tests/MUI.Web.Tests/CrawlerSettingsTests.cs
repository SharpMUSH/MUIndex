using Microsoft.Extensions.Configuration;

using MUI.Crawl;
using MUI.Crawler;
using MUI.Web.Data;

namespace MUI.Web.Tests;

/// <summary>
/// The three knobs a deployment turns on the in-process crawler, and the one it deliberately cannot.
/// </summary>
/// <remarks>
/// The load-bearing test is <see cref="AConfiguredSeedIsNeverExemptFromTheResolvedAddressGate"/>:
/// §7.2's exemption is a claim a person makes about an address they chose, which an environment
/// variable copied between deployments can't make on their behalf.
/// <b>Every test here clears both env vars and puts them back</b> — the code under test reads the
/// environment before configuration, so a developer's leftover shell export would otherwise silently
/// diverge this suite from CI. Clearing process-wide state is why the class is
/// <c>[NotInParallel]</c>.
/// </remarks>
[NotInParallel]
public class CrawlerSettingsTests
{
    private string? _seeds;
    private string? _enabled;
    private string? _infoUrl;
    private string? _dnsClaims;

    [Before(HookType.Test)]
    public void ClearTheAmbientEnvironment()
    {
        _seeds = Environment.GetEnvironmentVariable(CrawlerSettings.SeedsEnvironmentVariable);
        _enabled = Environment.GetEnvironmentVariable(CrawlerSettings.EnabledEnvironmentVariable);
        _infoUrl = Environment.GetEnvironmentVariable(CrawlerSettings.InfoUrlEnvironmentVariable);
        _dnsClaims = Environment.GetEnvironmentVariable(CrawlerSettings.DnsClaimsEnabledEnvironmentVariable);

        Environment.SetEnvironmentVariable(CrawlerSettings.SeedsEnvironmentVariable, null);
        Environment.SetEnvironmentVariable(CrawlerSettings.EnabledEnvironmentVariable, null);
        Environment.SetEnvironmentVariable(CrawlerSettings.InfoUrlEnvironmentVariable, null);
        Environment.SetEnvironmentVariable(CrawlerSettings.DnsClaimsEnabledEnvironmentVariable, null);
    }

    [After(HookType.Test)]
    public void PutItBack()
    {
        Environment.SetEnvironmentVariable(CrawlerSettings.SeedsEnvironmentVariable, _seeds);
        Environment.SetEnvironmentVariable(CrawlerSettings.EnabledEnvironmentVariable, _enabled);
        Environment.SetEnvironmentVariable(CrawlerSettings.InfoUrlEnvironmentVariable, _infoUrl);
        Environment.SetEnvironmentVariable(CrawlerSettings.DnsClaimsEnabledEnvironmentVariable, _dnsClaims);
    }

    private static IConfiguration Config(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => KeyValuePair.Create(s.Key, (string?)s.Value)))
            .Build();

    /// <summary>
    /// §8.3's sweep is on unless a deployment says otherwise, and a typo is not "otherwise".
    /// </summary>
    /// <remarks>
    /// On by default because it dials nothing and its cost is set by how many people are mid-claim.
    /// It has an off switch all the same: it is the one thing here that makes outbound DNS queries,
    /// and a deployment with no egress should be able to stop it rather than read a warning on a
    /// loop. Refused on an unparseable value for the same reason <c>MUI_CRAWL_ENABLED</c> is —
    /// <c>=no</c> must not be read as "not false, so leave it on".
    /// </remarks>
    [Test]
    public async Task TheDnsClaimSweepIsOnUnlessADeploymentSaysOtherwise()
    {
        await Assert.That(new CrawlerOptionsBuilder().Apply(Config()).Build().DnsClaims.Enabled).IsTrue();

        var off = new CrawlerOptionsBuilder()
            .Apply(Config((CrawlerSettings.DnsClaimsEnabledConfigurationKey, "false")))
            .Build();

        await Assert.That(off.DnsClaims.Enabled).IsFalse();
    }

    [Test]
    public async Task ADnsClaimSweepSettingThatIsNeitherTrueNorFalseIsRefused()
    {
        await Assert.That(() => new CrawlerOptionsBuilder()
                .Apply(Config((CrawlerSettings.DnsClaimsEnabledConfigurationKey, "no")))
                .Build())
            .Throws<ArgumentException>();
    }

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

        // Joined rather than compared as collections: TUnit's IsEquivalentTo ignores order.
        await Assert.That(string.Join(" ", seeds.Select(s => s.ToString())))
            .IsEqualTo("mush.pennmush.org:4201 aardmud.org:4000");
    }

    [Test]
    public async Task TheEnvironmentIsReadBeforeConfiguration()
    {
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
        // Compose writes ${MUI_CRAWL_SEEDS:-} into the container, so an empty string is the normal
        // state of an unset seed list.
        Environment.SetEnvironmentVariable(CrawlerSettings.SeedsEnvironmentVariable, string.Empty);

        var seeds = CrawlerSettings.Seeds(Config(
            (CrawlerSettings.SeedsConfigurationKey, "from.configuration:4000")));

        await Assert.That(seeds.Single().Host).IsEqualTo("from.configuration");
    }

    [Test]
    public async Task WhitespaceSeparatesAsWellAsCommas()
    {
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
        // Skipping it would leave a crawl quietly never dialling what it was pointed at.
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
        // Silently treating "no" as "not false, so on" would leave a deployment believing it turned
        // the crawler off while it kept dialling.
        await Assert.That(() => new CrawlerOptionsBuilder()
            .Apply(Config((CrawlerSettings.EnabledConfigurationKey, "no")))).Throws<ArgumentException>();
    }

    [Test]
    public async Task WithNoContactAddressConfiguredTheCrawlerHoldsThePlaceholder()
    {
        // /about renders a placeholder notice off exactly this comparison.
        await Assert.That(new CrawlerOptionsBuilder().Apply(Config()).Probe.InfoUrl)
            .IsEqualTo(new ProbeOptions().InfoUrl);
    }

    [Test]
    public async Task TheContactAddressIsWhatThisDeploymentSaysItIs()
    {
        var probe = new CrawlerOptionsBuilder()
            .Apply(Config((CrawlerSettings.InfoUrlConfigurationKey, "https://mu-index.com/crawler")))
            .Probe;

        await Assert.That(probe.InfoUrl).IsEqualTo("https://mu-index.com/crawler");

        Environment.SetEnvironmentVariable(
            CrawlerSettings.InfoUrlEnvironmentVariable, "https://from.environment/crawler");

        await Assert.That(new CrawlerOptionsBuilder()
            .Apply(Config((CrawlerSettings.InfoUrlConfigurationKey, "https://from.configuration/crawler")))
            .Probe.InfoUrl).IsEqualTo("https://from.environment/crawler");
    }

    [Test]
    public async Task AContactAddressNobodyCouldOpenIsRefusedAtTheSettingRatherThanAnnounced()
    {
        // Otherwise the site starts, the crawl runs, and every server dialled is told to write to
        // something that isn't an address.
        await Assert.That(() => new CrawlerOptionsBuilder()
                .Apply(Config((CrawlerSettings.InfoUrlConfigurationKey, "mu-index.com/crawler"))))
            .Throws<ArgumentException>();
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
