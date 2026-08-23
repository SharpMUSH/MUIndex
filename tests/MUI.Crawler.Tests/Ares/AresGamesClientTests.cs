using System.Net;
using System.Text;

using MUI.Ares;

namespace MUI.Crawler.Tests;

/// <summary>
/// What the client presents, and what it refuses to return.
/// </summary>
/// <remarks>
/// The handler is faked rather than a loopback server: what is under test is the credential we send
/// and how a bad answer is treated, neither of which involves a socket. There is no
/// <c>MUI.Ares.Tests</c> for the same reason — <c>MUI.I3.Tests</c> exists because framing a protocol
/// is worth its own suite, and this is one GET and a deserialise.
/// </remarks>
public class AresGamesClientTests
{
    private const string OneGame = """
        [
          {
            "name": "Battlestar Pacifica",
            "description": "A **Markdown** blurb.",
            "hostname": "bsgpacifica.org",
            "port": 4201,
            "genre": "Sci-Fi",
            "website": "https://bsgpacifica.org",
            "last_ping": "08/21/2026",
            "status": "Open"
          }
        ]
        """;

    private static AresOptions Options() => new()
    {
        BaseAddress = new Uri("https://arescentral.aresmush.com/"),
        ClientId = "muindex",
        ApiKey = "not-a-real-key",
    };

    /// <summary>
    /// The documented credential shape, exactly: the client id and the key joined by a colon, inside
    /// one bearer header. Getting this subtly wrong reads as a revoked key.
    /// </summary>
    [Test]
    public async Task TheCredentialIsTheClientIdAndKeyJoinedByAColon()
    {
        await Assert.That(AresGamesClient.AuthorizationFor(Options()))
            .IsEqualTo("muindex:not-a-real-key");
    }

    [Test]
    public async Task TheCredentialTravelsAsABearerHeader()
    {
        var handler = new StubHandler(HttpStatusCode.OK, OneGame);

        await Client(handler).ListAsync();

        await Assert.That(handler.Sent!.Headers.Authorization!.Scheme).IsEqualTo("Bearer");
        await Assert.That(handler.Sent.Headers.Authorization.Parameter)
            .IsEqualTo("muindex:not-a-real-key");
    }

    [Test]
    public async Task AListedGameComesBackWithEveryFieldTheHubHolds()
    {
        var games = await Client(new StubHandler(HttpStatusCode.OK, OneGame)).ListAsync();

        var game = games.Single();
        await Assert.That(game.Name).IsEqualTo("Battlestar Pacifica");
        await Assert.That(game.Description).IsEqualTo("A **Markdown** blurb.");
        await Assert.That(game.Hostname).IsEqualTo("bsgpacifica.org");
        await Assert.That(game.Port).IsEqualTo(4201);
        await Assert.That(game.Genre).IsEqualTo("Sci-Fi");
        await Assert.That(game.Website).IsEqualTo("https://bsgpacifica.org");
        await Assert.That(game.Status).IsEqualTo("Open");
        await Assert.That(game.LastPing).IsEqualTo("08/21/2026");
    }

    /// <summary>
    /// A thin listing is a listing, not a parse failure. Every string the hub sends is a third
    /// party's optional field, and one absent one must not cost us the whole pass.
    /// </summary>
    [Test]
    public async Task AListingMissingMostOfItsFieldsStillParses()
    {
        var games = await Client(new StubHandler(
                HttpStatusCode.OK, """[{"hostname":"thin.example.org","port":4201}]"""))
            .ListAsync();

        var game = games.Single();
        await Assert.That(game.Hostname).IsEqualTo("thin.example.org");
        await Assert.That(game.Name).IsNull();
        await Assert.That(game.Description).IsNull();
    }

    /// <summary>
    /// A refusal is an exception and never an empty list. An empty list is a legitimate answer
    /// meaning the hub lists nothing, and a caller that cannot tell the two apart will read a failed
    /// fetch as every game having been delisted at once.
    /// </summary>
    [Test]
    public async Task ARefusedRequestThrowsRatherThanReturningNothing()
    {
        await Assert.That(async () =>
                await Client(new StubHandler(HttpStatusCode.Unauthorized, "nope")).ListAsync())
            .Throws<HttpRequestException>();
    }

    [Test]
    public async Task ABodyThatIsNotTheDocumentedShapeThrows()
    {
        await Assert.That(async () =>
                await Client(new StubHandler(HttpStatusCode.OK, """{"error":"maintenance"}""")).ListAsync())
            .ThrowsException();
    }

    /// <summary>
    /// The ceiling is ours and is enforced on the stream, not read off a header the far end sets.
    /// </summary>
    [Test]
    public async Task AnAnswerPastTheCeilingIsRefused()
    {
        var huge = "[" + string.Join(",", Enumerable.Repeat(
            """{"hostname":"x.example.org","port":1}""", 500)) + "]";

        await Assert.That(async () => await new AresGamesClient(
                    new HttpClient(new StubHandler(HttpStatusCode.OK, huge))
                    {
                        BaseAddress = new Uri("https://arescentral.aresmush.com/"),
                    },
                    Options() with { MaxResponseBytes = 128 })
                .ListAsync())
            .ThrowsException();
    }

    private static AresGamesClient Client(StubHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://arescentral.aresmush.com/") },
            Options());

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Sent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sent = request;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
