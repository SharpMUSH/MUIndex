namespace MUI.Crawl.Tests.WebSocket;

/// <summary>The MSSP <c>WEBSOCKET</c> variable, read as the two games that publish it write it.</summary>
public class WebSocketAddressesTests
{
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Report(params string[] values)
    {
        var report = new OrderedDictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [WebSocketAddresses.Variable] = values,
        };

        return report;
    }

    [Test]
    public async Task MumesAddressIsReadWithItsPathAndTheSchemesPort()
    {
        var found = WebSocketAddresses.From(Report("wss://mume.org/ws-play/"));

        await Assert.That(found).Count().IsEqualTo(1);
        await Assert.That(found[0]).IsEqualTo(new WebSocketAddress("mume.org", 443, true, "/ws-play/"));
        await Assert.That(found[0].Uri.ToString()).IsEqualTo("wss://mume.org/ws-play/");
    }

    [Test]
    public async Task AListIsReadInOrderOnceEach()
    {
        var found = WebSocketAddresses.From(Report(
            "wss://last-outpost.com/ws/telnet/, ws://Last-Outpost.com:8080/ws",
            "wss://last-outpost.com/ws/telnet/"));

        await Assert.That(found).Count().IsEqualTo(2);
        await Assert.That(found[0].Path).IsEqualTo("/ws/telnet/");
        await Assert.That(found[1]).IsEqualTo(new WebSocketAddress("last-outpost.com", 8080, false, "/ws"));
    }

    [Test]
    public async Task ABareHostIsThePathSlash()
    {
        var found = WebSocketAddresses.Parse("ws://mud.example.org:4000");

        await Assert.That(found).IsEqualTo(new WebSocketAddress("mud.example.org", 4000, false, "/"));
    }

    [Test]
    public async Task AnIpv6AddressIsKeptWithoutBracketsAndWrittenWithThem()
    {
        var found = WebSocketAddresses.Parse("wss://[2001:db8::1]:4443/play");

        await Assert.That(found!.Host).IsEqualTo("2001:db8::1");
        await Assert.That(found.Uri.ToString()).IsEqualTo("wss://[2001:db8::1]:4443/play");
    }

    [Test]
    [Arguments("https://mume.org/ws-play/")]
    [Arguments("telnet://mume.org:4242")]
    [Arguments("wss://user:secret@mume.org/ws")]
    [Arguments("wss://mume.org/ws#here")]
    [Arguments("wss://mume.org/ws play")]
    [Arguments("wss://mume.org/wsé")]
    [Arguments("mume.org/ws")]
    [Arguments("1")]
    [Arguments("")]
    public async Task WhatThisSiteWouldNotShowIsRefusedNotTidied(string value)
    {
        await Assert.That(WebSocketAddresses.Parse(value)).IsNull();
    }

    [Test]
    public async Task APathPastTheDatabasesLimitIsRefused()
    {
        var path = "/" + new string('a', WebSocketAddresses.MaxPathLength);

        await Assert.That(WebSocketAddresses.Parse($"wss://mume.org{path}")).IsNull();
    }

    [Test]
    public async Task AReportNamingManyAddressesIsCut()
    {
        var values = Enumerable.Range(1, 10).Select(n => $"wss://mud{n}.example.org/").ToArray();

        await Assert.That(WebSocketAddresses.From(Report(values))).Count().IsEqualTo(WebSocketAddresses.MaxAddresses);
    }

    [Test]
    public async Task NoVariableIsNoAddress()
    {
        await Assert.That(WebSocketAddresses.From(MsspReport.Empty)).IsEmpty();
        await Assert.That(WebSocketAddresses.From(null)).IsEmpty();
    }
}
