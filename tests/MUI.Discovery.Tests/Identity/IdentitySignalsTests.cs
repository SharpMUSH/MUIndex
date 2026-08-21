namespace MUI.Discovery.Tests;

/// <summary>
/// Reading a <see cref="DuplicateReview"/> row's evidence back off <c>signals</c>, so a hand-resolved
/// merge can carry it forward rather than inventing new evidence (spec §7.3, <see cref="ReviewMerge"/>).
/// </summary>
public class IdentitySignalsTests
{
    [Test]
    public async Task RoundTripsWhatToJsonWrote()
    {
        IdentitySignal[] signals = [new("BannerHash", 0.5, true), new("Endpoint", 1.0, false)];

        var json = IdentitySignals.ToJson(signals);
        var read = IdentitySignals.FromJson(json);

        await Assert.That(read).IsEquivalentTo(signals);
    }

    [Test]
    public async Task ANullPayloadIsAnEmptyList()
    {
        await Assert.That(IdentitySignals.FromJson(null)).IsEmpty();
    }

    [Test]
    public async Task AnEmptyPayloadIsAnEmptyList()
    {
        await Assert.That(IdentitySignals.FromJson("")).IsEmpty();
    }

    /// <summary>
    /// A merge must not refuse to complete because its own paper trail from an earlier pass turned
    /// out corrupted.
    /// </summary>
    [Test]
    public async Task MalformedJsonIsAnEmptyListNotAnException()
    {
        await Assert.That(IdentitySignals.FromJson("{not valid json")).IsEmpty();
    }
}
