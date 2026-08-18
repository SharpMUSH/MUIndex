using Microsoft.Extensions.Configuration;

using MUI.Crawler;
using MUI.Web.Data;

namespace MUI.Web.Tests.Data;

/// <summary>
/// The Intermud-3 settings, which are refused rather than shrugged at.
/// </summary>
/// <remarks>Turning this on registers a name on a public network permanently, so a deployment that believes it configured the pass and didn't is the failure worth catching.</remarks>
public class I3SettingsTests
{
    private static CrawlerOptionsBuilder Apply(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

        return new CrawlerOptionsBuilder().Apply(configuration);
    }

    [Test]
    public async Task TheI3PassIsOffWhenNothingSaysOtherwise()
    {
        await Assert.That(Apply().I3.Enabled).IsFalse();
    }

    [Test]
    public async Task TheGatewayAddressIsParsedIntoAHostAndAPort()
    {
        var builder = Apply(
            (CrawlerSettings.I3EnabledConfigurationKey, "true"),
            (CrawlerSettings.I3GatewayConfigurationKey, "i3:8081"),
            (CrawlerSettings.I3ApiKeyConfigurationKey, "a-key"));

        await Assert.That(builder.I3.Enabled).IsTrue();
        await Assert.That(builder.I3.Gateway.Host).IsEqualTo("i3");
        await Assert.That(builder.I3.Gateway.Port).IsEqualTo(8081);
        await Assert.That(builder.I3.Gateway.ApiKey).IsEqualTo("a-key");
    }

    [Test]
    [Arguments("i3")]
    [Arguments("i3:notaport")]
    [Arguments(":8081")]
    public async Task AGatewayAddressThatIsNotHostPortIsRefused(string address)
    {
        await Assert.That(() => Apply((CrawlerSettings.I3GatewayConfigurationKey, address)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task AValueThatIsNeitherTrueNorFalseIsRefused()
    {
        await Assert.That(() => Apply((CrawlerSettings.I3EnabledConfigurationKey, "yes")))
            .Throws<ArgumentException>();
    }

    /// <summary>Enabled with no key fails at startup rather than as a recurring auth failure that reads like a broken sidecar.</summary>
    [Test]
    public async Task ThePassCannotBeEnabledWithoutAKey()
    {
        await Assert.That(() => Apply((CrawlerSettings.I3EnabledConfigurationKey, "true")))
            .Throws<InvalidOperationException>();
    }
}
