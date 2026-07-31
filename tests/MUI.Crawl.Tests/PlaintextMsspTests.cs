using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// Layer 4's second route: the plaintext <c>MSSP-REQUEST</c> reply (spec §6.4).
/// </summary>
/// <remarks>
/// Every transcript below was captured from the named server on 30 July 2026, including the ones
/// that are not replies at all. Roughly 69 of the surveyed repositories implement the server side
/// and three of the twenty live games tried answered — while eight read the request as a character
/// name and said so, which is the measurement behind the feature being off by default.
/// </remarks>
public class PlaintextMsspTests
{
    // 162.243.50.82:4000 — Riftforge, "Mortals and Monsters". Captured whole.
    private static readonly string[] Riftforge =
    [
        "MSSP-REPLY-START",
        "NAME\tMortals and Monsters",
        "PLAYERS\t3",
        "UPTIME\t1785462274",
        "PORT\t4000",
        "CODEBASE\tRiftforge",
        "GENRE\tHorror",
        "CRAWL DELAY\t-1",
        "MSSP-REPLY-END",
    ];

    // coffeemud.net:2327 — CoffeeMUD, which listens on nine ports and says so nine times.
    private static readonly string[] CoffeeMud =
    [
        "MSSP-REPLY-START",
        "PORT\t2330",
        "PORT\t2329",
        "PORT\t2326",
        "PORT\t2328",
        "PORT\t2327",
        "PORT\t2325",
        "PORT\t2324",
        "PORT\t2323",
        "PORT\t23",
        "GAMESYSTEM\tTick Based",
        "MSSP-REPLY-END",
    ];

    [Test]
    public async Task ARealReplyIsRead()
    {
        var report = PlaintextMssp.Parse(Riftforge);

        await Assert.That(report).IsNotNull();
        await Assert.That(report!["NAME"][0]).IsEqualTo("Mortals and Monsters");
        await Assert.That(report["PLAYERS"][0]).IsEqualTo("3");
        await Assert.That(report["CODEBASE"][0]).IsEqualTo("Riftforge");
    }

    [Test]
    public async Task EveryValueOfARepeatedVariableSurvives()
    {
        // The bug a flat string map hides. CoffeeMUD really does listen on nine ports and really
        // does report PORT nine times; a dictionary of strings keeps one of them, and joining them
        // into "2330, 2329, …" manufactures a value that no longer splits apart reliably, because
        // an MSSP value may legitimately contain a comma.
        var report = PlaintextMssp.Parse(CoffeeMud);

        await Assert.That(report).IsNotNull();
        await Assert.That(report!["PORT"].Count).IsEqualTo(9);
        await Assert.That(report["PORT"][0]).IsEqualTo("2330");
        await Assert.That(report["PORT"][8]).IsEqualTo("23");
    }

    [Test]
    public async Task WireOrderIsPreserved()
    {
        // MSSP has no sorted form, and for a variable a game repeats the sequence is the game
        // listing them rather than naming a set. REFERRAL is the one this project depends on.
        var report = PlaintextMssp.Parse(
        [
            "MSSP-REPLY-START",
            "REFERRAL\tfirst.example 4201",
            "REFERRAL\tsecond.example 4202",
            "REFERRAL\tthird.example 4203",
            "MSSP-REPLY-END",
        ]);

        await Assert.That(report!["REFERRAL"]).IsEquivalentTo(
            new[] { "first.example 4201", "second.example 4202", "third.example 4203" });
    }

    [Test]
    public async Task AServerThatReadTheRequestAsANameIsNotAReply()
    {
        // Measured, in these exact words, on the servers named. None of this is an MSSP report and
        // reading any of it as one would invent a game's self-description out of a login error.
        string[][] refusals =
        [
            ["Illegal name, try another."],                                  // realms.reichel.net:4000
            ["Illegal name, try another."],                                  // tsosmud.org:7070
            ["Invalid name, please try another."],                           // mud.virtustan.net:8888
            ["'MSSP-REQUEST' does not exist."],                              // eternitymud.com:23
            ["Mssp-request is not a valid name choice for sundering shadows."],
            ["Invalid account name, please try another."],                   // luminarimud.com:4100
        ];

        foreach (var refusal in refusals)
        {
            await Assert.That(PlaintextMssp.Parse(refusal)).IsNull();
        }
    }

    [Test]
    public async Task SilenceIsNotAReply()
    {
        await Assert.That(PlaintextMssp.Parse(null)).IsNull();
        await Assert.That(PlaintextMssp.Parse([])).IsNull();
        await Assert.That(PlaintextMssp.Parse(["", "   ", "\t"])).IsNull();
    }

