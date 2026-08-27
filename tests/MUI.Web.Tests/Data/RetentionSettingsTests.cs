using Microsoft.Extensions.Configuration;

using MUI.Catalog;
using MUI.Crawler;
using MUI.Web.Data;

namespace MUI.Web.Tests;

/// <summary>
/// How long presence is kept, said in the deployment's own configuration rather than in a patch.
/// </summary>
/// <remarks>
/// <para>
/// Retention was reachable only from C# before this: the windows are properties on
/// <see cref="PresenceRetentionOptions"/> and nothing bound them, so bounding a deployment's storage
/// meant editing <c>SiteComposition</c> and shipping an image. The pass itself has been running
/// hourly all along with nothing it was permitted to delete.
/// </para>
/// <para>
/// <b>Unset still keeps everything.</b> §15.4 is open and the catalogue's default is deliberate:
/// between an unsettled retention period and a deletion, the conservative answer is to delete
/// nothing. What is added here is the ability to say otherwise without a redeploy — not a new
/// default.
/// </para>
/// <para>
/// Every test clears the variables and puts them back, for the reason
/// <see cref="CrawlerSettingsTests"/> does: the code reads the environment before configuration, so a
/// leftover shell export would silently diverge this suite from CI.
/// </para>
/// </remarks>
[NotInParallel]
public class RetentionSettingsTests
{
    private static readonly string[] Variables =
    [
        CrawlerSettings.RetainRawDaysEnvironmentVariable,
        CrawlerSettings.RetainHourlyDaysEnvironmentVariable,
        CrawlerSettings.RetainDailyDaysEnvironmentVariable,
    ];

    private readonly Dictionary<string, string?> _saved = [];

    [Before(HookType.Test)]
    public void ClearTheAmbientEnvironment()
    {
        foreach (var variable in Variables)
        {
            _saved[variable] = Environment.GetEnvironmentVariable(variable);
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [After(HookType.Test)]
    public void PutItBack()
    {
        foreach (var (variable, value) in _saved)
        {
            Environment.SetEnvironmentVariable(variable, value);
        }
    }

    private static PresenceRetentionOptions Applied() =>
        new CrawlerOptionsBuilder()
            .Apply(new ConfigurationBuilder().Build())
            .Build()
            .Maintenance.Retention;

    [Test]
    public async Task NothingConfiguredKeepsEverything()
    {
        var retention = Applied();

        await Assert.That(retention.RawSamples).IsNull();
        await Assert.That(retention.HourlyRollups).IsNull();
        await Assert.That(retention.DailyRollups).IsNull();
    }

    [Test]
    public async Task EachGrainIsSetIndependently()
    {
        Environment.SetEnvironmentVariable(CrawlerSettings.RetainRawDaysEnvironmentVariable, "90");
        Environment.SetEnvironmentVariable(CrawlerSettings.RetainHourlyDaysEnvironmentVariable, "90");

        var retention = Applied();

        await Assert.That(retention.RawSamples).IsEqualTo(TimeSpan.FromDays(90));
        await Assert.That(retention.HourlyRollups).IsEqualTo(TimeSpan.FromDays(90));

        // Untouched, because §5.2 keeps the daily grain for ever and bounding the other two is not a
        // reason to start bounding the copy they may be dropped in favour of.
        await Assert.That(retention.DailyRollups).IsNull();
    }

    /// <summary>
    /// A typo throws rather than being read as "unset, so keep everything".
    /// </summary>
    /// <remarks>
    /// The failure this prevents is quiet and expensive: a deployment that believes it has bounded
    /// its storage, has not, and finds out when the disk does. Same reasoning as
    /// <c>MUI_CRAWL_ENABLED=no</c> not being read as "not false, so leave it on".
    /// </remarks>
    [Test]
    [Arguments("ninety")]
    [Arguments("90d")]
    [Arguments("")]
    [Arguments("-1")]
    [Arguments("0")]
    [Arguments("90.5")]
    public async Task AWindowThatIsNotAPositiveWholeNumberOfDaysThrows(string value)
    {
        Environment.SetEnvironmentVariable(CrawlerSettings.RetainHourlyDaysEnvironmentVariable, value);

        // The empty string is "unset" to the reader, not a typo — it is how a compose file writes a
        // variable it does not want to set, so it must keep everything rather than throw.
        if (value.Length == 0)
        {
            await Assert.That(Applied().HourlyRollups).IsNull();

            return;
        }

        await Assert.That(Applied).Throws<ArgumentException>();
    }

    /// <summary>
    /// Retention below the heatmap window is refused by the catalogue, not by the binding.
    /// </summary>
    /// <remarks>
    /// The floor belongs to <see cref="PresenceRetentionOptions.Validate"/> because it is a property
    /// of the data — the rollup is the only copy left once raw goes, so a window's worth of raw is
    /// what lets the grid be rebuilt after a rollup fault. Asserted here so that reaching it through
    /// configuration is known to hit the same guard reaching it through C# does.
    /// </remarks>
    [Test]
    public async Task RawRetentionUnderTheHeatmapWindowIsStillRefused()
    {
        Environment.SetEnvironmentVariable(CrawlerSettings.RetainRawDaysEnvironmentVariable, "7");

        var retention = Applied();

        await Assert.That(retention.RawSamples).IsEqualTo(TimeSpan.FromDays(7));
        await Assert.That(retention.Validate).Throws<ArgumentException>();
    }
}
