using System.Net;
using System.Net.Sockets;

using DnsClient;

using MUI.Crawler.Tests.Support;
using MUI.Discovery;

namespace MUI.Crawler.Tests;

/// <summary>
/// The live TXT resolver, against a nameserver this test runs itself.
/// </summary>
/// <remarks>
/// Real UDP, real wire format, no dependence on anybody's zone file. The rest of the opt-out is
/// tested against fakes, which proves the rules; this proves the thing that actually reads DNS reads
/// it correctly. The stub answers on loopback with an ephemeral port, so this needs no privileges, no
/// container and no network beyond the machine it runs on.
/// </remarks>
public class DnsTxtResolverTests
{
    private const string Name = "_muindex.corvid.example";

    private static DnsTxtResolver Against(StubNameServer server) =>
        new(new LookupClient(new LookupClientOptions(new NameServer(IPAddress.Loopback, server.Port))
        {
            UseCache = false,
            EnableAuditTrail = false,
            Retries = 0,
            Timeout = TimeSpan.FromSeconds(2),
            ThrowDnsErrors = false,
        }));

    [Test]
    public async Task ARecordPublishedOnARealNameserverIsReadAndUnderstood()
    {
        using var server = new StubNameServer();
        server.Publish(Name, OptOutVocabulary.DnsValue);

        var answer = await Against(server).LookupAsync(Name);

        await Assert.That(answer.Answered).IsTrue();
        await Assert.That(answer.Records).IsEquivalentTo(new[] { "opt-out" });

        // And the vocabulary reads it the same way it reads a fixture, which is the join this test
        // exists to check.
        await Assert.That(OptOutVocabulary.ReadDns(answer.Records, 4201)).IsNotNull();
    }

    [Test]
    public async Task ARecordLongerThanOneWireChunkComesBackAsOneString()
    {
        // TXT strings are chopped at 255 bytes on the wire. The chop is a transport detail and an
        // operator who pasted a long line did not type it, so a reader that returned the pieces would
        // fail to match a record that plainly says opt-out.
        using var server = new StubNameServer();

        var padding = new string('x', 300);
        server.Publish(Name, $"v=muindex1; note={padding}; opt-out");

        var answer = await Against(server).LookupAsync(Name);

        await Assert.That(answer.Records).Count().IsEqualTo(1);
        await Assert.That(answer.Records[0]).EndsWith("opt-out");
        await Assert.That(OptOutVocabulary.ReadDns(answer.Records, 4201)).IsNotNull();
    }

    [Test]
    public async Task NoSuchRecordIsAnAnswerWithNothingInIt()
    {
        // Which is what nearly every host in the registry replies, and it is the answer that lets an
        // opt-out be withdrawn. Reading it as a failure would mean never concluding anything.
        using var server = new StubNameServer();

        var answer = await Against(server).LookupAsync(Name);

        await Assert.That(answer.Answered).IsTrue();
        await Assert.That(answer.Records).IsEmpty();
    }

    [Test]
    public async Task AResolverThatDoesNotReplyIsNotAnAnswer()
    {
        // A timeout must never withdraw an opt-out somebody meant. The stub is stopped before the
        // question is asked, so nothing is listening on that port.
        var server = new StubNameServer();
        var port = server.Port;
        server.Dispose();

        var resolver = new DnsTxtResolver(new LookupClient(
            new LookupClientOptions(new NameServer(IPAddress.Loopback, port))
            {
                UseCache = false,
                Retries = 0,
                Timeout = TimeSpan.FromMilliseconds(400),
                ThrowDnsErrors = false,
            }));

        var answer = await resolver.LookupAsync(Name);

        await Assert.That(answer.Answered).IsFalse();
        await Assert.That(answer.Records).IsEmpty();
    }

    [Test]
    [Arguments("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.example.org")]
    [Arguments("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb."
        + "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb."
        + "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb."
        + "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb."
        + "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.example.org")]
    public async Task ANameDnsCannotEvenBeAskedAboutIsNoAnswerRatherThanAThrow(string host)
    {
        // A REFERRAL naming a 64-byte label, and a name legal at 243 bytes until our own "_muindex."
        // prefix pushes it over 255. DnsClient rejects both with ArgumentException before any packet
        // is sent; an escape here would land in the crawl loop's catch-all and leave the target due
        // forever.
        using var server = new StubNameServer();

        var answer = await Against(server).LookupAsync(OptOutVocabulary.DnsNameFor(host));

        await Assert.That(answer.Answered).IsFalse();
        await Assert.That(answer.Records).IsEmpty();
    }

    [Test]
    public async Task AResolverThatReceivesAndNeverAnswersIsBoundedByUsRatherThanWaitedOut()
    {
        // §12: the crawler shares a process with the web tier, so a bound on I/O is a correctness
        // requirement, not hygiene. The client is given thirty seconds on purpose — the resolver's
        // own budget is what has to end this.
        using var server = new StubNameServer { Silent = true };

        var resolver = new DnsTxtResolver(
            new LookupClient(new LookupClientOptions(new NameServer(IPAddress.Loopback, server.Port))
            {
                UseCache = false,
                Retries = 3,
                Timeout = TimeSpan.FromSeconds(30),
                ThrowDnsErrors = false,
            }),
            bound: TimeSpan.FromMilliseconds(300));

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var answer = await resolver.LookupAsync(Name);
        clock.Stop();

        await Assert.That(answer.Answered).IsFalse();
        await Assert.That(clock.Elapsed).IsLessThan(TimeSpan.FromSeconds(10));
        await Assert.That(server.Asked).IsNotEmpty();
    }

    [Test]
    public async Task TheCallersOwnCancellationIsNotSwallowedAsAnAnswer()
    {
        // A stopping host must stop this, and must not have its cancellation reported as a resolver
        // that stayed quiet — the two are told apart by which token fired.
        using var server = new StubNameServer { Silent = true };
        using var stopping = new CancellationTokenSource();

        var resolver = new DnsTxtResolver(
            new LookupClient(new LookupClientOptions(new NameServer(IPAddress.Loopback, server.Port))
            {
                UseCache = false,
                Retries = 0,
                Timeout = TimeSpan.FromSeconds(30),
                ThrowDnsErrors = false,
            }),
            bound: TimeSpan.FromSeconds(30));

        await stopping.CancelAsync();

        await Assert.That(async () => await resolver.LookupAsync(Name, stopping.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task TheNameAskedForIsTheNameWeTellOperatorsToPublish()
    {
        using var server = new StubNameServer();
        server.Publish(OptOutVocabulary.DnsNameFor("Corvid.Example."), OptOutVocabulary.DnsValue);

        var answer = await Against(server).LookupAsync(OptOutVocabulary.DnsNameFor("corvid.example"));

        await Assert.That(answer.Records).IsNotEmpty();
        await Assert.That(server.Asked).Contains("_muindex.corvid.example");
    }
}