    [Test]
    public async Task AnOpenedReplyWithNothingInItIsTheServersAnswerAndNotAnAbsence()
    {
        // The plaintext twin of an empty option-70 report: the server was asked, opened a reply and
        // had nothing to put in it. Empty and null are different answers, exactly as with layer 4's
        // three outcomes.
        var report = PlaintextMssp.Parse(["MSSP-REPLY-START", "MSSP-REPLY-END"]);

        await Assert.That(report).IsNotNull();
        await Assert.That(report!.Count).IsEqualTo(0);
    }

    [Test]
    public async Task LinesBeforeTheStartMarkerAreNotFields()
    {
        // The request goes out at a login screen, so the reply may be preceded by anything at all —
        // a prompt, a leftover banner line, the echo of what we typed.
        var report = PlaintextMssp.Parse(
        [
            "Welcome to the game!",
            "NAME\tnot a field yet",
            "MSSP-REPLY-START",
            "NAME\tthe real one",
            "MSSP-REPLY-END",
        ]);

        await Assert.That(report!["NAME"].Count).IsEqualTo(1);
        await Assert.That(report["NAME"][0]).IsEqualTo("the real one");
    }

    [Test]
    public async Task LinesAfterTheEndMarkerAreNotFields()
    {
        var report = PlaintextMssp.Parse(
        [
            "MSSP-REPLY-START",
            "NAME\tThe Game",
            "MSSP-REPLY-END",
            "PLAYERS\t9999",
        ]);

        await Assert.That(report!.ContainsKey("PLAYERS")).IsFalse();
    }

    [Test]
    public async Task AReplyWhoseEndMarkerNeverArrivedIsStillRead()
    {
        // The end marker is how a server says it has finished, but the probe stops reading on a
        // quiet period of its own. Discarding a good report because our own timing cut it short
        // would be recording a decision of ours as a measurement of theirs.
        var report = PlaintextMssp.Parse(["MSSP-REPLY-START", "NAME\tThe Game", "PLAYERS\t4"]);

        await Assert.That(report).IsNotNull();
        await Assert.That(report!["NAME"][0]).IsEqualTo("The Game");
        await Assert.That(report["PLAYERS"][0]).IsEqualTo("4");
    }

    [Test]
    public async Task OnlyTheFirstTabSeparates()
    {
        // A value may contain tabs; splitting on all of them truncates it silently.
        var report = PlaintextMssp.Parse(["MSSP-REPLY-START", "NAME\tThe\tGame\tHouse", "MSSP-REPLY-END"]);

        await Assert.That(report!["NAME"][0]).IsEqualTo("The\tGame\tHouse");
    }

    [Test]
    public async Task ALineWithNoTabIsNotAField()
    {
        var report = PlaintextMssp.Parse(["MSSP-REPLY-START", "this is just prose", "NAME\tThe Game", "MSSP-REPLY-END"]);

        await Assert.That(report!.Count).IsEqualTo(1);
        await Assert.That(report.ContainsKey("NAME")).IsTrue();
    }

    [Test]
    public async Task AHostileServerCannotMakeUsHoldUnboundedFields()
    {
        // The option-70 route is bounded by ProbeOptions.MaxSubnegotiationBytes. This one would
        // otherwise be bounded by nothing but how long a stranger cares to keep typing.
        var flood = new List<string> { "MSSP-REPLY-START" };
        for (var i = 0; i < PlaintextMssp.MaxFields * 3; i++)
        {
            flood.Add($"VAR{i}\tvalue");
        }

        var report = PlaintextMssp.Parse(flood);

        await Assert.That(report!.Count).IsLessThanOrEqualTo(PlaintextMssp.MaxFields);
    }

    [Test]
    public async Task AHostileServerCannotMakeUsHoldAnUnboundedValue()
    {
        var report = PlaintextMssp.Parse(
            ["MSSP-REPLY-START", "NAME\t" + new string('x', PlaintextMssp.MaxValueLength * 4)]);

        await Assert.That(report!["NAME"][0].Length).IsLessThanOrEqualTo(PlaintextMssp.MaxValueLength);
    }

    [Test]
    public async Task TheMarkersAreMatchedWithoutRegardToCase()
    {
        var report = PlaintextMssp.Parse(["mssp-reply-start", "NAME\tThe Game", "Mssp-Reply-End"]);

        await Assert.That(report).IsNotNull();
        await Assert.That(report!.Count).IsEqualTo(1);
    }
}
