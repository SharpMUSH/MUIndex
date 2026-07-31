using MUI.Crawl;
using TelnetNegotiationCore.Models;

namespace MUI.Crawl.Tests;

/// <summary>
/// An MSSP report is a name → value-<b>list</b> map, and the probe records all of it.
/// </summary>
/// <remarks>
/// Two separate losses used to happen here and both were ours rather than the library's.
/// TelnetNegotiationCore models multi-valued variables properly (upstream PR #56 made
/// <c>MSSPConfig.Variables</c> the lossless record its typed properties are projected from); the
/// probe flattened that into one string per name, and then kept only seven names.
/// </remarks>
public class MsspReportTests
{
    private static ProbeResult WithReport(IReadOnlyDictionary<string, IReadOnlyList<string>> report) => new()
    {
        Host = "corvid.example",
        Port = 4201,
        ObservedAt = DateTimeOffset.UnixEpoch,
        Outcome = ProbeOutcome.Answered,
        MsspOutcome = MsspOutcome.Received,
        MsspTransport = MsspTransport.TelnetOption70,
        Mssp = report,
    };

    [Test]
    public async Task EveryValueOfARepeatedVariableSurvivesTheProjection()
    {
        // alteraeon.com:23 really does report PORT four times (23, 3000, 3010, 3224), measured.
        // REFERRAL is the same shape and is the entire basis of crawl discovery, so losing all but
        // one value there loses the graph.
        var config = new MSSPConfig();
        config.Variables.Add("PORT", "23");
        config.Variables.Add("PORT", "3000");
        config.Variables.Add("PORT", "3010");
        config.Variables.Add("PORT", "3224");

        var report = MsspReport.From(config);

        await Assert.That(report["PORT"].Count).IsEqualTo(4);
        await Assert.That(report["PORT"]).IsEquivalentTo(new[] { "23", "3000", "3010", "3224" });
    }

    [Test]
    public async Task NoValueIsEverJoinedIntoOneString()
    {
        // Joining was worse than dropping. An MSSP value may legitimately contain a comma —
        // alteraeon.com reports GAMEPLAY as "Social", "Hack and Slash", "Adventure" — so a joined
        // string manufactures something that looks like a value and cannot be split back apart.
        // That is a fabrication, and the rule against those does not stop at player counts.
        var config = new MSSPConfig();
        config.Variables.Add("REFERRAL", "first.example 4201, still the first one");
        config.Variables.Add("REFERRAL", "second.example 4202");

        var report = MsspReport.From(config);

        await Assert.That(report["REFERRAL"].Count).IsEqualTo(2);
        await Assert.That(report["REFERRAL"][0]).IsEqualTo("first.example 4201, still the first one");
        await Assert.That(report["REFERRAL"][1]).IsEqualTo("second.example 4202");
    }

    [Test]
    public async Task NothingTheServerSentIsFilteredOut()
    {
        // The projection used to hand-pick NAME, PLAYERS, UPTIME, CODEBASE, CONTACT, CRAWL DELAY and
        // CHARSET, and discard the rest — REFERRAL, WEBSITE, HOSTNAME, PORT, SSL, ICON, LANGUAGE and
        // every name a codebase invented. A crawler whose premise is faithful measurement does not
        // get to decide which of a server's own answers were worth keeping.
        var config = new MSSPConfig();
        config.Variables.Add("NAME", "The Game");
        config.Variables.Add("REFERRAL", "elsewhere.example 4201");
        config.Variables.Add("WEBSITE", "https://example.invalid");
        config.Variables.Add("SSL", "4202");
        config.Variables.Add("XTERM TRUE COLORS", "1");
        config.Variables.Add("SOME NAME NOBODY STANDARDISED", "yes");

        var report = MsspReport.From(config);

        foreach (var name in new[]
                 {
                     "NAME", "REFERRAL", "WEBSITE", "SSL", "XTERM TRUE COLORS",
                     "SOME NAME NOBODY STANDARDISED",
                 })
        {
            await Assert.That(report.ContainsKey(name)).IsTrue();
        }
    }

    [Test]
    public async Task WireOrderIsPreserved()
    {
        var config = new MSSPConfig();
        config.Variables.Add("REFERRAL", "first.example 4201");
        config.Variables.Add("REFERRAL", "second.example 4202");
        config.Variables.Add("NAME", "The Game");

        var report = MsspReport.From(config);

        await Assert.That(report.Keys.First()).IsEqualTo("REFERRAL");
        await Assert.That(report["REFERRAL"][0]).IsEqualTo("first.example 4201");
    }

    [Test]
    public async Task TheScalarAccessorTakesTheLastValueTheSpecificationsWay()
    {
        // "The same variable can be sent more than once with different values, in which case the
        // last reported value should be used as the default value."
        var result = WithReport(MsspReport.From(Config(("PLAYERS", "3"), ("PLAYERS", "7"))));

        await Assert.That(result.MsspField("PLAYERS")).IsEqualTo("7");
        await Assert.That(result.MsspValues("PLAYERS").Count).IsEqualTo(2);
    }

    [Test]
    public async Task TheScalarAccessorIsNullRatherThanEmptyForAVariableNobodySent()
    {
        var result = WithReport(MsspReport.From(Config(("NAME", "The Game"))));

        await Assert.That(result.MsspField("REFERRAL")).IsNull();
        await Assert.That(result.MsspValues("REFERRAL")).IsEmpty();
    }

    [Test]
    public async Task VariableNamesAreMatchedWithoutRegardToCase()
    {
        var result = WithReport(MsspReport.From(Config(("NAME", "The Game"))));

        await Assert.That(result.MsspField("name")).IsEqualTo("The Game");
    }

    [Test]
    public async Task ANullConfigIsAnEmptyReportRatherThanAThrow()
    {
        await Assert.That(MsspReport.From(null)).IsEmpty();
    }

    [Test]
    public async Task AProbeThatGotNoReportCarriesAnEmptyOne()
    {
        var result = new ProbeResult
        {
            Host = "corvid.example",
            Port = 4201,
            ObservedAt = DateTimeOffset.UnixEpoch,
            Outcome = ProbeOutcome.Answered,
        };

        await Assert.That(result.Mssp).IsEmpty();
        await Assert.That(result.MsspField("NAME")).IsNull();
    }

    private static MSSPConfig Config(params (string Name, string Value)[] pairs)
    {
        var config = new MSSPConfig();
        foreach (var (name, value) in pairs)
        {
            config.Variables.Add(name, value);
        }

        return config;
    }
}
