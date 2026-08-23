using Microsoft.Extensions.Configuration;

using MUI.Crawler;
using MUI.Web.Data;

namespace MUI.Web.Tests.Data;

/// <summary>
/// The AresCentral settings, whose default is the interesting part.
/// </summary>
/// <remarks>
/// The options record says the pass is on, because once a deployment holds credentials that is the
/// intended state. Silence is not consent to run a pass that can only fail, though, so an
/// unconfigured deployment is switched off here — the case that has to keep working is every
/// existing deployment, which has never heard of AresCentral and must come up exactly as before.
/// </remarks>
public class AresSettingsTests
{
    private static CrawlerOptionsBuilder Apply(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

        return new CrawlerOptionsBuilder().Apply(configuration);
    }

    /// <summary>
    /// The one that protects every deployment that already exists: no credentials, no pass, no
    /// warning on a loop, no change.
    /// </summary>
    [Test]
    public async Task ThePassIsOffWhenNoCredentialsAreConfigured()
    {
        await Assert.That(Apply().Ares.Enabled).IsFalse();
    }

    [Test]
    public async Task ThePassIsOnWhenBothHalvesOfTheCredentialArePresent()
    {
        var options = Apply(
            (CrawlerSettings.AresClientIdConfigurationKey, "muindex"),
            (CrawlerSettings.AresApiKeyConfigurationKey, "not-a-real-key"));

        await Assert.That(options.Ares.Enabled).IsTrue();
        await Assert.That(options.Ares.Hub.ClientId).IsEqualTo("muindex");
        await Assert.That(options.Ares.Hub.ApiKey).IsEqualTo("not-a-real-key");
    }

    /// <summary>
    /// Half a pair is a typo in a compose file. Refused out loud rather than quietly switched off,
    /// which would leave somebody who configured half of it wondering why nothing happens.
    /// </summary>
    [Test]
    public async Task HalfACredentialIsRefusedRatherThanQuietlyIgnored()
    {
        await Assert.That(() => Apply((CrawlerSettings.AresClientIdConfigurationKey, "muindex")))
            .Throws<InvalidOperationException>();
    }

    /// <summary>Somebody asking for the pass and getting the compose file wrong is told so.</summary>
    [Test]
    public async Task ThePassCannotBeEnabledWithoutCredentials()
    {
        await Assert.That(() => Apply((CrawlerSettings.AresEnabledConfigurationKey, "true")))
            .Throws<InvalidOperationException>();
    }

    /// <summary>
    /// A deployment that holds credentials may still say no, and saying no must not then be
    /// overridden by the credentials it holds.
    /// </summary>
    [Test]
    public async Task ADeploymentWithCredentialsCanStillTurnThePassOff()
    {
        var options = Apply(
            (CrawlerSettings.AresEnabledConfigurationKey, "false"),
            (CrawlerSettings.AresClientIdConfigurationKey, "muindex"),
            (CrawlerSettings.AresApiKeyConfigurationKey, "not-a-real-key"));

        await Assert.That(options.Ares.Enabled).IsFalse();
    }

    [Test]
    public async Task AValueThatIsNeitherTrueNorFalseIsRefused()
    {
        await Assert.That(() => Apply((CrawlerSettings.AresEnabledConfigurationKey, "yes")))
            .Throws<ArgumentException>();
    }

    /// <summary>
    /// A half pair is a configuration error whether or not the pass is switched on.
    /// </summary>
    /// <remarks>
    /// Turning the pass off does not make a broken credential correct — it postpones finding out.
    /// The operator who later flips it back on is the one who pays, and by then the mistake is old
    /// enough that nobody connects the two.
    /// </remarks>
    [Test]
    public async Task HalfACredentialIsRefusedEvenWithThePassSwitchedOff()
    {
        await Assert.That(() => Apply(
                (CrawlerSettings.AresEnabledConfigurationKey, "false"),
                (CrawlerSettings.AresClientIdConfigurationKey, "muindex")))
            .Throws<InvalidOperationException>();
    }

    /// <summary>Off with neither half present is the ordinary untouched deployment, and is fine.</summary>
    [Test]
    public async Task OffWithNoCredentialsAtAllIsFine()
    {
        var options = Apply((CrawlerSettings.AresEnabledConfigurationKey, "false"));

        await Assert.That(options.Ares.Enabled).IsFalse();
    }
}
